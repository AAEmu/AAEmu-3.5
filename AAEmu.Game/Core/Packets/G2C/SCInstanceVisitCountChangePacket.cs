using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.InstantGame;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInstanceVisitCountChangePacket(VisitCountItem item)
    : GamePacket(SCOffsets.SCInstanceVisitCountChangePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(item);
        return stream;
    }
}
