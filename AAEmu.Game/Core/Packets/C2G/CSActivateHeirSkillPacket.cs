using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSActivateHeirSkillPacket() : GamePacket(CSOffsets.CSActivateHeirSkillPacket, 5)
{
    // x2game_dump.dll 0x392669E0, serializer 0x39931EA0.
    public override void Read(PacketStream stream)
    {
        var groupId = stream.ReadUInt16();
        var successorId = stream.ReadUInt16();
        var isChange = stream.ReadBoolean();
        Connection?.ActiveChar?.Skills?.TryActivateHeirSkill(groupId, successorId, isChange);
    }
}
