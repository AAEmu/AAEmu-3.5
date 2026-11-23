using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSHeirLevelUpPacket() : GamePacket(CSOffsets.CSHeirLevelUpPacket, 5)
{
    // Empty payload in x2game_dump.dll 0x39551E30.
    public override void Read(PacketStream stream) => Connection?.ActiveChar?.TryLevelUpHeir();
}
