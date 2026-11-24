using System.Numerics;
using System.Reflection;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class MateManagerTests
{
    private static readonly FieldInfo s_worldManagerInstance = typeof(Singleton<WorldManager>)
        .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
    private object _previousWorldManager;
    private bool _previousDebugInfo;

    [Before(Test)]
    public void SetUp()
    {
        _previousWorldManager = s_worldManagerInstance.GetValue(null);
        s_worldManagerInstance.SetValue(null, new WorldManager(null, null, null, null, null));
        _previousDebugInfo = AppConfiguration.Instance.DebugInfo;
        AppConfiguration.Instance.DebugInfo = false;
    }

    [After(Test)]
    public void TearDown()
    {
        AppConfiguration.Instance.DebugInfo = _previousDebugInfo;
        s_worldManagerInstance.SetValue(null, _previousWorldManager);
    }

    [Test]
    public async Task SummonedMountStartsWithinClientMountRangeAndCanBeRecalledWhileRidden()
    {
        ObjectIdManager.Instance.Initialize();
        TlIdManager.Instance.Initialize();
        var owner = new Character(new UnitCustomModelParams()) { Id = 1, ObjId = ObjectIdManager.Instance.GetNextId() };
        var world = new WorldInstance(new WorldTemplate(), 0, true, owner.Transform.InstanceId);
        var manager = world.MateManager = new MateManager(world);
        owner.ParentWorld = world;
        owner.Transform.Local.SetPosition(100f, 100f, 20f, 0f, 0f, 0.5f);
        owner.Mates = new CharacterMates(owner);
        world.AddObject(owner);

        var mate = new Mate
        {
            ObjId = ObjectIdManager.Instance.GetNextId(),
            TlId = (ushort)TlIdManager.Instance.GetNextId(),
            OwnerId = owner.Id,
            OwnerObjId = owner.ObjId,
            MateType = MateType.Ride,
            Template = new NpcTemplate { Scale = 1f },
            ParentWorld = world,
            Hp = 100
        };
        mate.Transform = owner.Transform.CloneDetached(mate);
        var connection = new GameConnection(null) { ActiveChar = owner };

        try
        {
            manager.AddActiveMateAndSpawn(owner, mate, new SummonMate());
            var distance = Vector3.Distance(owner.Transform.World.Position, mate.Transform.World.Position);
            await Assert.That(distance).IsGreaterThan(0f);
            await Assert.That(distance).IsLessThan(3f);

            var mountRequest = new PacketStream().Write(mate.TlId)
                .Write((byte)AttachPointKind.Driver).Write((byte)AttachUnitReason.MountMateLeft);
            new CSMountMatePacket { Connection = connection }.Read(new PacketStream(mountRequest.GetBytes()));
            await Assert.That(owner.IsRiding).IsTrue();
            await Assert.That(owner.Transform.Parent).IsEqualTo(mate.Transform);

            var recallRequest = new PacketStream().Write(mate.TlId);
            new CSRemoveMatePacket { Connection = connection }.Read(new PacketStream(recallRequest.GetBytes()));
            await Assert.That(owner.IsRiding).IsFalse();
            await Assert.That(owner.Transform.Parent).IsNull();
            await Assert.That(world.GetUnit(mate.ObjId)).IsNull();
            await Assert.That(manager.GetActiveMates(owner.Id)).IsEmpty();
        }
        finally
        {
            owner.Mates.DespawnMate(mate.TlId);
            world.RemoveObject(owner);
            WorldManager.Instance.TryRemoveCharacter(owner.ObjId);
            ObjectIdManager.Instance.ReleaseId(owner.ObjId);
        }
    }
}
