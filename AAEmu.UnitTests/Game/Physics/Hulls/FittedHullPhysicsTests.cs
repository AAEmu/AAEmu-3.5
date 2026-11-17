using System.Numerics;

using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Physics.Hulls;
using AAEmu.Geometry;

namespace AAEmu.UnitTests.Game.Physics.Hulls;

public class FittedHullPhysicsTests
{
    [Test]
    public async Task CollisionReporter_PreservesStrongestContactUntilHalfSecondFlush()
    {
        using var physics = new FittedHullPhysics(_ => new ZoneCollision(null, Pier(), 100f));
        Add(physics, 7, 1000f, 1018f, 8f, 0f);
        var reporter = new HullCollisionReporter();
        var reported = new List<float>();
        void Record(uint id, HullCollisionWindow contact, float impact) => reported.Add(impact);
        reporter.Flush(0, [7u], physics, Record);
        for (var now = 20; now < 500; now += 20)
        {
            physics.Advance([7u], now, _ => 15000f);
            reporter.Flush(now, [7u], physics, Record);
        }
        await Assert.That(reported).IsEmpty();
        reporter.Flush(500, [7u], physics, Record);
        await Assert.That(reported.Count).IsEqualTo(1);
        await Assert.That(reported[0]).IsGreaterThan(1f);
        reporter.Flush(501, [7u], physics, Record);
        await Assert.That(reported.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Shape_UsesSlaveTemplateBoundsForEachHull()
    {
        var shape = HullShape.For(null, new Vector3(12f, 44f, 20f));
        await Assert.That(shape.Beam).IsEqualTo(12f);
        await Assert.That(shape.Length).IsEqualTo(44f);
        await Assert.That(shape.KeelDepth).IsEqualTo(HullShape.ReferenceKeelDepth * 12f / HullShape.ReferenceSize.X).Within(0.0001f);
    }

    [Test]
    public async Task HullPair_UsesEachSlavesAuthoredBounds()
    {
        using var physics = NewPhysics();
        physics.Add(7, 191, null, 15000f, new Vector3(12f, 44f, 20f));
        physics.Seed(7, new ShipMoveType { X = 1000, Y = 1000, Z = 99.6f }, 100f, 0);
        physics.Activate(7, 0);
        Add(physics, 9, 1000f, 1030f, 0f, 0f);
        physics.Advance([7u, 9u], 100, _ => 15000f);
        await Assert.That(physics.ContactSteps).IsGreaterThan(0L);
    }

    [Test]
    public async Task HeldHull_KeepsSeedPoseAndDoesNotCatchUpWhenReleased()
    {
        using var physics = Seeded();
        var initial = physics.Pose(7);
        physics.SetHeld(7, true, 0);
        physics.Advance([7u], 10000, _ => 15000f);
        await Assert.That(physics.Pose(7).Y).IsEqualTo(initial.Y);
        physics.SetHeld(7, false, 10000);
        physics.Advance([7u], 10034, _ => 15000f);
        var moved = physics.Pose(7).Y - initial.Y;
        await Assert.That(moved).IsGreaterThan(0);
        await Assert.That(moved).IsLessThan(0.5);
    }

    [Test]
    public async Task HeldHull_DoesNotParticipateInHullContacts()
    {
        using var physics = NewPhysics();
        Add(physics, 7, 1000f, 1000f, 0f, 0f);
        Add(physics, 9, 1000f, 1000f, 0f, 0f);
        physics.SetHeld(9, true, 0);
        physics.Advance([7u, 9u], 100, _ => 15000f);
        await Assert.That(physics.ContactSteps).IsEqualTo(0L);
    }

    [Test]
    public async Task Prefetch_VisitsAdjacentZonesOnceAndStopsOnBackpressure()
    {
        var grid = new uint[20, 20];
        grid[10, 10] = 1;
        grid[11, 10] = 2;
        grid[10, 11] = 1;
        grid[18, 10] = 3;
        grid[0, 0] = 4;
        var visited = new HashSet<uint>();
        var ordered = new List<uint>();
        var complete = HullZonePrefetch.Visit(grid, new Vector2(640f, 640f), visited, key => { ordered.Add(key); return key != 2; });
        await Assert.That(complete).IsFalse();
        await Assert.That(ordered.Count).IsEqualTo(2);
        await Assert.That(ordered[0]).IsEqualTo(1u);
        await Assert.That(ordered[1]).IsEqualTo(2u);
        visited.Clear();
        ordered.Clear();
        HullZonePrefetch.Visit(grid, new Vector2(640f, 640f), visited, key => { ordered.Add(key); return true; });
        await Assert.That(ordered.Count).IsEqualTo(3);
        await Assert.That(ordered.Contains(3u)).IsTrue();
        await Assert.That(ordered.Contains(4u)).IsFalse();
    }

    [Test]
    public async Task CloserDoodad_IsNotHiddenByBrushBehindIt()
    {
        var body = new HullBody(HullModel.Default) { X = 1000, Y = 1022, Z = 99.6f, WaterLevel = 100f };
        var shape = HullShape.For(null);
        var doodad = StaticCollision.FromInstances([(Box(new Vector3(980f, 1026f, 95f), new Vector3(1020f, 1027f, 105f)), Matrix4x4.Identity)]);
        var contacts = new List<HullContact>();
        HullContacts.FindStatic(body, shape, HullContacts.Outline(shape), null, Pier(), doodad, contacts);
        var found = false;
        foreach (var contact in contacts)
            if (contact.Kind == HullContactKind.Doodad && contact.Point.Y < 1028f) found = true;
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task HullReachesNeighborZoneBeforeItsCentre_SeesNeighborWall()
    {
        var body = new HullBody(HullModel.Default) { X = 1000, Y = 1022, Z = 99.6f, WaterLevel = 100f };
        var shape = HullShape.For(null);
        var contacts = new List<HullContact>();
        var neighbor = Pier();
        HullContacts.FindStatic(body, shape, HullContacts.Outline(shape), null, StaticCollision.Empty, null, contacts,
            (_, y) => y >= 1028f ? neighbor : null);
        await Assert.That(contacts.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task DryHull_FallsOntoGroundWithoutEngineThrust()
    {
        var hull = new HullBody(HullModel.Default) { Z = 110f, HasWater = false, GroundHeight = 100f, HelmThrottle = 1f };
        for (var i = 0; i < 180; i++) hull.Step();
        await Assert.That(hull.Z).IsEqualTo(100f + hull.KeelDepth).Within(0.001f);
        await Assert.That(hull.Surge).IsEqualTo(0f);
        await Assert.That(hull.HeaveRate).IsEqualTo(0f);
    }

    [Test]
    public async Task HullAboveWater_FallsUntilItCanFloat()
    {
        var hull = new HullBody(HullModel.Default) { Z = 110f, HasWater = true, WaterLevel = 100f };
        hull.Step();
        await Assert.That(hull.HeaveRate).IsLessThan(0f);
        for (var i = 0; i < 300; i++) hull.Step();
        await Assert.That(hull.Z).IsEqualTo(100f - HullModel.Default.Draft).Within(0.1f);
    }

    [Test]
    public async Task DisabledMoveAndTurnModifiers_PreventPropulsion()
    {
        var hull = new HullBody(HullModel.Default) { Z = 99.6f, WaterLevel = 100f, HelmThrottle = 1f,
            HelmSteering = 1f, MoveSpeedMultiplier = 0f, TurnSpeedMultiplier = 0f };
        for (var i = 0; i < 180; i++) hull.Step();
        await Assert.That(hull.Surge).IsEqualTo(0f);
        await Assert.That(hull.YawRate).IsEqualTo(0f);
    }

    [Test]
    public async Task InProcessHullSimulation_IsAvailableWithoutAZoneHost()
    {
        var physics = typeof(ShipMoveType).Assembly.GetType("AAEmu.Game.Physics.Hulls.FittedHullPhysics");
        await Assert.That(physics).IsNotNull();
    }

    [Test]
    public async Task Shape_MissingAuthoredBounds_UsesMeasuredReferenceInsteadOfMassBox()
    {
        var shape = HullShape.For(new ShipModelV1 { MassBoxSizeX = 8f, MassBoxSizeY = 22f, MassBoxSizeZ = 30f });
        await Assert.That(shape.Beam).IsEqualTo(HullShape.ReferenceSize.X);
        await Assert.That(shape.Length).IsEqualTo(HullShape.ReferenceSize.Y);
    }

    [Test]
    public async Task Seed_DecodesThreePointFiveVelocityWith2048Scale()
    {
        using var physics = NewPhysics();
        physics.Add(7, 191, null, 15000f);
        physics.Seed(7, new ShipMoveType { X = 1000f, Y = 2000f, Z = 99.6f, VelX = 4096, VelY = 6144 }, 100f, 0);
        var pose = physics.Pose(7);
        await Assert.That(pose.Velocity.X).IsEqualTo(2f).Within(0.00001f);
        await Assert.That(pose.Velocity.Y).IsEqualTo(3f).Within(0.00001f);
    }

    [Test]
    public async Task Advance_UsesFixedStepsAndCapsCatchUp()
    {
        using var physics = Seeded();
        var before = physics.Pose(7);
        physics.Advance([7u], 32, _ => 15000f);
        await Assert.That(physics.Pose(7).Y).IsEqualTo(before.Y);
        physics.Advance([7u], 34, _ => 15000f);
        await Assert.That(physics.Pose(7).Y).IsGreaterThan(before.Y);
        var stepped = physics.Pose(7);
        physics.Advance([7u], 3600000, _ => 15000f);
        await Assert.That(physics.Pose(7).Y - stepped.Y).IsLessThan(2.0);
    }

    [Test]
    public async Task Helm_ExpiresAfterFiveSeconds()
    {
        using var physics = Seeded(speed: 0f);
        physics.SetHelm(7, 1f, 1f, 0);
        Run(physics, 0, 1000);
        await Assert.That(physics.Pose(7).ThrottleStep).IsGreaterThan(100);
        Run(physics, 1000, 5200);
        var pose = physics.Pose(7);
        await Assert.That(pose.ThrottleStep).IsEqualTo(0);
        await Assert.That(pose.Steering).IsEqualTo(0f);
        await Assert.That(pose.Rpm).IsEqualTo(0f);
    }

    [Test]
    public async Task Deactivate_FreezesMotionAndDropsHelmUntilActivated()
    {
        using var physics = Seeded();
        physics.SetHelm(7, 1f, 1f, 0);
        physics.Deactivate(7, 0);
        physics.SetHelm(7, 1f, 1f, 10);
        Run(physics, 0, 1000);
        var held = physics.Pose(7);
        await Assert.That(held.Y).IsEqualTo(1000.0);
        await Assert.That(held.ThrottleStep).IsEqualTo(0);
        physics.Activate(7, 1000);
        Run(physics, 1000, 1200);
        await Assert.That(physics.Pose(7).Y).IsGreaterThan(held.Y);
        await Assert.That(physics.Pose(7).ThrottleStep).IsEqualTo(0);
    }

    [Test]
    public async Task Impulse_UsesShipFrameAndIncludesExtraMass()
    {
        using var physics = Seeded(speed: 0f);
        physics.SetExtraMass(7, 15000f);
        physics.ApplyImpulse(7, new HullImpulse(Vector3.Zero, Vector3.Zero, new Vector3(30000f, 60000f, 0f), Vector3.Zero));
        var pose = physics.Pose(7);
        await Assert.That(pose.Velocity.X).IsEqualTo(1f).Within(0.00001f);
        await Assert.That(pose.Velocity.Y).IsEqualTo(2f).Within(0.00001f);
    }

    [Test]
    public async Task WaterLevelUpdate_MovesHullToTheNewWaterline()
    {
        using var physics = Seeded(speed: 0f);
        physics.SetWaterLevel(7, 110f);
        Run(physics, 0, 5000);
        await Assert.That(physics.Pose(7).Z).IsEqualTo(110f - HullModel.Default.Draft).Within(0.02f);
    }

    [Test]
    public async Task Pier_StopsHullAndReportsStaticFrontHitOncePerWindow()
    {
        var zone = new ZoneCollision(null, Pier(), 100f);
        using var physics = Seeded(zone, 10f);
        physics.SetHelm(7, 1f, 0f, 0);
        Run(physics, 0, 4000);
        var pose = physics.Pose(7);
        await Assert.That(pose.Y + HullShape.For(null).HalfLength).IsLessThan(1030.6);
        await Assert.That(pose.Y).IsGreaterThan(1015.0);
        var windows = new List<HullCollisionWindow>();
        physics.TakeCollisionWindows(7, windows);
        await Assert.That(windows.Count).IsEqualTo(1);
        await Assert.That(windows[0].SourceBcId).IsEqualTo(0u);
        await Assert.That(windows[0].OwnPart).IsEqualTo((byte)0);
        await Assert.That(windows[0].Kind).IsEqualTo(HullContactKind.Brush);
        await Assert.That(windows[0].Impact).IsGreaterThan(5f);
        await Assert.That(physics.FirstContactMs(7)).IsNotNull();
        windows.Clear();
        physics.TakeCollisionWindows(7, windows);
        await Assert.That(windows).IsEmpty();
    }

    [Test]
    public async Task DoodadIndex_StopsHullAndNamesDoodadContact()
    {
        using var physics = new FittedHullPhysics(_ => null, _ => Pier());
        Add(physics, 7, 1000f, 1000f, 10f, 0f);
        physics.SetHelm(7, 1f, 0f, 0);
        Run(physics, 0, 4000);
        var windows = new List<HullCollisionWindow>();
        physics.TakeCollisionWindows(7, windows);
        await Assert.That(windows.Count).IsEqualTo(1);
        await Assert.That(windows[0].Kind).IsEqualTo(HullContactKind.Doodad);
    }

    [Test]
    public async Task CollisionsDisabled_AllowsPassingThroughPier()
    {
        using var physics = Seeded(new ZoneCollision(null, Pier(), 100f), 10f);
        physics.CollisionsEnabled = false;
        physics.SetHelm(7, 1f, 0f, 0);
        Run(physics, 0, 4000);
        await Assert.That(physics.Pose(7).Y).IsGreaterThan(1035.0);
        await Assert.That(physics.ContactSteps).IsEqualTo(0L);
    }

    [Test]
    public async Task HullPair_StopsHeadOnHullsAndReportsTheOtherHull()
    {
        using var physics = NewPhysics();
        Add(physics, 7, 1000f, 1000f, 8f, 0f);
        Add(physics, 9, 1000f, 1060f, 8f, MathF.PI);
        for (var now = 0; now <= 6000; now += 20)
        {
            physics.SetHelm(7, 1f, 0f, now);
            physics.SetHelm(9, 1f, 0f, now);
            physics.Advance([7u, 9u], now, _ => 15000f);
        }
        await Assert.That(physics.Pose(9).Y - physics.Pose(7).Y).IsGreaterThan(19.0);
        var windows = new List<HullCollisionWindow>();
        physics.TakeCollisionWindows(7, windows);
        physics.TakeCollisionWindows(9, windows);
        await Assert.That(windows.Count).IsGreaterThan(0);
        await Assert.That(windows[0].Kind).IsEqualTo(HullContactKind.Hull);
        await Assert.That(windows[0].SourceBcId is 7u or 9u).IsTrue();
    }

    [Test]
    public async Task DifferentWorlds_HullsDoNotCollide()
    {
        using var physics = NewPhysics();
        Add(physics, 7, 1000f, 1000f, 0f, 0f);
        Add(physics, 9, 1000f, 1000f, 0f, 0f);
        physics.SetWorld(9, 2);
        physics.Advance([7u, 9u], 100, _ => 15000f);
        await Assert.That(physics.ContactSteps).IsEqualTo(0L);
    }

    [Test]
    public async Task DeadGroundedHull_RestsAndImpulseWakesIt()
    {
        using var physics = new FittedHullPhysics(_ => new ZoneCollision((_, _) => 100f, StaticCollision.Empty, 100f), isDead: _ => true);
        Add(physics, 7, 1000f, 1000f, 0f, 0f);
        Run(physics, 0, 10000);
        await Assert.That(physics.Rests(7)).IsTrue();
        var before = physics.Pose(7);
        physics.ApplyImpulse(7, new HullImpulse(new Vector3(0f, 2f, 0f), Vector3.Zero, Vector3.Zero, Vector3.Zero));
        await Assert.That(physics.Rests(7)).IsFalse();
        Run(physics, 10000, 11000);
        await Assert.That(physics.Pose(7).Y).IsGreaterThan(before.Y);
    }

    [Test]
    public async Task Explosion_OnlyPushesOtherHullsInsideItsWorldAndRadius()
    {
        using var physics = NewPhysics();
        Add(physics, 7, 1000f, 1000f, 0f, 0f);
        Add(physics, 9, 1000f, 1010f, 0f, 0f);
        Add(physics, 11, 1010f, 1000f, 0f, 0f);
        physics.SetWorld(11, 2);
        physics.Advance([7u, 9u, 11u], 0, _ => 15000f);
        physics.Explode(new HullExplosion(0, 7, 1000, 1000, 99.6f, 20000f, 0f, 2f));
        await Assert.That(physics.Pose(7).Velocity.Length()).IsEqualTo(0f);
        await Assert.That(physics.Pose(9).Velocity.Y).IsGreaterThan(0.9f);
        await Assert.That(physics.Pose(11).Velocity.Length()).IsEqualTo(0f);
    }

    [Test]
    public async Task NonFiniteImpulse_DoesNotPoisonTheSimulation()
    {
        using var physics = Seeded();
        var before = physics.Pose(7);
        physics.ApplyImpulse(7, new HullImpulse(new Vector3(float.NaN, 1f, 0f), Vector3.Zero, Vector3.Zero, Vector3.Zero));
        await Assert.That(physics.Pose(7)).IsEqualTo(before);
    }

    [Test]
    public async Task NonFiniteSeed_DoesNotReplaceTheLastValidPose()
    {
        using var physics = Seeded();
        var before = physics.Pose(7);
        physics.Seed(7, new ShipMoveType { X = float.NaN, Y = 1f, Z = 100f }, 100f, 100);
        await Assert.That(physics.Pose(7)).IsEqualTo(before);
    }

    [Test]
    public async Task NonFiniteTerrain_DoesNotProduceAContact()
    {
        using var physics = Seeded(new ZoneCollision((_, _) => float.NaN, StaticCollision.Empty, 100f));
        Run(physics, 0, 100);
        var windows = new List<HullCollisionWindow>();
        physics.TakeCollisionWindows(7, windows);
        await Assert.That(windows).IsEmpty();
    }

    [Test]
    public async Task NonFiniteShipModel_UsesFiniteReferenceMassAndFootprint()
    {
        var ship = new ShipModelV1
        {
            Id = 3, Mass = float.PositiveInfinity, MassBoxSizeX = float.NaN, MassBoxSizeY = float.PositiveInfinity
        };
        await Assert.That(float.IsFinite(HullModel.For(ship).Mass)).IsTrue();
        var shape = HullShape.For(ship);
        await Assert.That(shape.Beam).IsEqualTo(HullShape.ReferenceSize.X);
        await Assert.That(shape.Length).IsEqualTo(HullShape.ReferenceSize.Y);
    }

    [Test]
    public async Task NonFiniteBlastPressure_DoesNotPoisonTheSimulation()
    {
        using var physics = Seeded();
        physics.Advance([7u], 0, _ => 15000f);
        var before = physics.Pose(7);
        physics.Explode(new HullExplosion(0, 0, 1000, 990, 99.6f, 20000f, 0f, float.NaN));
        await Assert.That(physics.Pose(7)).IsEqualTo(before);
    }

    private static FittedHullPhysics NewPhysics() => new(_ => null);

    private static FittedHullPhysics Seeded(ZoneCollision? zone = null, float speed = 3f)
    {
        var physics = new FittedHullPhysics(_ => zone);
        Add(physics, 7, 1000f, 1000f, speed, 0f);
        return physics;
    }

    private static void Add(FittedHullPhysics physics, uint id, float x, float y, float speed, float yaw)
    {
        var (qx, qy, qz) = HullBody.ShortsFromYaw(yaw);
        physics.Add(id, 191, null, 15000f);
        physics.Seed(id, new ShipMoveType
        {
            X = x, Y = y, Z = 99.6f, RotationX = qx, RotationY = qy, RotationZ = qz,
            VelX = (short)(-MathF.Sin(yaw) * speed * 2048f), VelY = (short)(MathF.Cos(yaw) * speed * 2048f)
        }, 100f, 0);
        physics.Activate(id, 0);
    }

    private static void Run(FittedHullPhysics physics, int from, int to)
    {
        for (var now = from; now <= to; now += 20)
            physics.Advance([7u], now, _ => 15000f);
    }

    private static StaticCollision Pier() => StaticCollision.FromInstances(
        [(Box(new Vector3(980f, 1030f, 95f), new Vector3(1020f, 1040f, 105f)), Matrix4x4.Identity)]);

    private static CollisionMesh Box(Vector3 min, Vector3 max)
    {
        var vertices = new List<Vector3>();
        for (var i = 0; i < 8; i++)
            vertices.Add(new Vector3((i & 1) != 0 ? max.X : min.X, (i & 2) != 0 ? max.Y : min.Y, (i & 4) != 0 ? max.Z : min.Z));
        int[] triangles = [0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5];
        return CollisionMesh.Build(vertices, triangles);
    }
}
