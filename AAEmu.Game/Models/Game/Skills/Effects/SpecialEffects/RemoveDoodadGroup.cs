using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class RemoveDoodadGroup : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.RemoveDoodadGroup;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int doodadGroupId,
        int radiusMillimeters,
        int value3,
        int value4)
    {
        if (doodadGroupId <= 0)
            return;

        foreach (var doodad in DoodadRemovalResolver.FindInRange(caster, target, skill, radiusMillimeters))
            if (doodad.Template?.GroupId == (uint)doodadGroupId &&
                DoodadRemovalResolver.IsLooseInWorld(doodad, caster.ParentWorld))
                doodad.Delete();
    }
}
