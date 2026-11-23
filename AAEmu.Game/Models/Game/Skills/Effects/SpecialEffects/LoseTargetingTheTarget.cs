using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class LoseTargetingTheTarget : SpecialEffectAction
{
    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (target is not Unit unit)
            return;

        LoseTarget.ClearFor(unit);
        if (unit.ParentWorld is not { } world)
            return;

        foreach (var holder in world.GetUnitsTargeting(unit))
        {
            if (SkillTargetingUtil.IsTargetRestricted(holder, unit) && ReferenceEquals(holder.CurrentTarget, unit))
                LoseTarget.ClearFor(holder);
        }
    }
}
