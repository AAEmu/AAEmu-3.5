using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSpecialAbilityActivedPacket(byte abilityId) : GamePacket(SCOffsets.SCSpecialAbilityActivedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(abilityId);
        return stream;
    }
}
