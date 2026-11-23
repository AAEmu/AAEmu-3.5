using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ResetCooldown : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ResetCooldown;

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
        if (caster is not Character character)
            return;

        Logger.Debug("Special effects: ResetCooldown skillId {0}, tagId {1}, gcd {2}, value4 {3}", value1, value2, value3, value4);
        var gcd = value3 == 1;
        if (value1 > 0)
            character.ResetSkillCooldown((uint)value1, gcd, resetSkillTags: value4 != 0);
        if (value2 > 0)
            character.ResetSkillTagCooldown((uint)value2, gcd);
    }
}
