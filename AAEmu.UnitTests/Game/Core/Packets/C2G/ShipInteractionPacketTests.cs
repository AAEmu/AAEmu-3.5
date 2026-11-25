using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

[NotInParallel]
public class ShipInteractionPacketTests
{
    private const uint DismountSkillId = 35837;
    private readonly List<(FieldInfo Field, object Previous)> _previous = [];
    private readonly TestCharacter _player = new() { ObjId = 68884, Hp = 100, AccessLevel = 100 };
    private WorldInstance _world;
    private bool _debugInfo;

    [Before(Test)]
    public void SetUp()
    {
        _debugInfo = AppConfiguration.Instance.DebugInfo;
        AppConfiguration.Instance.DebugInfo = false;

        _world = new WorldInstance(new WorldTemplate(), 0, true, 0);
        _world.SlaveManager = new SlaveManager(_world);
        var worlds = new WorldManager(null, null, null, null, null);
        SetField(worlds, "_worlds", new ConcurrentDictionary<uint, WorldInstance> { [_world.Id] = _world });
        Install(worlds);
        _player.ParentWorld = _world;
        _player.Actability = new CharacterActability(_player);
        _world.AddObject(_player);

        var skills = new SkillManager(null, null);
        SetField(skills, "_skills", new Dictionary<uint, SkillTemplate>
        {
            [DismountSkillId] = new() { Id = DismountSkillId, TargetType = SkillTargetType.Slave, MaxRange = 4 }
        });
        SetField(skills, "_defaultSkills", new Dictionary<uint, DefaultSkill>());
        SetField(skills, "_skillReagents", new Dictionary<uint, SkillReagent>());
        SetField(skills, "_skillProducts", new Dictionary<uint, SkillProduct>());
        Install(skills);
    }

    [After(Test)]
    public void TearDown()
    {
        AppConfiguration.Instance.DebugInfo = _debugInfo;
        foreach (var (field, previous) in _previous)
            field.SetValue(null, previous);
    }

