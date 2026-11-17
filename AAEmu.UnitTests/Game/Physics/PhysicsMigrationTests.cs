using System.Collections.Concurrent;
using System.Numerics;
using System.Text;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Xml;
using AAEmu.Game.Physics.Hulls;
using AAEmu.Game.Physics.Static;
using AAEmu.Geometry.Abstractions;
using AAEmu.UnitTests.Geometry;

namespace AAEmu.UnitTests.Game.Physics;

public class PhysicsMigrationTests
{
    [Test]
    public async Task AddShip_WaitsForAndUsesAsynchronouslyResolvedBodyBounds()
    {
        using var gate = new ManualResetEventSlim(false);
        var resolver = new ShipHullBoundsResolver(new BoundsFiles(gate));
        var template = new WorldTemplate { Id = 1, Name = "test", OceanLevel = 100f };
        var world = new WorldInstance(template, 0, true, 1);
        world.Water.OceanLevel = 100f;
        using var hulls = new FittedHullPhysics(_ => null);
        using var physics = new PhysicsManager(hulls, resolver) { SimulationWorld = world };
        physics.Initialize();
        var first = new Slave { ObjId = 10, SpawnTime = DateTime.UtcNow };
        var second = new Slave { ObjId = 11, SpawnTime = DateTime.UtcNow };
        first.Transform.Local.SetPosition(1000f, 1000f, 100f);
        second.Transform.Local.SetPosition(1000f, 1030f, 100f);
        const string uri = "prefab://prefabs/hull.xml/ship";
        var now = Environment.TickCount64;
        try
        {
            physics.AddShip(first, new ShipModelV1 { Id = 1, Mass = 15000f, Normal = uri });
            physics.AddShip(second, new ShipModelV1 { Id = 2, Mass = 15000f });
            physics.Tick(now);
            physics.Tick(now + 100);
            await Assert.That(hulls.ContactSteps).IsEqualTo(0L);
            gate.Set();
            var size = await resolver.ResolveAsync(uri);
            await Assert.That(size.Y).IsEqualTo(44f).Within(0.001f);
            physics.Tick(now + 200);
            physics.Tick(now + 300);
            await Assert.That(hulls.ContactSteps).IsGreaterThan(0L);
        }
        finally { gate.Set(); }
    }

    private sealed class BoundsFiles(ManualResetEventSlim gate) : IGeometryFileSource
    {
        public byte[] TryReadBytes(string path)
        {
            gate.Wait(TimeSpan.FromSeconds(10));
            return path switch
            {
                "game/prefabs/hull.xml" => Encoding.UTF8.GetBytes("<Prefabs><Prefab Name='ship'><Objects><Object Type='Brush' Name='body' Prefab='objects/test.cgf' Scale='6,22,1'/></Objects></Prefab></Prefabs>"),
                "game/objects/test.cgf" => CgfFixture.WithBoxProxy(),
                _ => null
            };
        }
    }

    [Test]
    public async Task AddShip_PassesSlaveBoundsToHullContacts()
    {
        var template = new WorldTemplate { Id = 1, Name = "test", OceanLevel = 100f };
        var world = new WorldInstance(template, 0, true, 1);
        world.Water.OceanLevel = 100f;
        using var hulls = new FittedHullPhysics(_ => null);
        using var physics = new PhysicsManager(hulls) { SimulationWorld = world };
        physics.Initialize();
        var first = new Slave { ObjId = 10, SpawnTime = DateTime.UtcNow, Template = new SlaveTemplate { ObbSize = new Vector3(12f, 44f, 20f) } };
        var second = new Slave { ObjId = 11, SpawnTime = DateTime.UtcNow };
        first.Transform.Local.SetPosition(1000f, 1000f, 100f);
        second.Transform.Local.SetPosition(1000f, 1030f, 100f);
        var model = new ShipModelV1 { Id = 3, Mass = 15000f };
        physics.AddShip(first, model);
        physics.AddShip(second, model);
        physics.Tick(Environment.TickCount64 + 100);
        await Assert.That(hulls.ContactSteps).IsGreaterThan(0L);
    }

