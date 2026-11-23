using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

internal static class DoodadRemovalResolver
{
    internal static List<Doodad> FindInRange(BaseUnit caster, BaseUnit target, Skill skill, int radiusMillimeters)
    {
        var selection = skill?.Template?.TargetSelection;
        var anchor = selection switch
        {
            SkillTargetSelection.Source => caster,
            SkillTargetSelection.Target => target,
            _ => null
        };
        return FindInRange(anchor, caster?.ParentWorld, radiusMillimeters, selection == SkillTargetSelection.Target);
    }

    internal static List<Doodad> FindInRange(BaseUnit anchor, WorldInstance world, int radiusMillimeters, bool includeAnchor = false)
    {
        if (world == null || radiusMillimeters <= 0 || anchor?.Region == null || !ReferenceEquals(anchor.ParentWorld, world))
            return [];

        var candidates = world.GetDoodadsWithinRadius(anchor, radiusMillimeters / 1000f);
        if (includeAnchor && anchor is Doodad doodad)
            candidates.Add(doodad);

        return candidates;
    }

    internal static bool IsLooseInWorld(Doodad doodad, WorldInstance world) =>
        doodad.Spawner == null && IsStandaloneInWorld(doodad, world);

    internal static bool IsStandaloneInWorld(Doodad doodad, WorldInstance world) =>
        doodad.OwnerType is DoodadOwnerType.Character or DoodadOwnerType.System &&
        doodad.ParentObjId == 0 && doodad.ParentObj == null &&
        doodad.Transform.Parent == null && doodad.Transform.StickyParent == null &&
        doodad.IsVisible && ReferenceEquals(doodad.ParentWorld, world) &&
        ReferenceEquals(world.GetDoodad(doodad.ObjId), doodad);
}
