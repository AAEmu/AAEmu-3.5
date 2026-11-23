using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class HealPet : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.HealPet;

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
        if (caster is not Character owner || skill?.Id != CharacterMates.RecoveryPotionSkillId)
            return;

        if (casterObj is SkillItem potion && owner.Mates?.TryUseRecoveryPotion(potion.ItemId, targetObj, time) == true)
            return;

        owner.ConditionChance = false;
        skill.Cancelled = true;
    }
}
