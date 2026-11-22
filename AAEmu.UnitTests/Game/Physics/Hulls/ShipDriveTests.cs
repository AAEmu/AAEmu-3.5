using System.Numerics;

using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Physics.Hulls;
using AAEmu.Geometry;
using AAEmu.UnitTests.Geometry;

namespace AAEmu.UnitTests.Game.Physics.Hulls;

public class ShipDriveTests
{
    [Test]
    [Arguments(1f, 1f, 0f, 4f)]
    [Arguments(-1f, 1f, 0f, 2f)]
    [Arguments(1f, 0.5f, 0f, 2f)]
    [Arguments(-1f, 1f, 1f, 3f)]
    public async Task EngineUsesTheAuthoredSpeedForItsDirection(float throttle, float multiplier, float reverseBonus, float speedLimit)
    {
        using var hulls = new FittedHullPhysics(_ => null, useAuthoredDrive: true);
        AddShip(hulls, 1f);
        hulls.SetMovementModifiers(1, 1f, 1f, new ShipDriveAttributes(multiplier, 1f, 0f, reverseBonus, true, true));

        for (var ms = 20; ms <= 20000; ms += 20)
        {
            hulls.SetHelm(1, throttle, 0f, ms);
            hulls.Advance([1], ms, _ => 4000f);
        }

        var forward = hulls.Pose(1).Velocity.Y * throttle;
        await Assert.That(forward).IsGreaterThan(speedLimit * 0.65f);
        await Assert.That(forward).IsLessThan(speedLimit);
    }

    [Test]
    [Arguments(true, false, false)]
    [Arguments(true, true, true)]
    [Arguments(false, true, true)]
    public async Task ControllerRespectsMovementAndTurningRestrictions(bool authoredDrive, bool dead, bool canAct)
    {
        using var hulls = new FittedHullPhysics(_ => null, isDead: _ => dead, useAuthoredDrive: authoredDrive);
        AddShip(hulls, 1f);
        hulls.SetMovementModifiers(1, 1f, 1f, new ShipDriveAttributes(1f, 1f, 0f, 0f, canAct, canAct));
        for (var ms = 20; ms <= 2000; ms += 20)
        {
            hulls.SetHelm(1, 1f, 1f, ms);
            hulls.Advance([1], ms, _ => 4000f);
        }

        await Assert.That(hulls.Pose(1).Velocity.Y).IsEqualTo(0f).Within(0.001f);
        await Assert.That(hulls.Pose(1).AngularVelocity.Z).IsEqualTo(0f).Within(0.001f);
    }

    [Test]
    public async Task AZeroForwardSpeedDoesNotFallBackToAWorkingEngine()
    {
        using var hulls = new FittedHullPhysics(_ => null, useAuthoredDrive: true);
        AddShip(hulls, 1f, forwardSpeed: 0f);
        uint[] active = [1];
        for (var ms = 20; ms <= 3000; ms += 20)
        {
            hulls.SetHelm(1, 1f, 0f, ms);
            hulls.Advance(active, ms, _ => 4000f);
        }

        await Assert.That(hulls.Pose(1).Velocity.Y).IsEqualTo(0f).Within(0.001f);
    }

