using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public sealed class SCUpdateSkillActiveTypePacket(uint skillId, byte activeType)
    : GamePacket(SCOffsets.SCUpdateSkillActiveTypePacket, 5)
{
    public override PacketStream Write(PacketStream stream) => stream.Write(skillId).Write(activeType);
}
