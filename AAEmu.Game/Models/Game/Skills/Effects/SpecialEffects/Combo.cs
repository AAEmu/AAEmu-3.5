using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class Combo : SpecialEffectAction
{
    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int comboSkillId,
        int timeFromNow,
        int value3,
        int value4)
    {
        if (caster is Character character && comboSkillId > 0)
            character.Skills?.OpenComboFollowUp((uint)comboSkillId, timeFromNow);
    }
}
