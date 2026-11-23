using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Utils;

public static class SkillTargetingUtil
{
    internal static bool IsTargetRestricted(BaseUnit targeter, BaseUnit target) =>
        target != null && !ReferenceEquals(targeter, target) &&
        target.Buffs is Units.Buffs collection && collection.RestrictsTargetSelection(targeter);

    public static IEnumerable<T> FilterWithRelation<T>(SkillTargetRelation relation, T caster, IEnumerable<T> units) where T : BaseUnit
    {
        switch (relation)
        {
            case SkillTargetRelation.Any:
                return units;
            case SkillTargetRelation.Friendly:
                return units.Where(o => caster.GetRelationStateTo(o) == RelationState.Friendly && !caster.CanAttack(o));
            case SkillTargetRelation.Hostile:
                return units.Where(caster.CanAttack);
            case SkillTargetRelation.Party:
                return units;
            case SkillTargetRelation.Raid:
                var team = TeamManager.Instance.GetTeamByObjId(caster.ObjId);
                var mate = caster.ParentWorld.MateManager.GetActiveMates(caster.Id).FirstOrDefault(); // TODO: How to handle multiple pets?
                units = team == null ? units.Where(o => o.ObjId == mate?.ObjId) : units.Where(o => team.IsObjMember(o.ObjId));
                return units.Append(caster);
            case SkillTargetRelation.Others:
                return units.Where(o => caster.GetRelationStateTo(o) == RelationState.Neutral);
            case SkillTargetRelation.Family:
                return FilterFamily(caster, units);
            default:
                return units;
        }
    }

    public static bool IsRelationValid(SkillTargetRelation relation, BaseUnit caster, BaseUnit target)
    {
        switch (relation)
        {
            case SkillTargetRelation.Any:
                return true;
            case SkillTargetRelation.Friendly:
                return caster?.GetRelationStateTo(target) == RelationState.Friendly && !caster.CanAttack(target);
            case SkillTargetRelation.Hostile:
                return caster.CanAttack(target);
            case SkillTargetRelation.Party:
                return true;
            case SkillTargetRelation.Raid:
                if (target == caster) return true;
                var team = TeamManager.Instance.GetTeamByObjId(caster.ObjId);
                return team?.IsObjMember(target.ObjId) ?? false;
            case SkillTargetRelation.Others:
                // return caster.GetRelationStateTo(target) == RelationState.Neutral;
                return caster?.ObjId != target.ObjId;
            case SkillTargetRelation.Family:
                return IsSameFamily(caster, target);
            default:
                return true;
        }
    }

    private static IEnumerable<T> FilterFamily<T>(BaseUnit caster, IEnumerable<T> units) where T : BaseUnit
    {
        foreach (var unit in units)
            if (IsSameFamily(caster, unit))
                yield return unit;
    }

    private static bool IsSameFamily(BaseUnit caster, BaseUnit target) =>
        caster is Character { Family: > 0 } source && target is Character other && source.Family == other.Family;
}
