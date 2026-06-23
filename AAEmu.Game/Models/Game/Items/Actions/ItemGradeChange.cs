using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ItemGradeChange : ItemTask
{
    private readonly Item _item;

    public ItemGradeChange(Item item)
    {
        _type = ItemAction.ChangeGrade; // 14
        _item = item;
        _tLogt = SetTlogT(_type, SlotType.Inventory); // установим tLogt по значению ItemAction
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write((byte)_item.SlotType); // type
        stream.Write((byte)_item.Slot);     // index
        stream.Write(_item.Id);             // itemId
        stream.Write(_item.Grade);          // grade
        return stream;
    }
}
