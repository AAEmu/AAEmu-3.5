using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class RemoveAllDoodad : SpecialEffectAction
{
    private const uint LeviathanJumpSkillId = 27749;

    protected override SpecialType SpecialEffectActionType => SpecialType.RemoveAllDoodad;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int radiusMillimeters,
        int value2,
        int value3,
        int value4)
    {
        // In 3.5 only Leviathan's Jump uses this effect. The plot handles shipyards in a separate branch.
        if (caster is not Npc || castObj is not CastPlot || skill?.Id != LeviathanJumpSkillId ||
            value2 != 0 || value3 != 0 || value4 != 0)
            return;

        foreach (var doodad in DoodadRemovalResolver.FindInRange(caster, target, skill, radiusMillimeters))
            if (doodad.Template is { DespawnOnCollision: true } && doodad.OwnerObjId != 0 && !doodad.IsPersistent &&
                DoodadRemovalResolver.IsLooseInWorld(doodad, caster.ParentWorld))
                doodad.Delete();
    }
}
