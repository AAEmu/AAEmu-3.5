using AAEmu.Commons.Network;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ChangeGamePoint : ItemTask
{
    private readonly byte _kind;
    private readonly int _amount;

    public ChangeGamePoint(GamePointKind kind, int amount)
    {
        _type = ItemAction.ChangeGamePoint; // 3
        _amount = amount;
        _kind = (byte)kind;
        _tLogt = SetTlogT(_type, SlotType.Inventory); // установим tLogt по значению ItemAction
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(_kind);   // kind
        stream.Write(_amount); // amount
        return stream;
    }
}
