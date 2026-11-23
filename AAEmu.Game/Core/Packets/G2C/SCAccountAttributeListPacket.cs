using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Account;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAccountAttributeListPacket(AuctionPostingAccess auctionPosting)
    : GamePacket(SCOffsets.SCAccountAttributeListPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(auctionPosting.CanPost ? 1u : 0u);
        return auctionPosting.CanPost ? WriteAuctionPosting(stream, auctionPosting.GrantedAt) : stream;
    }

    internal static PacketStream WriteAuctionPosting(PacketStream stream, DateTime grantedAt) =>
        stream.Write(AuctionPostingAccess.Kind)
            .Write(AuctionPostingAccess.Listing)
            .Write(AuctionPostingAccess.AllWorlds)
            .Write(0u)
            .Write(grantedAt)
            .Write(DateTime.MinValue);
}
