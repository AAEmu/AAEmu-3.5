using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCharacterConnectionRestrictPacket() : GamePacket(CSOffsets.CSCharacterConnectionRestrictPacket, 5)
{
    public override void Read(PacketStream stream)
    {
        // The client acknowledges a successful restriction check with the character ID.
        var characterId = stream.ReadUInt32();
        Logger.Trace("Character connection restriction check acknowledged for {0}", characterId);
    }
}
