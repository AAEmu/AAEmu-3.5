#nullable enable

using System.Globalization;
using System.Numerics;

namespace AAEmu.Game.Models.Game.World;

public enum NpcSpawnAreaType : byte
{
    /// <summary>Spawns at one of the listed points (the editor's <c>spawnAreaType point</c>).</summary>
    Point = 0,
    /// <summary>Spawns anywhere inside the triangulated polygon (<c>spawnAreaType area</c>).</summary>
    Area = 1
}

/// <summary>One authored spawn point: zone-local position and yaw in radians.</summary>
public readonly record struct NpcSpawnPoint(Vector3 Position, float ZRot);

/// <summary>A patrol path the spawner's NPCs follow (<c>paths/path ( pathName …, pointNo … )</c>).</summary>
public readonly record struct NpcSpawnPathRef(string PathName, int PointNo);

/// <summary>
/// One <c>spawner</c> block of a zone's <c>npc_spawners.g</c>: the placement of an
/// <c>npc_spawners</c> template (<see cref="SpawnerType"/>) at an authored point or inside an authored
/// polygon. Coordinates are zone-local; continent = origin cell × 1024 + local.
/// </summary>
public sealed class NpcSpawnerPlacement
{
    /// <summary>The editor's placement id (<c>spawnerId</c>); the <c>sid</c> of ZWSpawnNpc / WZNpcState.</summary>
    public required uint SpawnerId { get; init; }
    /// <summary>The <c>npc_spawners</c> template id (<c>spawnerType</c>).</summary>
    public required uint SpawnerType { get; init; }
    public required NpcSpawnAreaType AreaType { get; init; }
    /// <summary>The <c>roamingArea</c> id of an area spawner (its own placement id in practice); 0 when absent.</summary>
    public uint RoamingArea { get; init; }
    /// <summary>Authored points. A point spawner picks one of them; an area spawner's points are the polygon outline.</summary>
    public required IReadOnlyList<NpcSpawnPoint> Points { get; init; }
    /// <summary>The polygon's triangulation with per-triangle area shares (area spawners only).</summary>
    public required IReadOnlyList<SpawnAreaTriangle> Triangles { get; init; }
    public required IReadOnlyList<NpcSpawnPathRef> Paths { get; init; }
    /// <summary>1-based source line of the block, for diagnostics.</summary>
    public int Line { get; init; }

    /// <summary>The first authored point: the placement's anchor for distance checks.</summary>
    public NpcSpawnPoint Anchor => Points[0];

    /// <summary>True when the zone-local point lies inside one of the area triangles (XY only).</summary>
    public bool ContainsXY(float x, float y)
    {
        foreach (var triangle in Triangles)
            if (ContainsXY(triangle, x, y))
                return true;
        return false;
    }

    public static bool ContainsXY(SpawnAreaTriangle t, float x, float y)
    {
        var d1 = Edge(x, y, t.A, t.B);
        var d2 = Edge(x, y, t.B, t.C);
        var d3 = Edge(x, y, t.C, t.A);
        var hasNegative = d1 < 0 || d2 < 0 || d3 < 0;
        var hasPositive = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNegative && hasPositive);
    }

    private static float Edge(float px, float py, Vector3 a, Vector3 b) =>
        (px - b.X) * (a.Y - b.Y) - (a.X - b.X) * (py - b.Y);
}

/// <summary>
/// Reads a zone's <c>npc_spawners.g</c> (<c>level_design/zone/{zoneKey}/zone_server/</c>). Shared by the
/// World's placement catalog and the headless zone host, so both see the same placements.
/// </summary>
/// <remarks>
/// Blocks without a <c>spawnerId</c>, a <c>spawnerType</c> or at least one positioned <c>point</c> (or a
/// direct <c>pos</c>) are skipped: nothing can be spawned from them. Everything else in the block (<c>paths</c>,
/// <c>roamingArea</c>, the <c>triInfos</c> of an area) is kept.
/// </remarks>
public static class NpcSpawnersFileRules
{
    public const string FileName = "npc_spawners.g";

