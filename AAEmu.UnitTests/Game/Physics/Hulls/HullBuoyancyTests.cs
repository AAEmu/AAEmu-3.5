using System.Numerics;

using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Physics.Hulls;
using AAEmu.Geometry;
using AAEmu.UnitTests.Geometry;

namespace AAEmu.UnitTests.Game.Physics.Hulls;

public class HullBuoyancyTests
{
    [Test]
    public async Task AngularImpulseTiltsTheReportedHullAndBuoyancyStartsToRightIt()
    {
        using var hulls = new FittedHullPhysics(_ => null);
        var ship = new ShipModelV1 { Mass = 4000f, MassBoxSizeX = 1f, MassBoxSizeY = 1f, MassBoxSizeZ = 1f, MassCenterZ = -1f };
        var mesh = CgfPhysicsReader.Read(CgfFixture.WithBoxProxy(), out _)!;
        var box = HullBuoyancy.Surface.FromBoxProxy(mesh, Matrix4x4.Identity)!;
        hulls.Add(1, 1, ship, ship.Mass);
        hulls.Seed(1, new ShipMoveType { Z = 100f }, 100f, 0);
        hulls.SetBuoyancy(1, new HullBuoyancy([box]), 1f);
        hulls.Activate(1, 0);
        hulls.ApplyImpulse(1, new HullImpulse(Vector3.Zero, new Vector3(0.5f, -0.5f, 0f), Vector3.Zero, Vector3.Zero));

        uint[] active = [1];
        hulls.Advance(active, 100, _ => ship.Mass);
        var tilted = hulls.Pose(1);
        await Assert.That(tilted.Rotation.X).IsGreaterThan(0f);
        await Assert.That(tilted.Rotation.Y).IsLessThan(0f);
        await Assert.That(tilted.AngularVelocity.X).IsLessThan(0.5f);
        await Assert.That(tilted.AngularVelocity.Y).IsGreaterThan(-0.5f);
    }

    [Test]
    public async Task WaterResistanceUsesTheWetSurfaceAndLeavesADryHullAlone()
    {
        var mesh = CgfPhysicsReader.Read(CgfFixture.WithBoxProxy(), out _)!;
        var volumes = new HullBuoyancy([HullBuoyancy.Surface.FromBoxProxy(mesh, Matrix4x4.Identity)!]);
        var model = HullModel.For(new ShipModelV1 { Mass = 4000f, WaterResistance = 1f });
        var wet = new HullBody(model) { Z = 100, HeaveRate = -1, Buoyancy = (volumes, 1000d) };
        var dry = new HullBody(model) { Z = 103, HeaveRate = -1, Buoyancy = (volumes, 1000d) };

        wet.Step();
        dry.Step();

        // The bottom's 4 m² resist 1 m/s at 1000 kg/(m² s). At half immersion buoyancy balances gravity.
        await Assert.That(wet.HeaveRate).IsEqualTo(-1f + 1f / 30f).Within(0.00001f);
        await Assert.That(dry.HeaveRate).IsEqualTo(-1f - 9.81f / 30f).Within(0.00001f);
    }

    [Test]
    public async Task KeelContributesItsDisplacementAndLateralTorque()
    {
        var floats = HullBuoyancy.FromTube(1f, 2f, 4f)!;
        var volume = floats.WithKeel(new Vector3(0f, 0f, -3f), new Vector3(0.0001f, 2f, 0.5f));
        var wet = volume.Measure(Vector3.UnitZ, 0f, Vector3.Zero, Vector3.UnitX, Vector3.Zero);

        await Assert.That(wet.Volume).IsEqualTo(0.0008d).Within(0.000001d);
        await Assert.That(wet.Drag.X).IsEqualTo(-4f).Within(0.00001f);
        await Assert.That(wet.DragTorque.Y).IsEqualTo(12f).Within(0.00001f);
    }

