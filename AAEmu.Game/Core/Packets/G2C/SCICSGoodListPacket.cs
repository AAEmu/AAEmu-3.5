using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CashShop;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSGoodListPacket : GamePacket
{
    private readonly bool _pageEnd;
    private readonly byte _reqPage;
    private readonly byte _totalPage;
    private readonly byte _mainTab;
    private readonly byte _subTab;
    private readonly IcsItem _item;
    private readonly IcsSku _firstSku;

    public SCICSGoodListPacket(bool pageEnd, byte reqPage, byte totalPage, byte mainTab, byte subTab, IcsItem item)
        : base(SCOffsets.SCICSGoodListPacket, 5)
    {
        _pageEnd = pageEnd;
        _reqPage = reqPage;
        _totalPage = totalPage;
        _mainTab = mainTab;
        _subTab = subTab;
        _item = item;
        _firstSku = item.FirstSku;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_pageEnd);
        stream.Write(_reqPage);
        stream.Write(_totalPage);

        stream.Write(_item.ShopId);
        stream.Write(_item.Name);
        stream.Write(_mainTab);
        stream.Write(_subTab);
        stream.Write(_item.LevelMin);
        stream.Write(_item.LevelMax);
        stream.Write(_item.DisplayItemId);
        stream.Write(_item.IsSale);
        stream.Write(_item.IsHidden);
        stream.Write((byte)_item.LimitedType);
        stream.Write(_item.LimitedStockMax);
        stream.Write((byte)_item.BuyRestrictType);
        stream.Write(_item.BuyRestrictId);
        stream.Write(_item.SaleStart);
        stream.Write(_item.SaleEnd);

        if (_firstSku != null)
        {
            stream.Write((byte)_firstSku.Currency);
            stream.Write(_firstSku.Price);
            stream.Write(_item.Remaining);
            stream.Write(_firstSku.BonusItemId);
            stream.Write(_firstSku.BonusItemCount);
            stream.Write((byte)_item.ShopButtons);
        }
        else
        {
            stream.Write((byte)CashShopCurrencyType.Credits);
            stream.Write(0);
            stream.Write(0);
            stream.Write(0);
            stream.Write(0);
            stream.Write((byte)CashShopCmdUiType.AllowAll);
        }

        stream.Write(_firstSku?.PayItemType ?? 0);
        stream.Write(0); // discount price placeholder

        return stream;
    }
}
