using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCGradeEnchantResultPacket(byte result, Item item, byte type1, byte type2)
    : GamePacket(SCOffsets.SCGradeEnchantResultPacket, 5)
{
    // result :
    //  0 = break, 1 = downgrade, 2 = fail, 3 = success, 4 = great success 

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(result);
        stream.Write(item);
        stream.Write(type1);
        stream.Write(type2);
        stream.Write(0u); // break reward item template
        stream.Write(0); // break reward item count
        stream.Write(false); // break reward by mail

        return stream;
    }
}
