using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class MatePacketWireFormatTests
{
    [Test]
    [Arguments(MateType.Ride, "01")]
    [Arguments(MateType.Battle, "02")]
    public async Task MateSpawned_WritesMateTypeBeforeId(MateType mateType, string typeHex)
    {
        var mate = new Mate
        {
            TlId = 0x1234,
            MateType = mateType,
            Id = 0x11223344,
            ItemId = 0x0102030405060708,
            UserState = 1,
            Experience = 2,
            Mileage = 3,
            SpawnDelayTime = 4,
            Skills = [0x10203040]
        };

        var payload = new SCMateSpawnedPacket(mate).Write(new PacketStream()).GetBytes();

        var expected = "3412" + typeHex + "4433221108070605040302010102000000030000000400000040302010" +
            "000000000000000000000000000000000000000000000000000000000000000000000000";
        await Assert.That(Convert.ToHexString(payload)).IsEqualTo(expected);
    }

    [Test]
    public async Task MateSpawned_WritesOnlyTenSkillSlots()
    {
        var mate = new Mate { Skills = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11] };

        var payload = new SCMateSpawnedPacket(mate).Write(new PacketStream()).GetBytes();

        await Assert.That(payload.Length).IsEqualTo(68);
        await Assert.That(Convert.ToHexString(payload.AsSpan(28))).IsEqualTo(
            "0100000002000000030000000400000005000000060000000700000008000000090000000A000000");
    }
}
