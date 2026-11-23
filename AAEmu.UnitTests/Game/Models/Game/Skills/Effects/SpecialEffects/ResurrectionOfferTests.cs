using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

[NotInParallel]
public class ResurrectionOfferTests
{
    private readonly List<(FieldInfo Field, object Previous)> _singletons = [];
    private WorldInstance _world;

    [Before(Test)]
    public void Setup()
    {
        var worlds = new WorldManager(null, null, null, null, null);
        var instanceId = WorldManager.DefaultInstanceId + 19;
        _world = new WorldInstance(new WorldTemplate(), 0, true, instanceId);
        _world.MateManager = new MateManager(_world);
        SetField(worlds, "_worlds", new ConcurrentDictionary<uint, WorldInstance> { [instanceId] = _world });
        Install(worlds);
        var zones = new ZoneManager(worlds);
        SetField(zones, "_zones", new Dictionary<uint, Zone>());
        Install(zones);
        var skills = new SkillManager(null, null);
        SetField(skills, "_buffs", new Dictionary<uint, BuffTemplate>());
        Install(skills);
    }

    [After(Test)]
    public void TearDown()
    {
        foreach (var (field, previous) in _singletons)
            field.SetValue(null, previous);
    }

    [Test]
    public async Task TheOfferPacketEndsWithTheClientInPlaceCounter()
    {
        var packet = new SCNotifyResurrectionPacket(new SkillCasterUnit(0x123456));
        var wire = ReadPayload(packet);

        await Assert.That(wire.ReadByte()).IsEqualTo((byte)SkillCasterType.Unit);
        await Assert.That(wire.ReadBc()).IsEqualTo(0x123456u);
        await Assert.That(wire.LeftBytes).IsEqualTo(4);
        await Assert.That(wire.ReadInt32()).IsEqualTo(0);
    }

