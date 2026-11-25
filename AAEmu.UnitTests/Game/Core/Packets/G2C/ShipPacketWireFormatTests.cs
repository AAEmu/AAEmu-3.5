using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class ShipPacketWireFormatTests
{
    // AA Classic Next deserializes these packets in sub_3C498BF0 and sub_3C498D60.
    [Test]
    public async Task SlaveCreated_PutsCreatorNameImmediatelyAfterThe64BitType()
    {
        var packet = new SCSlaveCreatedPacket(68884, 11, 46887, 0x0102030405060708, "Captain");
        var stream = packet.Write(new PacketStream());
        stream.Rollback();

        await Assert.That(packet.TypeId).IsEqualTo((ushort)0x083);
        await Assert.That(stream.ReadBc()).IsEqualTo(68884u);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)11);
        await Assert.That(stream.ReadBc()).IsEqualTo(46887u);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(0x0102030405060708ul);
        await Assert.That(stream.ReadString()).IsEqualTo("Captain");
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SlaveDespawn_WritesResultAfterTheObjectId(bool success)
    {
        var packet = new SCSlaveDespawnPacket(46887, success);
        var stream = packet.Write(new PacketStream());
        stream.Rollback();

        await Assert.That(packet.TypeId).IsEqualTo((ushort)0x1B6);
        await Assert.That(stream.Count).IsEqualTo(4);
        await Assert.That(stream.ReadBc()).IsEqualTo(46887u);
        await Assert.That(stream.ReadBoolean()).IsEqualTo(success);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }
}
