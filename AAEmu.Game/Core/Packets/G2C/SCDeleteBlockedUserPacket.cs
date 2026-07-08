using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDeleteBlockedUserPacket(Blocked blocked, bool success, short errorMessage)
    : GamePacket(SCOffsets.SCDeleteBlockedUserPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(blocked);
        stream.Write(success);
        stream.Write(errorMessage);
        return stream;
    }
}