    [Test]
    public async Task PortalAndNpcDeathsUseTheDefaultWaitAndAutoResurrectionTimes()
    {
        var npc = new PacketNpc { ObjId = 500, ParentWorld = _world };
        npc.DoDie(npc, KillReason.Unknown);

        var npcDeath = ReadPayload(npc.Packets.OfType<SCUnitDeathPacket>().Single());
        await Assert.That(npcDeath.ReadBc()).IsEqualTo(npc.ObjId);
        await Assert.That(npcDeath.ReadByte()).IsEqualTo((byte)KillReason.Unknown);
        await Assert.That(npcDeath.ReadUInt32()).IsEqualTo(15000u);
        await Assert.That(npcDeath.ReadUInt32()).IsEqualTo(75000u);
        await Assert.That(npcDeath.ReadInt32()).IsEqualTo(0);
        await Assert.That(npcDeath.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(npcDeath.ReadBc()).IsEqualTo(npc.ObjId);

        var portalDeath = ReadPayload(new SCUnitDeathPacket(19, KillReason.PortalTimeout));
        portalDeath.ReadBc();
        portalDeath.ReadByte();
        await Assert.That(portalDeath.ReadUInt32()).IsEqualTo(15000u);
        await Assert.That(portalDeath.ReadUInt32()).IsEqualTo(75000u);
        await Assert.That(portalDeath.ReadInt32()).IsEqualTo(0);
        await Assert.That(portalDeath.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(portalDeath.ReadBc()).IsEqualTo(0u);
        await Assert.That(portalDeath.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task EachDeathSendsThePlayersComputedWaitBeforeTheFiveMinuteAutoTimer()
    {
        var character = MakeCharacter();
        var killer = new PacketNpc { ObjId = 500, ParentWorld = _world };
        character.DoDie(killer, KillReason.Unknown);
        Reply(character, inPlace: false);
        character.Hp = 0;
        character.DoDie(killer, KillReason.Unknown);

        await Assert.That(character.RezWaitDuration).IsEqualTo(30000);
        var death = ReadPayload(killer.Packets.OfType<SCUnitDeathPacket>().Last());
        await Assert.That(death.ReadBc()).IsEqualTo(character.ObjId);
        death.ReadByte();
        await Assert.That(death.ReadUInt32()).IsEqualTo(30000u);
        await Assert.That(death.ReadUInt32()).IsEqualTo(300000u);
    }

    [Test]
    public async Task AnAliveCharactersReplyDoesNotChangeVitalsOrEmitARevival()
    {
        var character = MakeCharacter();
        character.Hp = 70;
        character.Mp = 80;

        Reply(character, inPlace: true);

        await Assert.That(character.Hp).IsEqualTo(70);
        await Assert.That(character.Mp).IsEqualTo(80);
        await Assert.That(character.Packets.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TheSelfRevivalScrollOfferRestoresEightyPercentWhereTheCharacterFell()
    {
        var character = MakeCharacter();
        var priestess = new Npc { ObjId = 502, TemplateId = 502, ParentWorld = _world };
        priestess.Transform.Local.SetPosition(700, 800, 900);
        _world.AddObject(priestess);
        CastOffer(character, character, new SkillItem(character.ObjId, 0, 34018), 26647, 80);

        Reply(character, inPlace: true);

        await Assert.That(character.Hp).IsEqualTo(80);
        await Assert.That(character.Mp).IsEqualTo(160);
        var revival = ReadPayload(character.Packets.OfType<SCCharacterResurrectedPacket>().Single());
        await Assert.That(revival.ReadBc()).IsEqualTo(character.ObjId);
        var (x, y, z) = revival.ReadPosition();
        await Assert.That(x).IsEqualTo(40.0f);
        await Assert.That(y).IsEqualTo(50.0f);
        // The nine-byte position encodes Z in steps of approximately one millimeter.
        await Assert.That(Math.Abs(z - 60.0f)).IsLessThan(0.001f);
    }

    [Test]
    public async Task ChoosingTheTempleDiscardsTheOfferBeforeTheNextDeath()
    {
        var character = MakeCharacter();
        var healer = MakeCharacter(72);
        CastOffer(healer, character, new SkillCasterUnit(healer.ObjId), 10546, 40);

        Reply(character, inPlace: false);
        await Assert.That(character.Hp).IsEqualTo(10);
        character.Hp = 0;
        Reply(character, inPlace: true);

        await Assert.That(character.Hp).IsEqualTo(1);
        await Assert.That(character.Mp).IsEqualTo(2);
    }

    [Test]
    public async Task ANewDeathDiscardsAnOfferThatWasNeverAccepted()
    {
        var character = MakeCharacter();
        var healer = MakeCharacter(72);
        CastOffer(healer, character, new SkillCasterUnit(healer.ObjId), 10546, 40);
        character.Hp = 100;
        character.Hp = 0;
        character.DoDie(new PacketNpc { ObjId = 500, ParentWorld = _world }, KillReason.Unknown);

        Reply(character, inPlace: true);

        await Assert.That(character.Hp).IsEqualTo(1);
        await Assert.That(character.Mp).IsEqualTo(2);
    }

    private static void Reply(PacketCharacter character, bool inPlace) =>
        new CSResurrectCharacterPacket { Connection = new GameConnection(null) { ActiveChar = character } }
            .Read(new PacketStream([inPlace ? (byte)1 : (byte)0]));

    private static void CastOffer(Character caster, Character target, SkillCaster source, uint skillId, int percent)
    {
        var skill = new Skill(new SkillTemplate { Id = skillId });
        var effect = new SpecialEffect { SpecialEffectTypeId = SpecialType.Resurrection, Value1 = 1, Value2 = percent, Value3 = percent };
        effect.Apply(caster, source, target, new SkillCastUnitTarget(target.ObjId), new CastSkill(skillId, 11),
            new EffectSource(skill), null, DateTime.UtcNow);
    }

    private PacketCharacter MakeCharacter(uint objId = 71)
    {
        var character = new PacketCharacter
        {
            Id = objId, ObjId = objId, ParentWorld = _world, Faction = new SystemFaction { Id = FactionsEnum.Friendly }
        };
        character.Transform.Local.SetPosition(40, 50, 60);
        character.Inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        SetField(character.Inventory, "<Equipment>k__BackingField", character.Equipment);
        return character;
    }

    private static PacketStream ReadPayload(GamePacket packet) => new(packet.Write(new PacketStream()).GetBytes());

    private void Install<T>(T instance) where T : class
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _singletons.Add((field, field.GetValue(null)));
        field.SetValue(null, instance);
    }

    private static void SetField<T>(T instance, string field, object value) =>
        typeof(T).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private sealed class PacketCharacter() : Character(new UnitCustomModelParams())
    {
        public override int MaxHp => 100;
        public override int MaxMp => 200;
        public List<GamePacket> Packets { get; } = [];
        public override void BroadcastPacket(GamePacket packet, bool self) => Packets.Add(packet);
    }

    private sealed class PacketNpc : Npc
    {
        public List<GamePacket> Packets { get; } = [];
        public override void BroadcastPacket(GamePacket packet, bool self) => Packets.Add(packet);
    }
}