    [Test]
    public async Task AttachAndUnhang_OnShipLadderUseTheCapturedObjectIds()
    {
        var ladder = new Doodad { ObjId = 90426, ParentWorld = _world, IsVisible = true };
        ladder.CurrentFuncs.Add(new DoodadFunc { FuncType = nameof(DoodadFuncClimb) });
        _world.AddObject(ladder);
        var payload = new PacketStream(Convert.FromHexString("140D013A610100000000000000"));

        Read(new CSUnitAttachedPacket(), new PacketStream().WriteBc(_player.ObjId + 1).WriteBc(ladder.ObjId));
        await Assert.That(_player.Transform.StickyParent).IsNull();

        Read(new CSUnitAttachedPacket(), payload);

        await Assert.That(payload.Pos).IsEqualTo(6);
        await Assert.That(_player.Transform.StickyParent).IsSameReferenceAs(ladder.Transform);
        await Assert.That(_player.Packets.Exists(packet => packet is SCHungPacket)).IsTrue();

        Read(new CSUnhangPacket(), new PacketStream().WriteBc(_player.ObjId).Write(2u));

        await Assert.That(_player.Transform.StickyParent).IsNull();
        await Assert.That(_player.Packets.Exists(packet => packet is SCUnhungPacket)).IsTrue();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task StartSkill_DismountsTheRiderWithoutRemovingTheShip(bool owner)
    {
        var ship = CreateShip(owner ? _player : new Character(null) { ObjId = 333 });
        var seat = owner ? AttachPointKind.Driver : AttachPointKind.Passenger0;
        _player.AttachedPoint = seat;
        _player.Transform.Parent = ship.Transform;
        ship.AttachedCharacters[seat] = _player;

        Read(new CSStartSkillPacket(), new PacketStream().Write(DismountSkillId)
            .Write(new SkillCasterUnit(_player.ObjId)).Write(new SkillCastUnitTarget(ship.ObjId)).Write((byte)0));

        await Assert.That(ship.AttachedCharacters).IsEmpty();
        await Assert.That(_player.AttachedPoint).IsEqualTo(AttachPointKind.None);
        await Assert.That(_player.Transform.Parent).IsNull();
        await Assert.That(_player.Packets.Exists(packet => packet is SCUnitDetachedPacket)).IsTrue();
        await Assert.That(_world.GetBaseUnit(ship.ObjId)).IsSameReferenceAs(ship);
    }

    [Test]
    public async Task DismountSkill_RequiresASeatOnTheTargetShip()
    {
        var ship = CreateShip(_player);
        var authority = new SkillAuthority(SkillAuthorityGameData.Instance);

        var result = authority.Check(_player, new SkillCasterUnit(_player.ObjId), new SkillCastUnitTarget(ship.ObjId),
            SkillManager.Instance.GetSkillTemplate(DismountSkillId));

        await Assert.That(result).IsEqualTo(SkillResult.InvalidTarget);
    }

    [Test]
    public async Task DespawnSlave_QueuesTheOwnedShipAndConfirmsTheRecall()
    {
        using var physics = new PhysicsManager { SimulationWorld = _world };
        typeof(WorldInstance).GetProperty(nameof(WorldInstance.Physics))!.SetValue(_world, physics);
        _world.SpawnManager = new SpawnManager(_world);
        var ship = CreateShip(_player);

        Read(new CSDespawnSlavePacket(), new PacketStream().WriteBc(ship.ObjId));

        await Assert.That(_world.GetBaseUnit(ship.ObjId)).IsNull();
        await Assert.That(ship.Despawn).IsGreaterThan(DateTime.MinValue);
        await Assert.That(_player.Packets.Count).IsEqualTo(2);
        await Assert.That(_player.Packets[0]).IsTypeOf<SCSlaveDespawnPacket>();
        await Assert.That(_player.Packets[1]).IsTypeOf<SCSlaveRemovedPacket>();
    }

    [Test]
    public async Task DespawnSlave_WithCargoKeepsTheShipAndItsPassengers()
    {
        var ship = CreateShip(_player);
        ship.AddAttachedDoodad(new Doodad { ItemTemplateId = 1 });
        var passenger = new TestCharacter { ObjId = 333, AttachedPoint = AttachPointKind.Passenger0 };
        passenger.Transform.Parent = ship.Transform;
        ship.AttachedCharacters[AttachPointKind.Passenger0] = passenger;

        Read(new CSDespawnSlavePacket(), new PacketStream().WriteBc(ship.ObjId));

        await Assert.That(_world.GetBaseUnit(ship.ObjId)).IsSameReferenceAs(ship);
        await Assert.That(ship.Despawn).IsEqualTo(DateTime.MinValue);
        await Assert.That(passenger.Transform.Parent).IsSameReferenceAs(ship.Transform);
        await Assert.That(ship.AttachedCharacters[AttachPointKind.Passenger0]).IsSameReferenceAs(passenger);
    }

    private Slave CreateShip(Character summoner)
    {
        var ship = new Slave
        {
            ObjId = 46887, TlId = 11, Hp = 100, Template = new SlaveTemplate(), ParentWorld = _world, Summoner = summoner
        };
        _world.AddObject(ship);
        return ship;
    }

    private void Read(GamePacket packet, PacketStream payload)
    {
        payload.Rollback();
        packet.Connection = new GameConnection(null) { ActiveChar = _player };
        packet.Read(payload);
    }

    private void Install<T>(T instance) where T : Singleton<T>
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _previous.Add((field, field.GetValue(null)));
        field.SetValue(null, instance);
    }

    private static void SetField<T>(T instance, string name, object value) =>
        typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private sealed class TestCharacter() : Character(null)
    {
        public List<GamePacket> Packets { get; } = [];
        public override int MaxHp => 100;
        public override int MaxMp => 100;
        public override void BroadcastPacket(GamePacket packet, bool self) => Packets.Add(packet);
    }
}