    [Test]
    public async Task NewShip_WaitsForNearbyZoneThenMovesAfterItLoads()
    {
        using var gate = new ManualResetEventSlim(false);
        var template = new WorldTemplate { Id = 1, Name = "test", OceanLevel = 100f, ZoneKeyByRegions = new uint[32, 32] };
        template.ZoneKeyByRegions[15, 15] = 137;
        template.XmlWorldZones = new ConcurrentDictionary<uint, XmlWorldZone>
        {
            [137] = new XmlWorldZone { Id = 137, Cells = new ConcurrentDictionary<(int, int), XmlWorldCell> { [(0, 0)] = new XmlWorldCell() } }
        };
        var geometry = new StaticGeometryManager(new WaitingFiles(gate));
        var world = new WorldInstance(template, 0, true, 1);
        world.Water.OceanLevel = 100f;
        using var physics = new PhysicsManager { SimulationWorld = world };
        physics.Initialize();
        physics.AttachStaticGeometry(geometry);
        var ship = new Slave { ObjId = 10, SpawnTime = DateTime.UtcNow };
        ship.Transform.Local.SetPosition(1000f, 1000f, 100f);
        var model = new ShipModelV1 { Id = 3, Mass = 15000f, MassBoxSizeX = 4f, MassBoxSizeY = 8f, MassBoxSizeZ = 2.5f };
        var now = Environment.TickCount64;
        try
        {
            physics.AddShip(ship, model);
            physics.Tick(now);
            var initial = ship.PhysicsState.Pose;
            physics.ApplyImpulse(ship, new HullImpulse(new Vector3(0f, 4f, 0f), Vector3.Zero, Vector3.Zero, Vector3.Zero));
            physics.Tick(now + 500);
            await Assert.That(ship.PhysicsState.Pose.Y).IsEqualTo(initial.Y);
            gate.Set();
            await geometry.EnsureZoneAsync(template, 137);
            physics.Tick(now + 1000);
            physics.ApplyImpulse(ship, new HullImpulse(new Vector3(0f, 4f, 0f), Vector3.Zero, Vector3.Zero, Vector3.Zero));
            physics.Tick(now + 1100);
            await Assert.That(ship.PhysicsState.Pose.Y).IsGreaterThan(initial.Y);
        }
        finally { gate.Set(); }
    }

    private sealed class WaitingFiles(ManualResetEventSlim gate) : IGeometryFileSource
    {
        public byte[] TryReadBytes(string path) { gate.Wait(TimeSpan.FromSeconds(10)); return null; }
    }

    [Test]
    public async Task GameAssembly_DoesNotDependOnJitter()
    {
        var references = typeof(PhysicsManager).Assembly.GetReferencedAssemblies();
        var jitter = false;
        foreach (var reference in references)
            jitter |= reference.Name == "Jitter2";
        await Assert.That(jitter).IsFalse();
    }

    [Test]
    public async Task AddThenRemoveBeforeTick_DoesNotLeaveASimulatedHull()
    {
        var template = new WorldTemplate { Id = 1, Name = "test", OceanLevel = 100f };
        var world = new WorldInstance(template, 0, true, 1);
        world.Water.OceanLevel = 100f;
        using var physics = new PhysicsManager { SimulationWorld = world };
        physics.Initialize();
        var ship = new Slave { ObjId = 10, SpawnTime = DateTime.UtcNow };
        ship.Transform.Local.SetPosition(100f, 100f, 100f);
        var model = new ShipModelV1 { Id = 3, Mass = 15000f, MassBoxSizeX = 4f, MassBoxSizeY = 8f, MassBoxSizeZ = 2.5f };

        physics.AddShip(ship, model);
        physics.RemoveShip(ship);
        physics.Tick(Environment.TickCount64);

        await Assert.That(physics.ShipCount).IsEqualTo(0);
        await Assert.That(ship.PhysicsState).IsNull();
    }
}
