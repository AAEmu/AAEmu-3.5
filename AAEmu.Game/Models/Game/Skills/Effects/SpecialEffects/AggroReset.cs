using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class AggroReset : SpecialEffectAction
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
        if (target is not Unit affected)
            return;

        var resetAll = value1 == 0 && value2 == 0 && value3 == 0 && value4 == 0;
        if (resetAll)
            affected.CharacterTagging.ClearAllTaggers();

        foreach (var entry in affected.AggroTable)
        {
            var aggro = entry.Value;
            if (resetAll || aggro.ResetComponents(value1 != 0, value2 != 0, value3 != 0, value4) <= 0)
                affected.ClearAggroOfUnit(aggro.Owner);
        }
    }
}
