using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class LearnSpecialAbility : SpecialEffectAction
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
        if (caster is not Character character || skill is not { Cancelled: false })
            return;

        if (casterObj is SkillCasterUnit unit && unit.ObjId == character.ObjId &&
            ReferenceEquals(target, character) && skillObject is SkillObjectSpecialAbility { Ability: <= byte.MaxValue } choice &&
            TryGetCost(skill, out var catalystId, out var catalystCount) &&
            character.Abilities?.TryLearnSpecialAbility((AbilityType)choice.Ability, catalystId, catalystCount) == true)
        {
            character.SendPacket(new SCSpecialAbilityActivedPacket((byte)choice.Ability));
            return;
        }

        skill.Cancelled = true;
    }

    private static bool TryGetCost(Skill skill, out uint itemId, out int count)
    {
        foreach (var effect in skill.Template.Effects)
        {
            if (effect.Template is not SpecialEffect { SpecialEffectTypeId: SpecialType.LearnSpecialAbility } ||
                !effect.ConsumeSourceItem || effect.ConsumeItemId == 0 || effect.ConsumeItemCount <= 0)
                continue;

            itemId = effect.ConsumeItemId;
            count = effect.ConsumeItemCount;
            return true;
        }

        itemId = 0;
        count = 0;
        return false;
    }
}
