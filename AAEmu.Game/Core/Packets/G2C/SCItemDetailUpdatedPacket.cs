using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCItemDetailUpdatedPacket(Item item) : GamePacket(SCOffsets.SCItemDetailUpdatedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(item.Id);
        stream.Write((byte)item.SlotType);
        stream.Write((byte)item.Slot);
        stream.Write((byte)item.DetailType);
        item.WriteDetails(stream);
        return stream;
    }
}
