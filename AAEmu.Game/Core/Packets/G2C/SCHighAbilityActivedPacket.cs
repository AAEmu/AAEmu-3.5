using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHighAbilityActivedPacket(uint objId, byte oldKind, AbilityType oldAbility, byte newKind,
    AbilityType newAbility, bool isSwapAbilitySet) : GamePacket(SCOffsets.SCHighAbilityActivedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(oldKind);
        stream.Write((byte)oldAbility);
        stream.Write(newKind);
        stream.Write((byte)newAbility);
        stream.Write(isSwapAbilitySet);
        return stream;
    }
}
