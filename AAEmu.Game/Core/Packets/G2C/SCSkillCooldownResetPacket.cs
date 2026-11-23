using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSkillCooldownResetPacket : GamePacket
{
    private readonly Character _chr;
    private readonly uint _skillId;
    private readonly uint _tagId;
    private readonly bool _gcd;
    private readonly bool _resetSkillTags;

    public SCSkillCooldownResetPacket() : base(SCOffsets.SCSkillCooldownResetPacket, 5)
    {
    }

    public SCSkillCooldownResetPacket(Character chr, uint skillId, uint tagId, bool gcd, bool resetSkillTags = false) : base(SCOffsets.SCSkillCooldownResetPacket, 5)
    {
        _chr = chr;
        _skillId = skillId;
        _tagId = tagId;
        _gcd = gcd;
        _resetSkillTags = resetSkillTags;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_chr.ObjId);      // unitId (BC)
        stream.Write((ushort)_skillId);  // type
        stream.Write((ushort)_tagId);    // type
        stream.Write(_gcd);              // gc
        stream.Write(_resetSkillTags);   // rstc
        stream.Write(false);             // rtsc
        stream.Write(false);             // rtstc

        return stream;
    }
}
