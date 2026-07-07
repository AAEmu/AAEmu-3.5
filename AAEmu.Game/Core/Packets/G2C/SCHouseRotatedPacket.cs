using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHouseRotatedPacket(uint objId, float zRot) : GamePacket(SCOffsets.SCHouseRotatedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(zRot);
        return stream;
    }
}
