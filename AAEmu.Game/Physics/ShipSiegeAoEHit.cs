#nullable enable

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.Game.Units;
using System.Numerics;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Physics.Hulls;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Physics;

/// <summary>
/// Siege AoE from a ground/world position uses <see cref="WorldManager.GetAround"/> pivot + <see cref="Unit.ModelSize"/>,
/// which misses long ships when the shell lands on the hull away from the pivot. Adds hostile boats whose
/// <c>ship_models</c> mass OBB (XZ) intersects the siege circle, without duplicating targets already collected.
/// </summary>
public static class ShipSiegeAoEHit
{
    /// <summary>
    /// Extra meters added to siege radius for <see cref="WorldManager.GetAround{T}"/> so pivots outside the skill circle
    /// still pick up long hulls. Tuned below a conservative 200 m: largest playable hulls are ~50 m LOA; mass-box half-length,
    /// beam, and center offset from pivot stay well under ~100 m in normal ship_models data.
    /// </summary>
    public const float PivotQuerySlackMeters = 100f;

    public static void AppendHostileShipsHitBySiegeHullAoE(
        BaseUnit caster,
        SkillTemplate template,
        BaseUnit targetSelf,
        SkillCastTarget? targetCaster,
        List<BaseUnit> possibleTargets)
    {
        if (template.TargetAreaRadius <= 0)
            return;
        if ((DamageType)template.DamageTypeId != DamageType.Siege)
            return;
        if (targetCaster is not (SkillCastPositionTarget or SkillCastPosition2Target or SkillCastPosition3Target))
            return;

        var radius = (float)template.TargetAreaRadius;
        var wx = targetSelf.Transform.World.Position.X;
        var wy = targetSelf.Transform.World.Position.Y;

        var queryR = radius + PivotQuerySlackMeters;
        var nearby = WorldManager.GetAround<Slave>(targetSelf, queryR, false);

        var seen = new HashSet<uint>();
        foreach (var t in possibleTargets)
            seen.Add(t.ObjId);

        foreach (var ship in nearby)
        {
            if (seen.Contains(ship.ObjId))
                continue;
            if (!ship.Template.IsABoat())
                continue;
            if (ship.PhysicsState is null || ship.ShipModel is null)
                continue;
            if (!SkillTargetingUtil.IsRelationValid(template.TargetRelation, caster, ship))
                continue;
            if (!TrySiegePointHitsShipMassBoxXz(wx, wy, radius, ship))
                continue;

            possibleTargets.Add(ship);
            seen.Add(ship.ObjId);
        }
    }

    public static bool TrySiegePointHitsShipMassBoxXz(float worldX, float worldY, float radius, Slave ship)
    {
        var state = ship.PhysicsState;
        return state != null && ship.ShipModel != null && CircleHitsHull(worldX, worldY, radius, state.Pose, ship.ShipModel, ship.Scale);
    }

    public static bool CircleHitsHull(float x, float y, float radius, HullPose pose, ShipModelV1 model, float scale)
    {
        var relative = new Vector3(x - (float)pose.X, y - (float)pose.Y, 0f);
        var local = Vector3.Transform(relative, Quaternion.Conjugate(pose.Rotation));
        var dx = local.X - model.MassCenterX * scale;
        var dy = local.Y - model.MassCenterY * scale;
        var halfWidth = MathF.Max(0.01f, model.MassBoxSizeX * scale * 0.5f);
        var halfLength = MathF.Max(0.01f, model.MassBoxSizeY * scale * 0.5f);
        var ox = dx - Math.Clamp(dx, -halfWidth, halfWidth);
        var oy = dy - Math.Clamp(dy, -halfLength, halfLength);
        return ox * ox + oy * oy <= radius * radius;
    }
}