    [Test]
    public async Task AuthoredKeelStillDampsRollWithoutBrushBounds()
    {
        using var hulls = new FittedHullPhysics(_ => null);
        var ship = new ShipModelV1
        {
            Mass = 4000f, WaterResistance = 1f, KeelLength = 2f, KeelHeight = 0.5f,
            MassBoxSizeX = 1f, MassBoxSizeY = 1f, MassBoxSizeZ = 1f
        };
        hulls.Add(1, 1, ship, ship.Mass);
        hulls.Seed(1, new ShipMoveType { Z = 100f, AngVelY = 1f }, 100f, 0);
        hulls.SetBuoyancy(1, HullBuoyancy.FromTube(1f, 2f, 4f)!, 1f);
        hulls.Activate(1, 0);
        hulls.Advance([1], 40, _ => ship.Mass);

        // The tube is dry. The submerged keel opposes the roll.
        await Assert.That(hulls.Pose(1).AngularVelocity.Y).IsEqualTo(1f - 0.5f / 30f).Within(0.0001f);
    }

    [Test]
    [Arguments(0f, 9.13f, 0f, 0f)]
    [Arguments(1f, 9.01f, -1.57f, -0.049f)]
    public async Task MeasuredCutterKeepsItsFittedPlanarHandling(float helm, float surge, float sway, float yawRate)
    {
        var ship = new ShipModelV1 { Id = 6, Mass = 45000f, WaterResistance = 1f };
        var body = new HullBody(HullModel.For(ship))
        {
            Z = 100.567f, HelmThrottle = 1f, HelmSteering = helm, Buoyancy = (HullBuoyancy.FromTube(5f, 19f, 4f)!, 1000d)
        };

        for (var i = 0; i < 900; i++)
            body.Step();

        await Assert.That(body.Surge).IsEqualTo(surge).Within(0.05f);
        await Assert.That(body.Sway).IsEqualTo(sway).Within(0.05f);
        await Assert.That(body.YawRate).IsEqualTo(yawRate).Within(0.005f);
    }

    [Test]
    public async Task LevelRowboatPreservesTrimThroughASteadyFullHelmTurn()
    {
        var ship = new ShipModelV1 { Id = 4, Mass = 800f, MassBoxSizeX = 1f, MassBoxSizeY = 3f, MassBoxSizeZ = 0.5f, WaterResistance = 1f };
        var tube = HullBuoyancy.FromTube(0.6f, 3f, 1f)!.WithKeel(new Vector3(0f, 0f, -0.9f), new Vector3(0.0001f, 3.3f, 0.6f));
        var body = new HullBody(HullModel.For(ship))
        {
            Z = 99.33f, HelmThrottle = 1f, HelmSteering = 1f, Buoyancy = (tube, 700d)
        };

        for (var i = 0; i < 900; i++)
            body.Step();

        await Assert.That(MathF.Abs(body.Roll)).IsLessThan(0.02f);
        await Assert.That(body.YawRate).IsLessThan(-0.01f);
    }

    [Test]
    public async Task CasterSpinIsReportedInWorldAxesWhenHullFacesSideways()
    {
        using var hulls = new FittedHullPhysics(_ => null);
        var ship = new ShipModelV1 { Mass = 4000f, MassBoxSizeX = 1f, MassBoxSizeY = 1f, MassBoxSizeZ = 1f };
        hulls.Add(1, 1, ship, ship.Mass);
        hulls.Seed(1, new ShipMoveType { Z = 100f, RotationZ = HullBody.ShortsFromYaw(MathF.PI / 2f).Z }, 100f, 0);
        hulls.SetBuoyancy(1, HullBuoyancy.FromTube(1f, 2f, 1f)!, 1f);
        hulls.ApplyImpulse(1, new HullImpulse(Vector3.Zero, Vector3.UnitX * 0.25f, Vector3.Zero, Vector3.UnitX * (4000f / 6f)),
            new Vector3(0f, -10f, 100f));

        var rate = hulls.Pose(1).AngularVelocity;
        await Assert.That(rate.X).IsEqualTo(0.5f).Within(0.00001f);
        await Assert.That(rate.Y).IsEqualTo(0f).Within(0.00001f);
    }

