using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ItemRemove : ItemTask
{
    private readonly Item _item;
    private readonly int _itemCount;
    private readonly DateTime _removeReservationTime;

    public ItemRemove(Item item, ItemTaskLogType? tlogtOverride = null)
    {
        _type = ItemAction.Remove; // 7
        _item = item;
        _itemCount = item.Count;
        _removeReservationTime = DateTime.UtcNow;
        _tLogt = tlogtOverride ?? SetTlogT(_type, item.SlotType, _itemCount > 0); // установим tLogt по значению ItemAction
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write((byte)_item.SlotType);   // type
        stream.Write((byte)_item.Slot);       // index
        stream.Write(_item.Id);               // id
        stream.Write(_itemCount);             // stack
        stream.Write(_removeReservationTime); // removeReservationTime
        stream.Write(0u);                     // type
        stream.Write(0u);                     // dbSlaveId
        stream.Write(_item.TemplateId);       // type

        return stream;
    }
}
