using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class ReportCrimeEffect : EffectTemplate
{
    public int Value { get; set; }
    public uint CrimeKindId { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        // AAC 3.5 uses evidence skill 11672 to open the reporting interaction.
        // The client submits the selected evidence and message in CSReportCrime.
        // The self-targeted skills with this effect do not submit that report.
    }
}
