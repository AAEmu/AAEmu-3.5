using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ItemBuyback : ItemTask
{
    private readonly Item _item;

    public ItemBuyback(Item item)
    {
        _type = ItemAction.Take; // 6
        _item = item;
        _tLogt = SetTlogT(_type, SlotType.Inventory); // установим tLogt по значению ItemAction
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write((byte)_item.SlotType);
        stream.Write((byte)_item.Slot);

        _item.Write(stream);

        return stream;
    }
}
