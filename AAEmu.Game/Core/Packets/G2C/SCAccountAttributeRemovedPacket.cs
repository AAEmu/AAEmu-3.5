using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Account;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAccountAttributeRemovedPacket() : GamePacket(SCOffsets.SCAccountAttributeRemovedPacket, 5)
{
    public override PacketStream Write(PacketStream stream) =>
        stream.Write(AuctionPostingAccess.Kind).Write(AuctionPostingAccess.Listing);
}
