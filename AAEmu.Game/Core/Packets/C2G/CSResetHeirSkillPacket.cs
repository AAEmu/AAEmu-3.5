using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSResetHeirSkillPacket() : GamePacket(CSOffsets.CSResetHeirSkillPacket, 5)
{
    // x2game_dump.dll 0x39266D00, serializer 0x39931EF0. The reply writes ability after successor.
    public override void Read(PacketStream stream)
    {
        var kind = stream.ReadUInt32();
        var ability = (AbilityType)stream.ReadByte();
        var successorId = stream.ReadUInt16();
        Connection?.ActiveChar?.Skills?.TryResetHeirSkills(kind, ability, successorId);
    }
}
