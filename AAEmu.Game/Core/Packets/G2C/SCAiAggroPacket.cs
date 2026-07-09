using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAiAggroPacket(
    uint npcId,
    int count,
    uint hostileUnitId = 0,
    int summarizeDamage = 0,
    int val2 = 0,
    int val3 = 0)
    : GamePacket(SCOffsets.SCUnitAiAggroPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(npcId);
        stream.Write(count);

        if (count > 0)
        {
            stream.WriteBc(hostileUnitId);
            stream.Write(summarizeDamage);
            stream.Write(val2);
            stream.Write(val3);
            stream.Write((byte)135);
        }

        return stream;
    }
}
