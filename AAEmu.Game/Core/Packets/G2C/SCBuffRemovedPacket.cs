using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCBuffRemovedPacket(uint objId, uint index) : GamePacket(SCOffsets.SCBuffRemovedPacket, 5)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override PacketStream Write(PacketStream stream)
    {
        // Structure from x2game_dump.dll (SCBuffRemoved_0x175 deserializer @ 0x3993E3D0):
        // bc objId, uint32 index. CryNetwork's Block() consumes no wire bytes.
        stream.WriteBc(objId);
        stream.Write(index);
        return stream;
    }
}
