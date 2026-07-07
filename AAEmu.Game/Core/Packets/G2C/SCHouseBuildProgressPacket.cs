using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHouseBuildProgressPacket(ushort tl, uint type, int allstep, int curstep)
    : GamePacket(SCOffsets.SCHouseBuildProgressPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tl);
        stream.Write(type);
        stream.Write(allstep);
        stream.Write(curstep);
        return stream;
    }
}
