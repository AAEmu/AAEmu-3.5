using System.Collections.Concurrent;
using System.Numerics;
using System.Text;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Game.World.Xml;
using AAEmu.Game.Physics.Hulls;
using AAEmu.Game.Physics.Static;
using AAEmu.Geometry.Abstractions;
using AAEmu.UnitTests.Geometry;

namespace AAEmu.UnitTests.Game.Physics;

[NotInParallel]
public class PhysicsMigrationTests
{
    [Test]
    public async Task Tick_AppliesLoadFromHullPartsAndUniqueSeatedRidersToQueuedImpulse()
    {
        var world = new WorldInstance(new WorldTemplate { Id = 1, Name = "test", OceanLevel = 100f }, 0, true, 1);
        world.Water.OceanLevel = 100f;
        using var physics = new PhysicsManager { SimulationWorld = world };
        physics.Initialize();
        var ship = new Slave { ObjId = 10, Hp = 100, Template = new SlaveTemplate { Capacity = 1, AddedShipMassPerUnit = 0.5f } };
        var cannon = new Slave { ObjId = 11 };
        var gunner = new Character(null) { ObjId = 12 };
        var passenger = new Character(null) { ObjId = 13 };
        var departedPassenger = new Character(null) { ObjId = 14 };
        ship.Transform.Local.SetPosition(1000f, 1000f, 100f);
        ship.AttachedSlaves.Add(cannon);
        cannon.Transform.Parent = ship.Transform;
        AddMassBonus(ship, 1000);
        AddMassBonus(cannon, 2000);
        AddMassBonus(gunner, 1000);
        AddMassBonus(passenger, 500);
        AddMassBonus(departedPassenger, 500);

        var now = Environment.TickCount64;
        physics.AddShip(ship, new ShipModelV1 { Id = 3, Mass = 15000f });
        physics.Tick(now);
        cannon.AttachedCharacters[AttachPointKind.Driver] = gunner;
        ship.AttachedCharacters[AttachPointKind.Driver] = gunner;
        ship.AttachedCharacters[AttachPointKind.Passenger0] = passenger;
        ship.AttachedCharacters[AttachPointKind.Passenger1] = departedPassenger;
        using var gunnerAttachment = AttachCharacter(gunner, cannon.Transform);
        using var passengerAttachment = AttachCharacter(passenger, ship.Transform);
        physics.ApplyImpulse(ship, new HullImpulse(Vector3.Zero, Vector3.Zero, new Vector3(0f, 54000f, 0f), Vector3.Zero));
        physics.Tick(now);

        await Assert.That(ship.PhysicsState.Pose.Velocity.Y).IsEqualTo(2f).Within(0.00001f);
    }

    [Test]
    [Arguments(true, 17000f)]
    [Arguments(false, 15000f)]
    public async Task HullContact_ReportsOnlyTheModelMassOfPartsStillFitted(bool remainsFitted, float expectedMass)
    {
        var world = new WorldInstance(new WorldTemplate { Id = 1, Name = "test", OceanLevel = 100f }, 0, true, 1);
        world.Water.OceanLevel = 100f;
        using var hulls = new FittedHullPhysics(_ => null);
        using var physics = new PhysicsManager(hulls) { SimulationWorld = world };
        physics.Initialize();
        var first = new Slave { ObjId = 10, Hp = 100 };
        var target = new Slave { ObjId = 11, Hp = 100 };
        var cannon = new Slave { ObjId = 12, FittedCollisionMass = 2000f };
        AddMassBonus(cannon, 4000);
        target.AttachedSlaves.Add(cannon);
        first.Transform.Local.SetPosition(1000f, 1000f, 100f);
        target.Transform.Local.SetPosition(1000f, 1010f, 100f);
        cannon.Transform.Parent = target.Transform;
        var model = new ShipModelV1 { Id = 3, Mass = 15000f };
        var now = Environment.TickCount64;
        now -= now % HullCollisionReporter.IntervalMs;

        physics.AddShip(first, model);
        physics.AddShip(target, model);
        physics.Tick(now);
        var windows = new List<HullCollisionWindow>();
        hulls.TakeCollisionWindows(first.ObjId, windows);
        windows.Clear();
        if (!remainsFitted)
            cannon.Transform.Parent = null;
        physics.ApplyImpulse(first, new HullImpulse(new Vector3(0f, 5f, 0f), Vector3.Zero, Vector3.Zero, Vector3.Zero));
        physics.Tick(now + 50);
        hulls.TakeCollisionWindows(first.ObjId, windows);

        await Assert.That(windows).HasSingleItem();
        await Assert.That(windows[0].SourceBcId).IsEqualTo(target.ObjId);
        await Assert.That(windows[0].SourceMass).IsEqualTo(expectedMass);
    }

