using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Physics;
using System.Numerics;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Physics.Hulls;

namespace AAEmu.UnitTests.Game.Physics;

public class ShipMovementCodecTests
{
    [Test]
    public async Task MovementUpdate_AdvertisesShipPayloadWithHelmAndPropulsion()
    {
        var ship = new Slave { ObjId = 74100, SpawnTime = DateTime.UtcNow };
        var pose = new HullPose(15655, 15480, 100, Quaternion.Identity, new Vector3(0, 1, 0),
            Vector3.Zero, -0.5f, 127, 0.5f);
        var movement = ShipMovementCodec.Movement(ship, pose);
        var stream = new SCOneUnitMovementPacket(ship.ObjId, movement).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadBc()).IsEqualTo(74100u);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)4);
        stream.ReadUInt32(); // time
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        var (x, y, z) = stream.ReadPosition();
        await Assert.That(x).IsEqualTo(15655f);
        await Assert.That(y).IsEqualTo(15480f);
        await Assert.That(z).IsEqualTo(100f).Within(0.002f);
        stream.ReadBytes(24); // velocity, rotation and angular velocity
        await Assert.That(stream.ReadSByte()).IsEqualTo((sbyte)-64);
        await Assert.That(stream.ReadSByte()).IsEqualTo((sbyte)127);
        await Assert.That(stream.ReadSByte()).IsEqualTo((sbyte)64);
        stream.ReadUInt16(); // zone
        await Assert.That(stream.ReadBoolean()).IsFalse();
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

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
