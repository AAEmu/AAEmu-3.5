using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

public class TickEffect
{
    public bool CheckNoTargetTagSrc { get; set; }
    public bool CheckTargetTagSrc { get; set; }
    public uint EffectId { get; set; }
    public uint TargetBuffTagId { get; set; }
    public uint TargetNoBuffTagId { get; set; }

    internal bool MeetsTargetBuffTags(BaseUnit target, BaseUnit caster) =>
        (TargetBuffTagId == 0 || SkillTargetingUtil.HasBuffTag(target, TargetBuffTagId, caster, CheckTargetTagSrc)) &&
        (TargetNoBuffTagId == 0 || !SkillTargetingUtil.HasBuffTag(target, TargetNoBuffTagId, caster, CheckNoTargetTagSrc));
}
