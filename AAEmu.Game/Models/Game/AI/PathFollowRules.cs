using System.Globalization;
using System.Numerics;

using AAEmu.Game.Models.Game.AI.v2.Controls;
using AAEmu.Game.Models.Game.AI.v2.Framework;

namespace AAEmu.Game.Models.Game.AI;

/// <summary>Designer patrol parity (ArcheageZones <c>PathWalkers</c> / <c>follow_path.lua</c> look-ahead).</summary>
public static class PathFollowRules
{
    /// <summary><c>AAEMU_ZONE_QUEST_FOLLOW_PATH=0</c> disables quest/command-set designer paths (Zones default: on).</summary>
    public static bool QuestFollowPathEnabled { get; } =
        Environment.GetEnvironmentVariable("AAEMU_ZONE_QUEST_FOLLOW_PATH") != "0";

    /// <summary><c>AAEMU_LEGACY_DISK_PATH=1</c> enables <c>Data/Path/*.path</c>; <c>AIPath_*</c> always uses <c>npc.xml</c>.</summary>
    public static bool LegacyDiskPathEnabled { get; } =
        Environment.GetEnvironmentVariable("AAEMU_LEGACY_DISK_PATH") == "1";

    /// <summary>Whether <see cref="NpcAi"/> may load a route from disk instead of designer data.</summary>
    public static bool AllowsLegacyDiskPath(string? pathName)
    {
        if (!LegacyDiskPathEnabled || string.IsNullOrWhiteSpace(pathName))
            return false;
        return !pathName.StartsWith("AIPath_", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Open-world <c>AIPath_*</c> uses <c>AiPathHandler</c> only; legacy names mirror into <see cref="AAEmu.Game.Models.Game.Units.Route.Simulation"/> for GM/quest.</summary>
    public static bool ShouldMirrorPathInSimulation(string? pathName)
    {
        if (string.IsNullOrWhiteSpace(pathName))
            return false;
        if (pathName.StartsWith("AIPath_", StringComparison.OrdinalIgnoreCase))
            return false;
        return LegacyDiskPathEnabled;
    }

    /// <summary>follow_path.lua: start at first point at least this far from the walker (m).</summary>
    public const float LookAheadMetres = 10f;

    /// <summary>Zones <c>FollowPathEndAccuracy</c> (m); legacy <see cref="AAEmu.Game.Models.Game.Units.Route.Simulation"/> checkpoint advance.</summary>
    public const float FollowPathEndAccuracy = 10f;

    /// <summary>Dense designer routes (<c>npc.xml</c>): visit each leg before dequeuing (Zones follow_path legs).</summary>
    public const float PatrolWaypointArrivalMetres = 2.5f;

    /// <summary><c>AIPath_{zoneKey}_…</c> from <c>editor/npc.xml</c>.</summary>
    public static bool TryParseZoneKeyFromPathName(string pathName, out uint zoneKey)
    {
        zoneKey = 0;
        if (!pathName.StartsWith("AIPath_", StringComparison.OrdinalIgnoreCase))
            return false;
        var rest = pathName.AsSpan(7);
        var underscore = rest.IndexOf('_');
        if (underscore <= 0)
            return false;
        return uint.TryParse(rest[..underscore], NumberStyles.Integer, CultureInfo.InvariantCulture, out zoneKey);
    }

    /// <summary>Route from the first point at least <paramref name="lookAheadMetres"/> from <paramref name="here"/>; Z from walker.</summary>
    public static List<AiPathPoint> TrimLookAhead(IReadOnlyList<AiPathPoint> route, Vector3 here, float lookAheadMetres = LookAheadMetres)
    {
        if (route.Count == 0)
            return [];

        var start = route.Count - 1;
        var here2 = new Vector2(here.X, here.Y);
        for (var i = 0; i < route.Count; i++)
        {
            var p = route[i].Position;
            if (Vector2.Distance(new Vector2(p.X, p.Y), here2) >= lookAheadMetres)
            {
                start = i;
                break;
            }
        }

        var trimmed = new List<AiPathPoint>(route.Count - start);
        for (var i = start; i < route.Count; i++)
        {
            var src = route[i];
            trimmed.Add(new AiPathPoint
            {
                Position = new Vector3(src.Position.X, src.Position.Y, here.Z),
                Action = src.Action,
                Param = src.Param
            });
        }

        return trimmed;
    }

    /// <summary><c>Rec._Speed</c> from <c>npc.xml</c> scales template patrol run speed (Zones <c>follow_path</c>).</summary>
    public static float LegMetresPerSecond(NpcAi ai, float pathSpeedMultiplier) =>
        (float)ai.GetRealMovementSpeed(ai.Owner.BaseMoveSpeed * pathSpeedMultiplier);
}