    [Test]
    public async Task ShipDrivePercentModifiersAddBeforeScalingTheFlatAttribute()
    {
        var ship = new Slave();
        ship.AddBonus(1, new Bonus
        {
            Template = new BonusTemplate { Attribute = UnitAttribute.MoveSpeedMul, ModifierType = UnitModifierType.Value },
            Value = 500
        });
        for (uint id = 2; id <= 3; id++)
            ship.AddBonus(id, new Bonus
            {
                Template = new BonusTemplate { Attribute = UnitAttribute.MoveSpeedMul, ModifierType = UnitModifierType.Percent },
                Value = 50
            });

        var attributes = PhysicsManager.GetShipDriveAttributes(ship, canDrive: true, canTurn: true);
        await Assert.That(attributes.SpeedMultiplier).IsEqualTo(1f);
    }

    [Test]
    public async Task ShipDriveMultipliesSteeringAndReverseBonusesByTheAdjustedFacets()
    {
        var ship = new Slave { Facets = 2500 };
        Add(UnitAttribute.Facets, 500);
        Add(UnitAttribute.SteeringSpeedMul, 400);
        Add(UnitAttribute.SteeringSpeed, 200);
        Add(UnitAttribute.ReverseVelocityMul, 200);
        Add(UnitAttribute.ReverseVelocity, 400);

        var attributes = PhysicsManager.GetShipDriveAttributes(ship, canDrive: true, canTurn: true);

        await Assert.That(attributes.SteeringBonus).IsEqualTo(1.4f).Within(0.00001f);
        await Assert.That(attributes.ReverseSpeedBonus).IsEqualTo(1f).Within(0.00001f);

        void Add(UnitAttribute attribute, int amount) => ship.AddBonus(0, new Bonus
        {
            Template = new BonusTemplate { Attribute = attribute, ModifierType = UnitModifierType.Value },
            Value = amount
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Tick_CountsUnitsHangingOrBondedToDoodadsForOverloadOnly(bool bonded)
    {
        var world = new WorldInstance(new WorldTemplate { Id = 1, Name = "test", OceanLevel = 100f }, 0, true, 1);
        world.Water.OceanLevel = 100f;
        using var physics = new PhysicsManager { SimulationWorld = world };
        physics.Initialize();
        var ship = new Slave { ObjId = 10, Hp = 100, Template = new SlaveTemplate { Capacity = 1, AddedShipMassPerUnit = 0.5f } };
        ship.Transform.Local.SetPosition(1000f, 1000f, 100f);
        var ladder = new Doodad { ObjId = 11 };
        ladder.Transform = ship.Transform.CloneAttached(ladder);
        ship.AddAttachedDoodad(ladder);
        var seated = new Character(null) { ObjId = 12 };
        var passenger = new Character(null) { ObjId = 13 };
        AddMassBonus(passenger, 3000);

        var now = Environment.TickCount64;
        physics.AddShip(ship, new ShipModelV1 { Id = 3, Mass = 15000f });
        physics.Tick(now);
        ship.AttachedCharacters[AttachPointKind.Driver] = seated;
        using var seatAttachment = AttachCharacter(seated, ship.Transform);
        if (bonded)
            passenger.Bonding = new BondDoodad(ladder, AttachPointKind.Passenger0, BondKind.BondChairSingle, 1, 0);
        using var passengerAttachment = AttachCharacter(passenger, ladder.Transform, sticky: !bonded);
        physics.ApplyImpulse(ship, new HullImpulse(Vector3.Zero, Vector3.Zero, new Vector3(0f, 45000f, 0f), Vector3.Zero));
        physics.Tick(now);

        await Assert.That(ship.PhysicsState.Pose.Velocity.Y).IsEqualTo(2f).Within(0.00001f);
    }

    [Test]
    [Arguments((sbyte)127)]
    [Arguments((sbyte)-127)]
    public async Task Helm_AcceleratesAnOverloadedShipLessAtTheSameThrottle(sbyte throttle)
    {
        var world = new WorldInstance(new WorldTemplate { Id = 1, Name = "test", OceanLevel = 100f }, 0, true, 1);
        world.Water.OceanLevel = 100f;
        using var physics = new PhysicsManager { SimulationWorld = world };
        physics.Initialize();
        var template = new SlaveTemplate { Capacity = 1, AddedShipMassPerUnit = 0.5f };
        var light = new Slave { ObjId = 10, Hp = 100, Template = template };
        var loaded = new Slave { ObjId = 11, Hp = 100, Template = template };
        light.Transform.Local.SetPosition(1000f, 1000f, 100f);
        loaded.Transform.Local.SetPosition(2000f, 1000f, 100f);
        light.AttachedCharacters[AttachPointKind.Driver] = new Character(null) { ObjId = 12 };
        loaded.AttachedCharacters[AttachPointKind.Driver] = new Character(null) { ObjId = 13 };
        loaded.AttachedCharacters[AttachPointKind.Passenger0] = new Character(null) { ObjId = 14 };
        loaded.AttachedCharacters[AttachPointKind.Passenger1] = new Character(null) { ObjId = 15 };
        using var lightDriver = AttachCharacter(light.AttachedCharacters[AttachPointKind.Driver], light.Transform);
        using var loadedDriver = AttachCharacter(loaded.AttachedCharacters[AttachPointKind.Driver], loaded.Transform);
        using var firstPassenger = AttachCharacter(loaded.AttachedCharacters[AttachPointKind.Passenger0], loaded.Transform);
        using var secondPassenger = AttachCharacter(loaded.AttachedCharacters[AttachPointKind.Passenger1], loaded.Transform);
        var model = new ShipModelV1 { Id = 3, Mass = 15000f, ReverseVelocity = 5f };
        var now = Environment.TickCount64;
        physics.AddShip(light, model);
        physics.AddShip(loaded, model);
        physics.Tick(now);

        physics.SetHelm(light, throttle, 0);
        physics.SetHelm(loaded, throttle, 0);
        for (var elapsed = 20; elapsed <= 1000; elapsed += 20)
            physics.Tick(now + elapsed);

        var direction = Math.Sign(throttle);
        var lightSpeed = light.PhysicsState.Pose.Velocity.Y * direction;
        var loadedSpeed = loaded.PhysicsState.Pose.Velocity.Y * direction;
        await Assert.That(lightSpeed).IsGreaterThan(0.1f);
        await Assert.That(loadedSpeed).IsGreaterThan(0.1f);
        await Assert.That(loadedSpeed).IsLessThan(lightSpeed * 0.8f);
    }

    private static void AddMassBonus(Unit unit, int mass) => unit.AddBonus(0, new Bonus
    {
        Template = new BonusTemplate { Attribute = UnitAttribute.Mass, ModifierType = UnitModifierType.Value },
        Value = mass
    });

    private static IDisposable AttachCharacter(Character character, Transform parent, bool sticky = false)
    {
        SetCharacterParent(character, parent, sticky);
        return new CharacterAttachment(character, sticky);
    }

    private sealed class CharacterAttachment(Character character, bool sticky) : IDisposable
    {
        public void Dispose() => SetCharacterParent(character, null, sticky);
    }

    private static void SetCharacterParent(Character character, Transform parent, bool sticky)
    {
        var config = AppConfiguration.Instance;
        var debugInfo = config.DebugInfo;
        try
        {
            config.DebugInfo = false;
            if (sticky)
                character.Transform.StickyParent = parent;
            else
                character.Transform.Parent = parent;
        }
        finally
        {
            config.DebugInfo = debugInfo;
        }
    }

    [Test]
    public async Task AddShip_WaitsForAndUsesAsynchronouslyResolvedBodyBounds()
    {
        using var gate = new ManualResetEventSlim(false);
        var resolver = new ShipHullGeometryResolver(new BoundsFiles(gate));
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
            var geometry = await resolver.ResolveAsync(uri);
            await Assert.That(geometry.Bounds.Y).IsEqualTo(44f).Within(0.001f);
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
    [Arguments(0, 100f)]
    [Arguments(2000, 99.5f)]
    public async Task AddShip_FloatsAtTheDisplacementOfItsPrefabBuoy(int equipmentMass, float expectedZ)
    {
        var world = new WorldInstance(new WorldTemplate { Id = 1, Name = "test", OceanLevel = 100f }, 0, true, 1);
        world.Water.OceanLevel = 100f;
        var resolver = new ShipHullGeometryResolver(new BuoyFiles());
        using var physics = new PhysicsManager(hullGeometry: resolver) { SimulationWorld = world };
        physics.Initialize();
        var ship = new Slave { ObjId = 10, Hp = 100, Template = new SlaveTemplate { ObbSize = new Vector3(8f, 20f, 6f) } };
        ship.Transform.Local.SetPosition(1000f, 1000f, 100.25f);
        AddMassBonus(ship, equipmentMass);
        const string uri = "prefab://prefabs/floating.xml/ship";
        await resolver.ResolveAsync(uri, 1f, 2f, 2f);
        var now = Environment.TickCount64;
        physics.AddShip(ship, new ShipModelV1
        {
            Id = 3, Normal = uri, Mass = 4000f, WaterDensity = 1f,
            TubeRadius = 1f, TubeLength = 2f, TubeOffsetZ = 2f
        });

        for (var elapsed = 0; elapsed <= 6000; elapsed += 20)
            physics.Tick(now + elapsed);

        // A 2x2x2 buoy needs 4 m³ under water for the bare hull and 6 m³ for the fitted hull.
        await Assert.That(ship.PhysicsState.Pose.Z).IsEqualTo(expectedZ).Within(0.015f);
    }

    [Test]
    [Arguments("ship", 1f, 2f, 2f, 2000f, 98f)]
    [Arguments("missing", 1f, 2f, 2f, 2000f, 98f)]
    [Arguments("", 1f, 2f, 2f, 2000f, 98f)]
    [Arguments("missing", 1f, 0f, 2f, 2000f, 99.587f)]
    [Arguments("animated", 0f, 0f, 0f, 16000f, 98f)]
    [Arguments("mixed", 1f, 0f, 0f, 2500f, 98.5f)]
    public async Task AddShip_UsesAuthoredOrDerivedTubeDimensions(string prefabName, float radius, float halfLength, float offset,
        float massOverPi, float expectedZ)
    {
        var world = new WorldInstance(new WorldTemplate { Id = 1, Name = "test", OceanLevel = 100f }, 0, true, 1);
        world.Water.OceanLevel = 100f;
        var resolver = new ShipHullGeometryResolver(new TubeFiles());
        using var physics = new PhysicsManager(hullGeometry: resolver) { SimulationWorld = world };
        physics.Initialize();
        var ship = new Slave { ObjId = 10, Hp = 100, Template = new SlaveTemplate { ObbSize = new Vector3(8f, 20f, 6f) } };
        ship.Transform.Local.SetPosition(1000f, 1000f, 98.25f);
        var uri = prefabName.Length == 0 ? string.Empty : "prefab://prefabs/tube.xml/" + prefabName;
        await resolver.ResolveAsync(uri, radius, halfLength, offset);
        var now = Environment.TickCount64;
        physics.AddShip(ship, new ShipModelV1
        {
            Id = 3, Normal = uri, Mass = massOverPi * MathF.PI, WaterDensity = 1f,
            TubeRadius = radius, TubeLength = halfLength, TubeOffsetZ = offset
        });

        for (var elapsed = 0; elapsed <= 6000; elapsed += 20)
            physics.Tick(now + elapsed);

        // The authored and derived cylinders each hold twice the hull mass at this waterline.
        await Assert.That(ship.PhysicsState.Pose.Z).IsEqualTo(expectedZ).Within(0.015f);
    }

    private sealed class TubeFiles : IGeometryFileSource
    {
        public byte[] TryReadBytes(string path) => path switch
        {
            "game/prefabs/tube.xml" => Encoding.UTF8.GetBytes("<Prefabs><Prefab Name='ship'><Objects><Object Type='Brush' Name='body' Prefab='objects/ship.cgf'/></Objects></Prefab>" +
                "<Prefab Name='animated'><Objects>" + Animated + "</Objects></Prefab>" +
                "<Prefab Name='mixed'><Objects><Object Type='Brush' Name='slot' Pos='4,2,0' Prefab='objects/placed.chr'/>" +
                "<Object Type='Brush' Name='ignore_slotbrush_test' Pos='100,100,0' Prefab='objects/placed.chr'/>" +
                "<Object Type='Brush' Pos='100,100,0' Prefab='objects/lod.cgf'/><Object Type='Brush' Pos='100,100,0' Prefab='objects/empty.cgf'/>" +
                "<Object Type='Entity' EntityClass='AnimObject'><Properties object_Model='objects/broken.cdf'/></Object>" +
                Animated + "</Objects></Prefab></Prefabs>"),
            "game/objects/ship.cgf" => CgfFixture.WithBoxProxy(),
            "game/objects/placed.cdf" => Encoding.UTF8.GetBytes("<CharacterDefinition><Model File='Objects/Placed.chr'/></CharacterDefinition>"),
            "game/objects/placed.chr" => CgfFixture.WithPlacedRenderBounds(),
            "game/objects/lod.cgf" => CgfFixture.WithPlacedRenderBounds(nodeName: "$lod1"),
            "game/objects/empty.cgf" => CgfFixture.WithPlacedRenderBounds(vertexCount: 0),
            "game/objects/broken.cdf" => Encoding.UTF8.GetBytes("<CharacterDefinition>"),
            _ => null
        };

        private const string Animated = "<Object Type='Entity' EntityClass='AnimObject' Pos='3,5,0'><Properties object_Model='objects/placed.cdf'/></Object>";
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WorldSettingSelectsAuthoredDriveAndUsesSlaveSpeedAttributes(bool enabled)
    {
        var world = new WorldInstance(new WorldTemplate { Id = 1, Name = "test", OceanLevel = 100f }, 0, true, 1);
        world.Water.OceanLevel = 100f;
        var resolver = new ShipHullGeometryResolver(new TubeFiles());
        var config = AppConfiguration.Instance.World;
        var previous = config.AuthoredShipDriveEnabled;
        PhysicsManager physics;
        try
        {
            config.AuthoredShipDriveEnabled = enabled;
            physics = new PhysicsManager(hullGeometry: resolver) { SimulationWorld = world };
        }
        finally
        {
            config.AuthoredShipDriveEnabled = previous;
        }

        using (physics)
        {
            physics.Initialize();
            var ship = new Slave { ObjId = 10, Hp = 100, Template = new SlaveTemplate() };
            ship.Transform.Local.SetPosition(1000f, 1000f, 98f);
            var driver = new Character(null) { ObjId = 11 };
            using var attachment = AttachCharacter(driver, ship.Transform);
            ship.AttachedCharacters[AttachPointKind.Driver] = driver;
            ship.AddBonus(0, new Bonus
            {
                Template = new BonusTemplate { Attribute = UnitAttribute.MoveSpeedMul, ModifierType = UnitModifierType.Value },
                Value = 500
            });
            const string uri = "prefab://prefabs/tube.xml/mixed";
            await resolver.ResolveAsync(uri, 1f, 2f, 2f);
            var now = Environment.TickCount64;
            physics.AddShip(ship, new ShipModelV1
            {
                Id = 3, Normal = uri, Mass = 2000f * MathF.PI, WaterDensity = 1f, WaterResistance = 1f,
                TubeRadius = 1f, TubeLength = 2f, TubeOffsetZ = 2f,
                MassBoxSizeX = 1f, MassBoxSizeY = 3f, MassBoxSizeZ = 1f,
                Velocity = 4f, ReverseVelocity = 2f, Accel = 1f, ReverseAccel = 1f, MaxRpmSec = 1f, MinRpmSec = 1f,
                AccelExponent = 1f, HaltRate = 1f
            });

            for (var elapsed = 0; elapsed <= 4000; elapsed += 20)
            {
                physics.SetHelm(ship, 127, 0);
                physics.Tick(now + elapsed);
            }

            var speed = ship.PhysicsState.Pose.Velocity.Y;
            if (enabled)
            {
                await Assert.That(speed).IsGreaterThan(1f);
                await Assert.That(speed).IsLessThan(2f);
            }
            else
                await Assert.That(speed).IsGreaterThan(4f);
        }
    }

    [Test]
    public async Task SharedPrefab_ResolvesEachModelsCylinderDimensions()
    {
        var resolver = new ShipHullGeometryResolver(new TubeFiles());
        const string uri = "prefab://prefabs/tube.xml/mixed";
        var derived = resolver.ResolveAsync(uri, 1f, 0f, 0f);
        var authored = resolver.ResolveAsync(uri, 2f, 4f, 1.5f);
        // Both tube centres are at 1.5 m. The immersed half-volumes are π r² times the half-length.
        var first = await derived;
        var second = await authored;
        await Assert.That(first.Buoyancy.DisplacedVolume(1.5f)).IsEqualTo(2.5d * Math.PI).Within(0.00001d);
        await Assert.That(second.Buoyancy.DisplacedVolume(1.5f)).IsEqualTo(16d * Math.PI).Within(0.00001d);
    }

    [Test]
    [Arguments("<Object Type='Brush' Name='buoy2' Prefab='objects/missing.cgf'/>")]
    [Arguments("<Object Type='Brush' Name='buoy2'/>")]
    [Arguments("<Object Type='Brush' Name='buoy2' Prefab='objects/broken.cgf'/>")]
    public async Task IncompletePrefabBuoys_DiscardThePartialDisplacementProfile(string additionalBuoy)
    {
        var resolver = new ShipHullGeometryResolver(new BuoyFiles(additionalBuoy));
        var geometry = await resolver.ResolveAsync("prefab://prefabs/floating.xml/ship", 1f, 2f, 2f);

        await Assert.That(geometry.Buoyancy).IsNull();
    }

    private sealed class BuoyFiles(string additionalBuoy = "") : IGeometryFileSource
    {
        public byte[] TryReadBytes(string path) => path switch
        {
            "game/prefabs/floating.xml" => Encoding.UTF8.GetBytes("<Prefabs><Prefab Name='ship'><Objects>" +
                "<Object Type='Brush' Name='Buoy1' Prefab='objects/float.cgf'/>" +
                additionalBuoy +
                "</Objects></Prefab></Prefabs>"),
            "game/objects/float.cgf" => CgfFixture.WithBoxProxy(),
            "game/objects/broken.cgf" => CgfFixture.WithBoxAndBrokenProxyReference(),
            _ => null
        };
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
