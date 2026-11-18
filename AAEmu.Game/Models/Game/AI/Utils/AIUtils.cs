using System.Numerics;

using AAEmu.Game.Core.Managers.Bots.Navigation;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.AI;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Physics.Static;
using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.Utils;

public static class AiUtils
{

    // This is taken from x2ai.lua
    public static Vector3 CalcNextRoamingPosition(NpcAi ai) =>
        TryPlanRoamLeg(ai, out var goal, out _) ? goal : ai.IdlePosition;

    /// <summary>Roam goal plus mesh/geo waypoints (Zones <c>LegPlanner</c> / <c>NpcMover</c>).</summary>
    public static bool TryPlanRoamLeg(NpcAi ai, out Vector3 goal, out List<Vector3> waypoints)
    {
        goal = ai.IdlePosition;
        waypoints = [];
        var maxAttempts = RoamMovementRules.RoamTargetAttempts;
        var geo = ai.Owner.ParentWorld?.Template?.GeoData;
        var footZ = ai.Owner.Transform.World.Position.Z;
        var start = ai.Owner.Transform.World.Position;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var newPosition = RoamTargetRules.PickHorizontalTarget(ai, ai.IdlePosition, footZ);
            var zoneId = ResolveZoneKey(ai, newPosition.X, newPosition.Y);
            newPosition.Z = WorldManager.Instance.GetReferenceHeight(ai, newPosition.X, newPosition.Y, footZ, zoneId);
            if (geo is { } navigation && navigation.CheckImpossibleWalk(newPosition))
                continue;
            if (!RoamMovementRules.IsMeshWalkable(ai.Owner.ParentWorld?.Template, newPosition.X, newPosition.Y))
                continue;
            if (RoamMovementRules.WallCheckEnabled
                && WalkerCollision.Enabled
                && ai.Owner.ParentWorld?.Template is { } wallTemplate)
            {
                var resolver = WorldManager.Instance.NpcGroundResolver;
                if (!resolver.IsClearPath(
                        wallTemplate,
                        zoneId,
                        ai.IdlePosition,
                        newPosition,
                        RoamMovementRules.RoamWallProbeHeightMetres))
                    continue;
            }

            List<Vector3>? path = null;
            if (RoamMovementRules.Enabled && ai.Owner.ParentWorld is { } world
                && (geo != null || NavMeshGround.IsEnabled))
            {
                var chord = RoamMovementRules.Flat(ai.IdlePosition, newPosition);
                ai.PathNode.ZoneKey = ResolveZoneKey(ai, start.X, start.Y);
                path = ai.PathNode.FindPath(world, start, newPosition);
                if (!RoamMovementRules.IsAcceptableRoamPath(path, chord))
                    continue;
            }

            goal = newPosition;
            waypoints = path is { Count: >= 2 }
                ? path.Select(p => SnapPathPoint(ai, p)).ToList()
                : [SnapPathPoint(ai, start), SnapPathPoint(ai, newPosition)];
            return true;
        }

        return false;
    }

    /// <summary>Высота точки движения по слою P4 (reference — ноги НПС).</summary>
    public static Vector3 SnapWalkTarget(NpcAi ai, Vector3 horizontal)
    {
        var footZ = ai.Owner.Transform.World.Position.Z;
        horizontal.Z = WorldManager.Instance.GetReferenceHeight(
            ai, horizontal.X, horizontal.Y, footZ, ResolveZoneKey(ai, horizontal.X, horizontal.Y));
        return horizontal;
    }

    /// <summary>Высота точки маршрута A* по P4 (reference — Z самой точки).</summary>
    public static Vector3 SnapPathPoint(NpcAi ai, Vector3 point)
    {
        // Мира ещё нет (загрузка, тесты, юнит вне мира): правила опоры нечего спросить, точка остаётся как есть.
        if (ai.Owner.ParentWorld?.Template is null)
            return point;
        point.Z = WorldManager.Instance.GetReferenceHeight(
            ai, point.X, point.Y, point.Z, ResolveZoneKey(ai, point.X, point.Y));
        return point;
    }

    private static uint ResolveZoneKey(NpcAi ai, float x, float y) =>
        ai.Owner.ParentWorld?.Template is { } template
            ? WorldManager.Instance.GetZoneId(template, x, y)
            : ai.Owner.Transform.ZoneId;

    /// <summary>Очередь waypoints для chase/return (legacy simplify только при <see cref="PathPlanningRules.UseLegacyGeoPathSimplify"/>).</summary>
    public static Queue<Vector3> BuildPathQueue(NpcAi ai, WorldInstance world, List<Vector3> path, Vector3 endPoint)
    {
        if (path.Count == 0)
            return [];

        var list = new List<Vector3>(path);
        if (MathUtil.CalculateDistance(list[^1], endPoint, true) > 0.5f)
            list.Add(endPoint);

        IEnumerable<Vector3> waypoints = list;
        if (PathPlanningRules.UseLegacyGeoPathSimplify && world.Template.GeoData is { } geoData)
            waypoints = geoData.ReducePath(list, 10);

        var queue = new Queue<Vector3>();
        foreach (var point in waypoints)
            queue.Enqueue(SnapPathPoint(ai, point));
        return queue;
    }

    public static NpcAi GetAiByType(AiParamType type, Npc owner)
    {
        switch (type)
        {
            case AiParamType.AlmightyNpc:
                return new AlmightyNpcAiCharacter { Owner = owner };
            case AiParamType.ArcherHoldPosition:
                return new ArcherHoldPositionAiCharacter { Owner = owner };
            case AiParamType.ArcherRoaming:
                return new ArcherRoamingAiCharacter { Owner = owner };
            case AiParamType.BigMonsterRoaming:
                return new BigMonsterRoamingAiCharacter { Owner = owner };
            case AiParamType.BigMonsterHoldPosition:
                return new BigMonsterHoldPositionAiCharacter { Owner = owner };
            case AiParamType.Default:
                return new DefaultAiCharacter { Owner = owner };
            case AiParamType.Dummy:
                return new DummyAiCharacter { Owner = owner };
            case AiParamType.Flytrap:
                return new FlytrapAiCharacter { Owner = owner };
            case AiParamType.HoldPosition:
                return new HoldPositionAiCharacter { Owner = owner };
            case AiParamType.Roaming:
                return new RoamingAiCharacter { Owner = owner };
            case AiParamType.TowerDefenseAttacker:
                return new TowerDefenseAttackerAiCharacter { Owner = owner };
            case AiParamType.WildBoarHoldPosition:
                return new WildBoarHoldPositionAiCharacter { Owner = owner };
            case AiParamType.WildBoarRoaming:
                return new WildBoarRoamingAiCharacter { Owner = owner };
            default:
                return null;
        }
    }
}
