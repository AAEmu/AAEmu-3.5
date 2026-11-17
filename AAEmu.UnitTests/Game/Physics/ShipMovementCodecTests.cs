using AAEmu.Game.Physics;
using System.Numerics;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Physics.Hulls;

namespace AAEmu.UnitTests.Game.Physics;

public class ShipMovementCodecTests
{
    [Test]
    public async Task Velocity_Uses35ScaleAndSaturatesInsteadOfWrapping()
    {
        await Assert.That(ShipMovementCodec.Velocity(1f)).IsEqualTo((short)2048);
        await Assert.That(ShipMovementCodec.Velocity(60f)).IsEqualTo(short.MaxValue);
        await Assert.That(ShipMovementCodec.Velocity(-60f)).IsEqualTo(short.MinValue);
        await Assert.That(ShipMovementCodec.Velocity(float.NaN)).IsEqualTo((short)0);
    }

    [Test]
    public async Task SiegeCircle_UsesRotatedHullBoundsAndCentre()
    {
        var pose = new HullPose(100, 200, 0, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2),
            Vector3.Zero, Vector3.Zero, 0, 0, 0);
        var model = new ShipModelV1 { MassBoxSizeX = 4, MassBoxSizeY = 20, MassCenterY = 3 };
        await Assert.That(ShipSiegeAoEHit.CircleHitsHull(88, 200, 0.5f, pose, model, 1)).IsTrue();
        await Assert.That(ShipSiegeAoEHit.CircleHitsHull(100, 205, 0.5f, pose, model, 1)).IsFalse();
    }
}
