using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAccountAttributeUpdatedPacket(DateTime grantedAt) : GamePacket(SCOffsets.SCAccountAttribute2Packet, 5)
{
    public override PacketStream Write(PacketStream stream) =>
        SCAccountAttributeListPacket.WriteAuctionPosting(stream, grantedAt);
}
