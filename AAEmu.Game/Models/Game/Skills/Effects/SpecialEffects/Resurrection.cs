using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class Resurrection : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.Resurrection;

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
        if (target is Character character && casterObj != null && skill is not { Cancelled: true })
            character.OfferResurrection(casterObj, value2, value3);
    }
}
