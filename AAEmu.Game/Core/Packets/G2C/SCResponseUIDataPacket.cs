using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCResponseUIDataPacket(uint type, ushort uiDataType, string uiData)
    : GamePacket(SCOffsets.SCResponseUIDataPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(type);
        stream.Write(uiDataType);
        stream.Write(uiData);
        stream.Write(uiData.Length + 1);
        return stream;
    }
}
