using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSGoodDetailPacket(bool pageEnd, IcsSku sku) : GamePacket(SCOffsets.SCICSGoodDetailPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(pageEnd);

        stream.Write(sku.ShopId);
        stream.Write(sku.Sku);
        stream.Write(sku.ItemId);
        stream.Write(sku.ItemCount);
        stream.Write(sku.SelectType);
        stream.Write(sku.IsDefault);
        stream.Write(sku.EventType);
        stream.Write(sku.EventEndDate);
        stream.Write((byte)sku.Currency);
        stream.Write(sku.Price);
        stream.Write(sku.DiscountPrice);
        stream.Write(sku.BonusItemId);
        stream.Write(sku.BonusItemCount);
        stream.Write(sku.PayItemType);

        return stream;
    }
}
