using System.Numerics;

using AAEmu.Game.Core.Managers.Bots.Navigation;

namespace AAEmu.Game.Models.Game.AI;

/// <summary>Лимиты <see cref="NavPlanner"/> как у ArcheageZones <c>LegPlanner</c> (chase / roam).</summary>
public static class LegPlannerRules
{
    public const float DefaultActorRadius = 0.55f;
    public const float OffMeshLegMaxMetres = 15f;

    public static bool OffMeshRoamEnabled
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("AAEMU_OFF_MESH_ROAM")
                ?? Environment.GetEnvironmentVariable("AAEMU_ZONE_OFFMESH_ROAM");
            return env is not ("0" or "false" or "False");
        }
    }

    /// <summary>Порт <see cref="MovementRules.OffMeshLegAllowed"/> (ArcheageZones).</summary>
    public static bool OffMeshLegAllowed(bool startWalkable, bool unreachable, bool formationWalk, float flatMetres) =>
        OffMeshRoamEnabled
        && flatMetres is > 0.01f and <= OffMeshLegMaxMetres
        && (!startWalkable || unreachable || formationWalk);

    public static NavOptions ChaseOptions(float actorRadius = DefaultActorRadius) => NavOptions.Default with
    {
        ActorRadius = MathF.Min(actorRadius, DefaultActorRadius),
        MaxWaterDepth = 1f,
        MaxSearchNodes = 6_000,
        Deadline = TimeSpan.FromMilliseconds(25),
    };

    public static NavOptions RoamOptions(float actorRadius = DefaultActorRadius) => NavOptions.Default with
    {
        ActorRadius = MathF.Min(actorRadius, DefaultActorRadius),
        MaxWaterDepth = 1f,
        MaxSearchNodes = 2_000,
        Deadline = TimeSpan.FromMilliseconds(20),
    };

    /// <summary>Выбор бюджета планировщика по длине хорды (roam ≤6 m, иначе chase).</summary>
    public static NavOptions ForLeg(Vector3 from, Vector3 to, float actorRadius = DefaultActorRadius)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var chord = MathF.Sqrt(dx * dx + dy * dy);
        return chord <= OffMeshLegMaxMetres ? RoamOptions(actorRadius) : ChaseOptions(actorRadius);
    }

    /// <summary>
    /// Прямой leg по полу сооружения (Zones <c>LegPlanner.StructureLeg</c>): оба конца на forbidden footprint
    /// с waypoint в пределах <see cref="StructureFloorRules"/>.
    /// </summary>
    public static bool TryStructureLeg(NavMeshGround nav, Vector3 from, Vector3 to, out List<Vector3> leg)
    {
        leg = [];
        if (!StructureFloorRules.Enabled)
            return false;

        if (!nav.TryStructureFloor(from.X, from.Y, from.Z, StructureFloorRules.FlatMetres, StructureFloorRules.RiseMetres, out _))
            return false;

        if (!nav.TryStructureFloor(to.X, to.Y, from.Z, StructureFloorRules.FlatMetres, StructureFloorRules.RiseMetres, out var toZ))
            return false;

        leg = [from, to with { Z = toZ }];
        return true;
    }

    /// <summary>Прямой короткий leg, когда mesh-route нет (Zones off-mesh roam).</summary>
    public static bool TryStraightLeg(
        Vector3 from,
        Vector3 to,
        NavMeshGround nav,
        bool unreachable,
        out List<Vector3> leg,
        bool formationMember = false)
    {
        leg = [];
        var chord = RoamMovementRules.Flat(from, to);
        var startWalkable = nav.IsWalkable(from.X, from.Y);
        if (!OffMeshLegAllowed(startWalkable, unreachable, formationMember, chord))
            return false;

        leg = [from, to];
        return true;
    }
}