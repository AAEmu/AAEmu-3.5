using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSConvertItemLookPacket() : GamePacket(CSOffsets.CSConvertItemLookPacket, 5)
{
    public override void Read(PacketStream stream)
    {
        if (Connection.ActiveChar is not { Inventory.Bag: { } bag } character || stream.LeftBytes < sizeof(ulong) * 2 + 3)
            return;

        var baseId = stream.ReadUInt64();
        var lookId = stream.ReadUInt64();
        var npcObjId = stream.ReadBc();
        var result = bag.ConvertItemLook(character, baseId, lookId, npcObjId, out var materialId);
        if (result != ErrorMessageType.NoErrorMessage)
            character.SendErrorMessage(result, materialId);
    }
}
