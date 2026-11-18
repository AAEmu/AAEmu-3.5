using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.AI;

/// <summary>
/// Open-world designer path walks (ArcheageZones <c>PathWalkers</c>). Native: almost only underwater-leader ground members;
/// private servers often want all <c>AIPath_*</c> from <c>npc_spawners.g</c>.
/// </summary>
public static class PathWalkerRules
{
    public static bool GroupPathWalkEnabled { get; } =
        Environment.GetEnvironmentVariable("AAEMU_ZONE_GROUP_PATH_WALK") != "0";

    /// <summary>When on (default), any spawner with a loaded designer path may enter <c>FollowPath</c>.</summary>
    public static bool OpenWorldDesignerPatrolEnabled { get; } =
        Environment.GetEnvironmentVariable("AAEMU_ZONE_OPEN_WORLD_PATROL") != "0";

    public const float CruiseFactor = 0.88f;

    /// <summary>Native open-world group path: first walking records ~5.0–6.3 s after spawn (median 5.6 s).</summary>
    public const long StartDelayMs = 5_500;

    public static bool ShouldEnterFollowPath(Npc npc)
    {
        if (npc.Ai?.PathHandler.HasPathMovementData() != true)
            return false;

        var follow = npc.Spawner?.FollowPath;
        if (string.IsNullOrWhiteSpace(follow))
            return true;

        if (!follow.StartsWith("AIPath_", StringComparison.OrdinalIgnoreCase))
            return true;

        if (OpenWorldDesignerPatrolEnabled)
            return true;

        // ArcheageZones slice 5: dungeon / instance placements walk designer paths on bind.
        if (npc.ParentWorld?.DungeonInstance is not null)
            return true;

        return GroupPathWalkEnabled && WalksGroupPlacementPath(npc);
    }

    /// <summary>Ground member of a group whose move leader is a flier/swimmer (underwater leader proxy).</summary>
    public static bool WalksGroupPlacementPath(Npc npc)
    {
        if (npc.CanFly || npc.Spawner?.Template?.Npcs is not { Count: > 0 } spawnerLines)
            return false;

        foreach (var line in spawnerLines)
        {
            if (!string.Equals(line.MemberType, "NpcGroup", StringComparison.OrdinalIgnoreCase))
                continue;

            var members = NpcGroupGameData.Instance.GetNpcGroupMembers((int)line.MemberId);
            if (members.Count == 0)
                continue;

            if (members.All(m => m.NpcId != (int)npc.TemplateId))
                continue;

            var leaderMember = members.FirstOrDefault(m => m.IsMoveLeader)
                ?? members.FirstOrDefault(m => m.IsLeader)
                ?? members[0];
            var leaderTemplate = NpcManager.Instance.GetTemplate((uint)leaderMember.NpcId);
            if (leaderTemplate == null)
                continue;

            var leaderFlies = ModelManager.Instance.IsFlyOrSwim(leaderTemplate.ModelId);
            return leaderFlies;
        }

        return false;
    }

    /// <summary>Nearest authored placement point (XY) for group path anchor (Zones <c>PathWalkers.Anchor</c>).</summary>
    public static NpcSpawnPoint? AnchorPlacementPoint(IReadOnlyList<NpcSpawnPoint> points, Vector3 memberLocal)
    {
        if (points.Count == 0)
            return null;

        var best = points[0];
        var bestDist = float.MaxValue;
        foreach (var p in points)
        {
            var dx = p.Position.X - memberLocal.X;
            var dy = p.Position.Y - memberLocal.Y;
            var d2 = dx * dx + dy * dy;
            if (d2 < bestDist)
            {
                bestDist = d2;
                best = p;
            }
        }

        return best;
    }

    /// <summary>Designer json row is dropped when <paramref name="clientZonesWithSpawnerFile"/> contains the path zone key.</summary>
    public static bool JsonPatrolSupersededByClient(string? followPath, IReadOnlyCollection<uint> clientZonesWithSpawnerFile,
        bool clientSpawnEnabled)
    {
        if (!clientSpawnEnabled || string.IsNullOrWhiteSpace(followPath))
            return false;
        if (!PathFollowRules.TryParseZoneKeyFromPathName(followPath, out var zoneKey))
            return false;
        return clientZonesWithSpawnerFile.Contains(zoneKey);
    }
}