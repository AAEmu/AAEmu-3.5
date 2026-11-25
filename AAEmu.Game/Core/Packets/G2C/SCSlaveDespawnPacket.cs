using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSlaveDespawnPacket(uint id, bool success = true) : GamePacket(SCOffsets.SCSlaveDespawnPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(id);
        stream.Write(success);
        return stream;
    }
}