    [Test]
    public async Task ASubmergedRudderKeepsWorkingOverAShoalAtTheHullCentre()
    {
        using var hulls = new FittedHullPhysics(_ => null, useAuthoredDrive: true);
        AddShip(hulls, 1f);
        hulls.SetEnvironment(1, true, 100f, 97.5f);
        uint[] active = [1];
        for (var ms = 20; ms <= 3000; ms += 20)
        {
            hulls.SetHelm(1, 1f, 0f, ms);
            hulls.Advance(active, ms, _ => 4000f);
        }

        await Assert.That(hulls.Pose(1).Velocity.Y).IsGreaterThan(0.5f);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task LakeShoreChecksTheRotatedSternForRudderImmersion(bool wetStern)
    {
        var minX = wetStern ? 999f : 990f;
        var maxX = wetStern ? 1010f : 1001f;
        var lake = new WaterBodyArea
        {
            Depth = 10f,
            Points = [new Vector3(minX, 990f, 100f), new Vector3(maxX, 990f, 100f),
                new Vector3(maxX, 1010f, 100f), new Vector3(minX, 1010f, 100f)]
        };
        lake.UpdateBounds();
        var water = new WaterBodies { OceanLevel = 85f, Areas = [lake] };
        using var hulls = new FittedHullPhysics(_ => null, useAuthoredDrive: true, isWaterAt: point => water.IsWater(point, out _));
        AddShip(hulls, 1f, yaw: MathF.PI / 2f);
        uint[] active = [1];
        for (var ms = 20; ms <= 1000; ms += 20)
        {
            hulls.SetHelm(1, -1f, 1f, ms);
            hulls.Advance(active, ms, _ => 4000f);
        }

        var pose = hulls.Pose(1);
        if (wetStern)
        {
            await Assert.That(pose.AngularVelocity.Z).IsGreaterThan(0.01f);
            await Assert.That(pose.Velocity.X).IsGreaterThan(0.1f);
        }
        else
        {
            await Assert.That(pose.AngularVelocity.Z).IsEqualTo(0f).Within(0.001f);
            await Assert.That(pose.Velocity.X).IsEqualTo(0f).Within(0.001f);
        }
    }

    [Test]
    public async Task AThinPierIsHitBeforeTheSecondSubstepCrossesIt()
    {
        var wall = CgfPhysicsReader.Read(CgfFixture.WithBoxProxy(), out _)!;
        var pier = StaticCollision.FromInstances([(wall,
            Matrix4x4.CreateScale(2f, 0.02f, 3f) * Matrix4x4.CreateTranslation(1000f, 1001.14f, 101f))]);
        using var hulls = new FittedHullPhysics(_ => new ZoneCollision(null, pier, 100f), useAuthoredDrive: true);
        AddShip(hulls, 1f, authoredSize: new Vector3(0.6f, 0.6f, 1f));
        hulls.SetEnvironment(1, false, 100f, null);
        hulls.ApplyImpulse(1, new HullImpulse(new Vector3(0f, 60f, 0f), Vector3.Zero, Vector3.Zero, Vector3.Zero));

        hulls.Advance([1], 40, _ => 4000f);

        await Assert.That(hulls.FirstContactMs(1)).IsNotNull();
        await Assert.That(hulls.Pose(1).Y).IsLessThan(1001.12d);
    }

    [Test]
    [Arguments(false, 2f, true)]
    [Arguments(true, 2f, true)]
    [Arguments(false, 1f, true)]
    [Arguments(false, 2f, false)]
    public async Task HeadOnHullsCollideAtTheirSharedSubstep(bool reverseOrder, float separation, bool secondHasAuthoredDrive)
    {
        using var hulls = new FittedHullPhysics(_ => null, useAuthoredDrive: true);
        var size = new Vector3(0.6f, 0.6f, 1f);
        AddShip(hulls, 1f, authoredSize: size);
        AddShip(hulls, 1f, authoredSize: size, id: 2, y: 1000f + separation, hasBuoyancy: secondHasAuthoredDrive);
        hulls.SetEnvironment(1, false, 100f, null);
        hulls.SetEnvironment(2, false, 100f, null);
        hulls.ApplyImpulse(1, new HullImpulse(new Vector3(0f, 60f, 0f), Vector3.Zero, Vector3.Zero, Vector3.Zero));
        hulls.ApplyImpulse(2, new HullImpulse(new Vector3(0f, -60f, 0f), Vector3.Zero, Vector3.Zero, Vector3.Zero));

        uint[] active = reverseOrder ? [2, 1] : [1, 2];
        hulls.Advance(active, 20, _ => 4000f);

        var windows = new List<HullCollisionWindow>();
        hulls.TakeCollisionWindows(1, windows);
        await Assert.That(windows.Count).IsEqualTo(1);
        await Assert.That(windows[0].SourceBcId).IsEqualTo(2u);
        await Assert.That(hulls.Pose(1).Y).IsLessThan(hulls.Pose(2).Y);
    }

    [Test]
    public async Task HullsAtSeparateHeightsDoNotCollideOnTheSameFootprint()
    {
        using var hulls = new FittedHullPhysics(_ => null, useAuthoredDrive: true);
        AddShip(hulls, 1f);
        AddShip(hulls, 1f, id: 2, z: 110f);
        hulls.SetEnvironment(1, false, 100f, null);
        hulls.SetEnvironment(2, false, 100f, null);

        hulls.Advance([1, 2], 40, _ => 4000f);

        await Assert.That(hulls.ContactSteps).IsEqualTo(0L);
    }

    [Test]
    [Arguments(0.8f, 0)]
    [Arguments(0.1f, 1)]
    public async Task FallingHullsOnlyCollideWhenTheirHeightsOverlapAtImpact(float verticalSeparation, int expectedWindows)
    {
        using var hulls = new FittedHullPhysics(_ => null, useAuthoredDrive: true);
        var size = new Vector3(0.6f, 0.6f, 1f);
        AddShip(hulls, 1f, authoredSize: size);
        AddShip(hulls, 1f, authoredSize: size, id: 2, y: 1001f, z: 98f + verticalSeparation);
        hulls.SetEnvironment(1, false, 100f, null);
        hulls.SetEnvironment(2, false, 100f, null);
        hulls.ApplyImpulse(1, new HullImpulse(new Vector3(0f, 60f, -60f), Vector3.Zero, Vector3.Zero, Vector3.Zero));
        hulls.ApplyImpulse(2, new HullImpulse(new Vector3(0f, -60f, -60f), Vector3.Zero, Vector3.Zero, Vector3.Zero));

        hulls.Advance([1, 2], 40, _ => 4000f);

        var windows = new List<HullCollisionWindow>();
        hulls.TakeCollisionWindows(1, windows);
        await Assert.That(windows.Count).IsEqualTo(expectedWindows);
    }

    [Test]
    public async Task AnEngineCanDriveAValidFlotationHullWithNoBrushSlots()
    {
        using var hulls = new FittedHullPhysics(_ => null, useAuthoredDrive: true);
        AddShip(hulls, 1f, hasBrushBounds: false);
        uint[] active = [1];
        for (var ms = 20; ms <= 6000; ms += 20)
        {
            hulls.SetHelm(1, 1f, 1f, ms);
            hulls.Advance(active, ms, _ => 4000f);
        }

        var pose = hulls.Pose(1);
        await Assert.That(pose.Velocity.Y).IsGreaterThan(2f);
        await Assert.That(pose.Velocity.Y).IsLessThan(4f);
        await Assert.That(pose.AngularVelocity.Z).IsEqualTo(0f).Within(0.001f);
    }

    [Test]
    public async Task AnAuthoredHullDampsItsDriftAboveWater()
    {
        using var hulls = new FittedHullPhysics(_ => null, useAuthoredDrive: true);
        AddShip(hulls, 1f);
        hulls.SetEnvironment(1, false, 100f, null);
        hulls.ApplyImpulse(1, new HullImpulse(Vector3.UnitY * 2f, Vector3.Zero, Vector3.Zero, Vector3.Zero));

        hulls.Advance([1], 100, _ => 4000f);

        await Assert.That(hulls.Pose(1).Velocity.Y).IsEqualTo(2f * MathF.Exp(-0.01f)).Within(0.0001f);
    }

    [Test]
    public async Task WideLightHullStaysAtTheWaterlineDuringAFullTurn()
    {
        using var hulls = new FittedHullPhysics(_ => null, useAuthoredDrive: true);
        var ship = new ShipModelV1
        {
            Id = 29, Mass = 800f, MassBoxSizeX = 6.5f, MassBoxSizeY = 30f, MassBoxSizeZ = 14.5f,
            WaterDensity = 1f, WaterResistance = 1f, Velocity = 8f, ReverseVelocity = 3.5f,
            Accel = 1f, ReverseAccel = 0.5f, TurnAccel = 1f, MaxRpmSec = 1f, MinRpmSec = 1f,
            AccelExponent = 1f, KeelHeight = 0.2f
        };
        var bounds = new GeometryBounds(new Vector3(-37.881836f, -86.92189f, -41.805183f), new Vector3(36.188515f, 37.899258f, 4.738652f));
        var half = (bounds.Max - bounds.Min) * 0.5f;
        var flotation = HullBuoyancy.FromTube(half.X, half.Y, half.X)!;
        hulls.Add(1, 1, ship, ship.Mass);
        hulls.Seed(1, new ShipMoveType { X = 1000f, Y = 1000f, Z = 100f }, 100f, 0);
        hulls.SetBuoyancy(1, flotation, 1f, bounds);
        hulls.Activate(1, 0);

        uint[] active = [1];
        var maxDeviation = 0f;
        for (var ms = 20; ms <= 12000; ms += 20)
        {
            if (ms > 3000)
                hulls.SetHelm(1, 1f, 1f, ms);
            hulls.Advance(active, ms, _ => ship.Mass);
            maxDeviation = MathF.Max(maxDeviation, MathF.Abs(hulls.Pose(1).Z - 100f));
        }

        await Assert.That(maxDeviation).IsLessThan(0.1f);
    }

    [Test]
    public async Task FittedControllerRemainsTheDefault()
    {
        using var hulls = new FittedHullPhysics(_ => null);
        AddShip(hulls, 1f);
        for (var ms = 20; ms <= 5000; ms += 20)
        {
            hulls.SetHelm(1, 1f, 0f, ms);
            hulls.Advance([1], ms, _ => 4000f);
        }

        await Assert.That(hulls.Pose(1).Velocity.Y).IsGreaterThan(4f);
    }

    [Test]
    [Arguments(1f, -1f)]
    [Arguments(-1f, 1f)]
    public async Task RudderReversesYawWhenBacking(float throttle, float expectedSign)
    {
        using var hulls = new FittedHullPhysics(_ => null, useAuthoredDrive: true);
        AddShip(hulls, 1f);
        for (var ms = 20; ms <= 1000; ms += 20)
        {
            hulls.SetHelm(1, throttle, 0.5f, ms);
            hulls.Advance([1], ms, _ => 4000f);
        }

        await Assert.That(hulls.Pose(1).AngularVelocity.Z * expectedSign).IsGreaterThan(0.01f);
    }

    private static void AddShip(FittedHullPhysics hulls, float resistance, float forwardSpeed = 4f, float yaw = 0f,
        Vector3 authoredSize = default, uint id = 1, float y = 1000f, float z = 98f, bool hasBrushBounds = true, bool hasBuoyancy = true)
    {
        var ship = new ShipModelV1
        {
            Mass = 4000f, MassBoxSizeX = 1f, MassBoxSizeY = 3f, MassBoxSizeZ = 1f,
            WaterDensity = 1f, WaterResistance = resistance, Velocity = forwardSpeed, ReverseVelocity = 2f,
            Accel = 1f, ReverseAccel = 1f, SteerVel = 1f, TurnAccel = 1f, MaxRpmSec = 1f, MinRpmSec = 1f,
            AccelExponent = 1f, HaltRate = 1f
        };
        hulls.Add(id, 1, ship, ship.Mass, authoredSize);
        hulls.Seed(id, new ShipMoveType { X = 1000f, Y = y, Z = z, RotationZ = HullBody.ShortsFromYaw(yaw).Z }, 100f, 0);
        hulls.SetEnvironment(id, true, 100f, null);
        var bounds = hasBrushBounds ? new GeometryBounds(new Vector3(-1f, -3f, -1f), new Vector3(1f, 3f, 1f)) : (GeometryBounds?)null;
        if (hasBuoyancy)
            hulls.SetBuoyancy(id, HullBuoyancy.FromTube(1f, 2f, 2f)!, 1f, bounds);
        hulls.Activate(id, 0);
    }
}
