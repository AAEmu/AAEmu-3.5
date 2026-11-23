using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class CancelOngoingBuff : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.CancelOngoingBuff;
    
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
        if (target == null)
            return;

        if (value1 > 0)
            target.Buffs.RemoveBuffs((uint)value1, int.MaxValue);
        if (value2 > 0)
            target.Buffs.RemoveBuff((uint)value2);
    }
}
