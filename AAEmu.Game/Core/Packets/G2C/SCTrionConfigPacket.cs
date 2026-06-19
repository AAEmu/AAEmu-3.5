using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTrionConfigPacket(bool activate, string authUrl, string platformUrl, string commerceUrl, string csUrl)
    : GamePacket(SCOffsets.SCTrionConfigPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(activate);
        stream.Write(authUrl);
        stream.Write(platformUrl);
        stream.Write(commerceUrl);
        stream.Write(csUrl);
        return stream;
    }
}