    [Test]
    public async Task PitchedTubeUsesItsSubmergedEndForTheBuoyantMoment()
    {
        var tube = HullBuoyancy.FromTube(1f, 2f, 1f)!;
        var up = new Vector3(0f, 0.6f, 0.8f);
        var wet = tube.Measure(up, Vector3.Dot(up, Vector3.UnitZ), Vector3.UnitZ, Vector3.Zero, Vector3.Zero);

        await Assert.That(wet.Volume).IsEqualTo(2d * Math.PI).Within(0.00001d);
        await Assert.That(Vector3.Cross(wet.FirstMoment, up).X).IsLessThan(0f);
    }

    [Test]
    public async Task HorizontalTube_DisplacesTheCircularSegmentAcrossItsFullLength()
    {
        var tube = HullBuoyancy.FromTube(1f, 2f, -3f)!;

        await Assert.That(tube.DisplacedVolume(-4f)).IsEqualTo(0d);
        await Assert.That(tube.DisplacedVolume(-3.5f)).IsEqualTo(4d * Math.PI / 3d - Math.Sqrt(3d)).Within(0.000001d);
        await Assert.That(tube.DisplacedVolume(-3f)).IsEqualTo(2d * Math.PI).Within(0.000001d);
        await Assert.That(tube.DisplacedVolume(-2f)).IsEqualTo(4d * Math.PI).Within(0.000001d);
    }

    [Test]
    [Arguments(0f, 2f, 3f)]
    [Arguments(1f, 0f, 3f)]
    [Arguments(1f, 2f, 0f)]
    [Arguments(float.NaN, 2f, 3f)]
    public async Task TubeWithUnspecifiedOrNonFiniteDimensions_UsesNoDisplacementProfile(float radius, float halfLength, float offset)
    {
        await Assert.That(HullBuoyancy.FromTube(radius, halfLength, offset)).IsNull();
    }

    [Test]
    public async Task TiltedBox_DisplacesOnlyItsClippedVolume()
    {
        var proxy = CgfPhysicsReader.Read(CgfFixture.WithBoxProxy(), out _)!;
        var box = HullBuoyancy.Surface.FromBoxProxy(proxy, Matrix4x4.CreateRotationX(MathF.PI / 4f))!;

        await Assert.That(box.DisplacedVolume(-2f)).IsEqualTo(0d);
        await Assert.That(box.DisplacedVolume(-MathF.Sqrt(2f) * 0.5f)).IsEqualTo(1d).Within(0.00001d);
        await Assert.That(box.DisplacedVolume(0f)).IsEqualTo(4d).Within(0.00001d);
        await Assert.That(box.DisplacedVolume(2f)).IsEqualTo(8d).Within(0.00001d);
    }

    [Test]
    public async Task SubmergedBuoy_EnablesDriveAboveTheFittedKeelThreshold()
    {
        var proxy = CgfPhysicsReader.Read(CgfFixture.WithBoxProxy(), out _)!;
        var box = HullBuoyancy.Surface.FromBoxProxy(proxy, Matrix4x4.CreateTranslation(0f, 0f, -3f))!;
        var body = new HullBody(HullModel.Default with { Mass = 4000f })
        {
            WaterLevel = 100f,
            Z = 103f,
            HelmThrottle = 1f,
            Buoyancy = (new HullBuoyancy([box]), 1000d)
        };

        for (var step = 0; step < 45; step++)
            body.Step();

        await Assert.That(body.Surge).IsGreaterThan(1f);
        await Assert.That(body.Z).IsEqualTo(103f).Within(0.00001f);
    }
}
