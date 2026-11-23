using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class RemoveDoodad : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.RemoveDoodad;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int doodadTemplateId,
        int radiusMillimeters,
        int value3,
        int value4)
    {
        if (caster?.ParentWorld is not { } world || doodadTemplateId <= 0)
            return;

        // SourceToPos can dispatch to individual units, while the cast keeps the original impact marker.
        if (castObj is CastSkill && skill?.Template is { TargetSelection: SkillTargetSelection.Target,
                TargetType: SkillTargetType.BallisticPos or SkillTargetType.ArtilleryPos })
        {
            if (skill.InitialTarget is { ObjId: uint.MaxValue } impact)
                target = impact;
            else if (target is not { ObjId: uint.MaxValue })
                return;
        }

        // Buff ticks pass an effect owner independently of their original source and may have no Skill.
        var candidates = castObj is CastBuff { Buff.Owner: BaseUnit owner }
            ? DoodadRemovalResolver.FindInRange(owner, world, radiusMillimeters)
            : DoodadRemovalResolver.FindInRange(caster, target, skill, radiusMillimeters);

        foreach (var doodad in candidates)
            if (doodad.TemplateId == (uint)doodadTemplateId && DoodadRemovalResolver.IsStandaloneInWorld(doodad, world))
                doodad.Delete();
    }
}
