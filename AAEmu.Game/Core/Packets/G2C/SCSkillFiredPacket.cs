using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// SCSkillFired (0x11B) — server-to-client packet sent when a skill is fired.
/// </summary>
/// <remarks>
/// x2game client deserializer: <c>sub_39943580</c>.
/// Wire layout derived from IDA:
/// <list type="bullet">
///   <item><description>UInt16 "tl" (packet offset 0x10)</description></item>
///   <item><description>caster</description></item>
///   <item><description>target</description></item>
///   <item><description>skillObject</description></item>
///   <item><description>UInt16 EffectDelay / 10 (packet offset 0x148; client multiplies by 10)</description></item>
///   <item><description>UInt16 ChannelingTime / 10 (packet offset 0x150)</description></item>
///   <item><description>byte "f" extra-data flags (packet offset 0x154, parsed by <c>sub_39921B90</c>; currently empty)</description></item>
///   <item><description>PISC array [skillId, FireAnimId] (offsets 0x0C and 0x160)</description></item>
///   <item><description>byte "flag" (bits 0/1 stored at client offsets 0x14C and 0x164)</description></item>
/// </list>
/// </remarks>
public class SCSkillFiredPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    private readonly uint _skillId;
    private readonly ushort _tlId;
    private readonly SkillCaster _caster;
    private readonly SkillCastTarget _target;
    private readonly SkillObject _skillObject;
    private readonly Skill _skill;

    public int EffectDelay { get; set; }
    public int ChannelingTime { get; set; }

    /// <summary>
    /// Fire animation id sent to the client.
    /// Default is the skill template's FireAnim id.
    /// </summary>
    public uint FireAnimId { get; set; }

    public SCSkillFiredPacket(uint skillId, ushort tlId, SkillCaster caster, SkillCastTarget target, Skill skill, SkillObject skillObject)
        : base(SCOffsets.SCSkillFiredPacket, 5)
    {
        _skillId = skillId;
        _tlId = tlId;
        _caster = caster;
        _target = target;
        _skill = skill;
        _skillObject = skillObject;
        ChannelingTime = skill.Template.ChannelingTime;
        FireAnimId = skill.Template.FireAnim?.Id ?? 0;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_tlId);
        stream.Write(_caster);
        stream.Write(_target);
        stream.Write(_skillObject);

        stream.Write((ushort)Math.Clamp(EffectDelay / 10, 0, ushort.MaxValue));
        stream.Write((ushort)Math.Clamp(ChannelingTime / 10, 0, ushort.MaxValue));

        // extra-data flags (sub_39921B90); no optional c/e/p/d data is currently sent.
        stream.Write((byte)0);

        stream.WritePisc(_skillId, FireAnimId);

        // flag byte (bits 0/1 stored at client offsets 0x14C and 0x164)
        stream.Write((byte)0);

        return stream;
    }

    public SCSkillFiredPacket SetEffectDelay(int effectDelay)
    {
        EffectDelay = effectDelay;
        return this;
    }

    public SCSkillFiredPacket SetChannelingTime(int channelingTime)
    {
        ChannelingTime = channelingTime;
        return this;
    }

    public SCSkillFiredPacket SetFireAnimId(uint fireAnimId)
    {
        FireAnimId = fireAnimId;
        return this;
    }

    public override string Verbose()
    {
        return $" - Id {_skillId}, TlId {_tlId}, Caster {_caster.ObjId}, Target {_target.ObjId}, Skill {_skill.Template.Id}";
    }
}