    /// <summary>Where the file lives under an extracted game data root.</summary>
    public static string PathFor(string root, string worldName, uint zoneKey) =>
        Path.Combine(
            root, "worlds", worldName, "level_design", "zone",
            zoneKey.ToString(CultureInfo.InvariantCulture), "zone_server", FileName);

    public static List<NpcSpawnerPlacement> ParseFile(string path) => Parse(File.ReadAllText(path));

    public static List<NpcSpawnerPlacement> Parse(string text) => Parse(text, out _);

    /// <param name="skipped">Blocks dropped for lacking an id, a type or a point.</param>
    public static List<NpcSpawnerPlacement> Parse(string text, out int skipped)
    {
        skipped = 0;
        var list = new List<NpcSpawnerPlacement>();
        foreach (var node in GTextFile.Parse(text))
        {
            if (!string.Equals(node.Name, "spawner", StringComparison.Ordinal))
                continue;
            if (TryRead(node) is { } placement)
                list.Add(placement);
            else
                skipped++;
        }

        return list;
    }

    private static NpcSpawnerPlacement? TryRead(GTextNode node)
    {
        if (!node.TryGetUInt32("spawnerId", out var id) || !node.TryGetUInt32("spawnerType", out var type))
            return null;

        var points = new List<NpcSpawnPoint>();
        if (node.Child("points") is { } pointsNode)
        {
            foreach (var point in pointsNode.ChildrenNamed("point"))
            {
                if (!GTextNode.TryReadVector3(point.ChildValue("pos"), out var pos))
                    continue;
                point.TryGetSingle("zRot", out var zRot);
                points.Add(new NpcSpawnPoint(pos, zRot));
            }
        }

        // A block that names its position directly ("pos … / zRot …" without a points list) is one point.
        if (points.Count == 0 && GTextNode.TryReadVector3(node.ChildValue("pos"), out var direct))
        {
            node.TryGetSingle("zRot", out var directRot);
            points.Add(new NpcSpawnPoint(direct, directRot));
        }

        if (points.Count == 0)
            return null;

        var areaType = string.Equals(node.ChildValue("spawnAreaType"), "area", StringComparison.OrdinalIgnoreCase)
            ? NpcSpawnAreaType.Area
            : NpcSpawnAreaType.Point;
        node.TryGetUInt32("roamingArea", out var roamingArea);

        var triangles = new List<SpawnAreaTriangle>();
        if (node.Child("triInfos") is { } triInfos)
        {
            foreach (var tri in triInfos.ChildrenNamed("triInfo"))
            {
                if (!GTextNode.TryReadVector3(tri.ChildValue("v1"), out var v1) ||
                    !GTextNode.TryReadVector3(tri.ChildValue("v2"), out var v2) ||
                    !GTextNode.TryReadVector3(tri.ChildValue("v3"), out var v3))
                    continue;
                tri.TryGetSingle("areaRate", out var rate);
                triangles.Add(new SpawnAreaTriangle { A = v1, B = v2, C = v3, Rate = rate });
            }
        }

        var paths = new List<NpcSpawnPathRef>();
        if (node.Child("paths") is { } pathsNode)
        {
            foreach (var path in pathsNode.ChildrenNamed("path"))
            {
                var fields = GTextNode.ReadTuple(path.Value);
                if (!fields.TryGetValue("pathName", out var name))
                    continue;
                var pointNo = 0;
                if (fields.TryGetValue("pointNo", out var pointNoText))
                    int.TryParse(pointNoText, NumberStyles.Integer, CultureInfo.InvariantCulture, out pointNo);
                paths.Add(new NpcSpawnPathRef(name, pointNo));
            }
        }

        return new NpcSpawnerPlacement
        {
            SpawnerId = id,
            SpawnerType = type,
            AreaType = areaType,
            RoamingArea = roamingArea,
            Points = points,
            Triangles = triangles,
            Paths = paths,
            Line = node.Line
        };
    }
}
