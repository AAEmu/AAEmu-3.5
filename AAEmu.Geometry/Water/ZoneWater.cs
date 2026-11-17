using System.Numerics;

using AAEmu.Geometry.Source;

namespace AAEmu.Geometry;

/// <summary>
/// The inland water of one zone: the water volumes (object type 11) of its cells' object files — lakes, ponds and river
/// pools over the terrain, each a contour polygon with a flat surface (the volume's higher end) and a depth. The open
/// sea (a volume at the world's ocean level) is left out. Continent coordinates; read-only once built.
/// </summary>
public sealed class ZoneWater
{
    private const float GridMetres = 64f;

    private readonly Body[] _bodies;
    private readonly Dictionary<(int, int), int[]> _grid;

    private ZoneWater(Body[] bodies, float? oceanLevel = null)
    {
        _bodies = bodies;
        OceanLevel = oceanLevel;
        var cells = new Dictionary<(int, int), List<int>>();
        for (var i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            for (var gx = (int)MathF.Floor(b.Min.X / GridMetres); gx <= (int)MathF.Floor(b.Max.X / GridMetres); gx++)
            for (var gy = (int)MathF.Floor(b.Min.Y / GridMetres); gy <= (int)MathF.Floor(b.Max.Y / GridMetres); gy++)
            {
                if (!cells.TryGetValue((gx, gy), out var list))
                    cells[(gx, gy)] = list = [];
                list.Add(i);
            }
        }

        _grid = cells.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    public static ZoneWater Empty { get; } = new([]);

    public int Bodies => _bodies.Length;

    /// <summary>The sea's surface (the world's ocean level) when the zone's sea counts as water (<c>StaticCollision.SeaWaterEnabled</c>); null otherwise.</summary>
    public float? OceanLevel { get; }

    /// <summary>One volume: its contour (continent XY), surface height and depth.</summary>
    public sealed record Body(Vector2[] Contour, float Surface, float Depth, Vector2 Min, Vector2 Max)
    {
        /// <summary>The volume's surface plane: its normal (the client's <c>FogPlanePos</c>).</summary>
        public Vector3 PlaneNormal { get; init; } = new(0f, 0f, 1f);

        /// <summary>The distance of the surface plane from the origin.</summary>
        public float PlaneDistance { get; init; }

        /// <summary>
        /// The surface height at (x, y): the volume's plane, when it has one (a river flows downhill), else the flat
        /// height the volume stores.
        /// </summary>
        public float SurfaceAt(float x, float y)
        {
            if (MathF.Abs(PlaneNormal.Z) < 1e-6f)
                return Surface;
            return -(PlaneNormal.X * x + PlaneNormal.Y * y + PlaneDistance) / PlaneNormal.Z;
        }
    }

    /// <summary>Builds from given bodies (tests).</summary>
    public static ZoneWater FromBodies(IEnumerable<(IReadOnlyList<Vector2> Contour, float Surface, float Depth)> bodies, float? oceanLevel = null) =>
        new(bodies.Where(b => b.Contour.Count >= 3).Select(b => Make([.. b.Contour], b.Surface, b.Depth)).ToArray(), oceanLevel);

    /// <summary>
    /// The water volumes of a cell's object file, placed in continent coordinates (main_world's files hold cell-local
    /// points; a point far outside the cell is taken as already continent, as the World's <c>WaterBodies</c> does).
    /// Volumes at the ocean level (within 1 m) are the sea, not inland water.
    /// </summary>
    public static IEnumerable<Body> FromCell(IReadOnlyList<WaterVolumeRecord> volumes, int cellX, int cellY, float oceanLevel)
    {
        var offset = new Vector2(cellX * (float)StaticGeometry.CellSize, cellY * (float)StaticGeometry.CellSize);
        foreach (var volume in volumes)
        {
            if (volume.PhysicsContourPoints.Count < 3 || volume.SurfaceHeight <= oceanLevel + 1f)
                continue;
            var contour = volume.PhysicsContourPoints.Select(p =>
            {
                var local = p.X >= -512f && p.Y >= -512f && p.X <= 2f * StaticGeometry.CellSize && p.Y <= 2f * StaticGeometry.CellSize;
                return local ? new Vector2(p.X, p.Y) + offset : new Vector2(p.X, p.Y);
            }).ToArray();
            yield return Make(contour, volume.SurfaceHeight, MathF.Max(0f, volume.Depth), Plane(volume, offset));
        }
    }

    /// <summary>A volume of kind 2 (an area: a lake or pond); the client's enum calls it River.</summary>
    public const int AreaKind = 2;

    /// <summary>A volume of kind 3 (a river: its two banks, the second reversed, with a flow along them).</summary>
    public const int RiverKind = 3;

    /// <summary>
    /// The volume's surface plane as the native engine physicalises it: the plane through the volume's own point
    /// (a river's end point, an area's first shape point), moved with the cell when the points are cell-local.
    /// </summary>
    private static (Vector3 Normal, float Distance) Plane(WaterVolumeRecord volume, Vector2 offset)
    {
        var n = volume.PlaneNormal;
        if (MathF.Abs(n.Z) < 1e-6f)
            return (n, volume.PlaneDistance);

        var kind = volume.VolumeType;
        float d;
        if (kind == RiverKind)
            d = -(n.X * volume.EndPos.X + n.Y * volume.EndPos.Y + n.Z * volume.EndPos.Z);
        else if (volume.ShapePoints.Count > 0)
            d = -n.Z * volume.ShapePoints[0].Z;
        else
            d = volume.PlaneDistance;

        // The points of a cell's file are cell-local: the plane moves with the cell.
        var local = volume.PhysicsContourPoints.Count > 0 && volume.PhysicsContourPoints.All(IsCellLocal);
        if (local)
            d -= n.X * offset.X + n.Y * offset.Y;

        return (n, d);
    }

    private static bool IsCellLocal(Vector3 p) => p.X >= -512f && p.Y >= -512f && p.X <= 2f * StaticGeometry.CellSize && p.Y <= 2f * StaticGeometry.CellSize;

    public static ZoneWater FromCells(IEnumerable<Body> bodies, float? oceanLevel = null) => new(bodies.ToArray(), oceanLevel);

    private static Body Make(Vector2[] contour, float surface, float depth, (Vector3 Normal, float Distance)? plane = null)
    {
        var min = new Vector2(contour.Min(p => p.X), contour.Min(p => p.Y));
        var max = new Vector2(contour.Max(p => p.X), contour.Max(p => p.Y));
        return new Body(contour, surface, depth, min, max)
        {
            PlaneNormal = plane?.Normal ?? new Vector3(0f, 0f, 1f),
            PlaneDistance = plane?.Distance ?? 0f
        };
    }

    /// <summary>
    /// The surface of the water a point at height <paramref name="z"/> is in: of the volumes whose contour holds (x, y)
    /// and whose surface is at or over <paramref name="z"/> (and not deeper than the volume plus 5 m under it), the
    /// lowest surface; under <see cref="OceanLevel"/>, the sea when no inland volume is lower. False when the point is in no water.
    /// </summary>
    public bool TrySurface(float x, float y, float z, out float surface)
    {
        surface = OceanLevel is { } ocean && z <= ocean ? ocean : float.PositiveInfinity;
        if (_bodies.Length == 0 || !_grid.TryGetValue(((int)MathF.Floor(x / GridMetres), (int)MathF.Floor(y / GridMetres)), out var candidates))
            return !float.IsPositiveInfinity(surface);
        var p = new Vector2(x, y);
        foreach (var i in candidates)
        {
            var b = _bodies[i];
            if (p.X < b.Min.X || p.X > b.Max.X || p.Y < b.Min.Y || p.Y > b.Max.Y)
                continue;
            // The surface of a volume follows its plane (a river runs downhill), so it is asked at the point.
            var bodySurface = b.SurfaceAt(x, y);
            if (z > bodySurface || z < bodySurface - b.Depth - 5f || bodySurface >= surface || !Inside(b.Contour, p))
                continue;
            surface = bodySurface;
        }

        return !float.IsPositiveInfinity(surface);
    }

    /// <remarks>
    /// Swim stance (<c>StaticCollision.SwimStanceEnabled</c>): a ground unit's record at least
    /// <see cref="SwimStanceMinDepthMetres"/> under a water surface is stance Swim when it is more than
    /// <see cref="SwimStanceOffBedMetres"/> off the floor or at least <see cref="SwimStanceBedDepthMetres"/> under the
    /// surface. Native, sea traces 172, 173, 245, 257, 281, 344 (2026-11-08): records in the water column 99 % Swim (172:
    /// 24 902 of 25 123; the rest falls, stance Combat, and Fly); on the bed by depth under the surface, 0.25–0.75 m: 437 of
    /// 739 Swim (mostly 344's small fish), 0.75–1.25 m: 162 of 429, 1.25 m and more: 971 of 993; within 0.3 m of the surface
    /// none (Relaxed and Combat). Zone 328's inland swimmers: every record Swim. The host sent them all Relaxed.
    /// </remarks>
    public const float SwimStanceMinDepthMetres = 0.3f;
    public const float SwimStanceOffBedMetres = 0.3f;
    public const float SwimStanceBedDepthMetres = 1.25f;

    /// <summary>The depth (metres per unit of scale) a unit put on the sea's surface swims under it, when its model has no row in <see cref="SurfaceDepthPerScale"/>.</summary>
    public const float DefaultSurfaceDepthPerScale = 0.95f;

    /// <summary>
    /// Surface swimmers' depth per model (metres under the surface per unit of the WZNpcState scale), fitted on every unit
    /// the World put on the sea's surface (ocean level − 0.3 to + 1 m) over water deeper than 2 m, from native's Swim records
    /// after the first, in the sea traces 172, 173, 245, 257, 281, 344 and 159 (2026-11-08; 159 and 281 have none):
    /// <list type="bullet">
    /// <item>404 seabug: 0.943–0.950 for scale 1.0 (template 8565, 241 units), 1.4 (8564, 82), 2.3 (8563, 21), and 2312 (1);</item>
    /// <item>347 crocodile_ironscale: 0.49–0.50 (257, template 2309, 3 units);</item>
    /// <item>1006 beholder_swamp: 0.707–0.753 at scale 3 (245, template 9417, 6 units: 2.12 m);</item>
    /// <item>2240: 0.71 (344, template 17795, 1 unit); 11 nuian_female: 1.70 (245, template 9462, 1 unit).</item>
    /// </list>
    /// </summary>
    public static readonly IReadOnlyDictionary<uint, float> SurfaceDepthPerScale = new Dictionary<uint, float>
    {
        [404] = 0.95f,
        [347] = 0.49f,
        [1006] = 0.71f,
        [2240] = 0.71f,
        [11] = 1.70f
    };

    /// <remarks>
    /// Small seabugs do not scale down: model 404 at scale 0.5 (template 8566, 164 units in 172 and 173) bobs between 98.5
    /// and 99.5 m (median 1.30 m under the surface, 2.6 per scale), where 0.95 × scale would put it at 0.48.
    /// </remarks>
    public const float SmallSeabugDepthMetres = 1.30f;

    /// <summary>
    /// A small seabug at rest floats up: after a leg native's records rise with a growing vertical velocity (to ~0.31 m/s)
    /// for ~3 s, 0.6 m over the swimming depth, then stand; the next leg (or an idle dive) takes it back down (172 bc 2447:
    /// 98.68 → 99.27 at 30.6–33.6 s, 98.67 → 99.25 at 38.2–41.2 s, legs at 98.5–98.7). The comparator sees the stands:
    /// per-unit stand medians 0.74 / 0.88 / 1.22 m under the 100 m surface (p10 / p50 / p90, 72 units, 172) and 0.73 /
    /// 0.78 / 1.30 (112 units, 173). A single resting depth of 0.85 m puts 60 of 72 and 88 of 112 units within 0.25 m of
    /// native's (the swimming depth 1.30 m: 19 and 26), median difference +0.03 / −0.06 m. The host stands it there and
    /// dives it back to <see cref="SmallSeabugDepthMetres"/> when a leg starts (native sinks over ~1.5 s). Kill switch
    /// <c>AAEMU_ZONE_SMALL_SEABUG_REST=0</c>.
    /// </summary>
    public const float SmallSeabugRestDepthMetres = 0.85f;

    public static bool SmallSeabugRestEnabled { get; } = Environment.GetEnvironmentVariable("AAEMU_ZONE_SMALL_SEABUG_REST") != "0";

    /// <summary>Model 404 under 0.75 scale: the seabug that does not scale its depth (<see cref="SmallSeabugDepthMetres"/>).</summary>
    public static bool IsSmallSeabug(uint modelId, float scale) => StaticCollision.SurfaceDepthByModelEnabled && modelId == 404 && scale is > 0f and < 0.75f;

    /// <summary>
    /// How deep under the sea's surface a unit the World put on it swims (metres): its model's depth per scale × the
    /// unit's scale (<see cref="SurfaceDepthPerScale"/>, else <see cref="DefaultSurfaceDepthPerScale"/>); with the kill switch
    /// <c>AAEMU_ZONE_SURFACE_DEPTH_BY_MODEL=0</c> the default for every model.
    /// </summary>
    public static float SurfaceSwimDepth(uint modelId, float scale)
    {
        if (scale <= 0f)
            scale = 1f;
        if (!StaticCollision.SurfaceDepthByModelEnabled)
            return DefaultSurfaceDepthPerScale * scale;
        if (modelId == 404 && scale < 0.75f)
            return SmallSeabugDepthMetres;
        return (SurfaceDepthPerScale.TryGetValue(modelId, out var perScale) ? perScale : DefaultSurfaceDepthPerScale) * scale;
    }

    /// <summary>
    /// The stance the zone's water puts a unit on <paramref name="floorZ"/> at (x, y, z), using the same thresholds
    /// as the native traces (see <see cref="StanceRules"/>). <see cref="WaterStance.Unchanged"/> when the point is
    /// not in water.
    /// </summary>
    public WaterStance ResolveStance(float x, float y, float z, float floorZ)
    {
        if (!StaticCollision.SwimStanceEnabled || !TrySurface(x, y, z, out var surface))
            return WaterStance.Unchanged;
        return StanceRules.Resolve(surface, z, floorZ);
    }

    /// <summary>
    /// The surface a swimmer floats at, and how deep it should sit under it, for a unit the world put on the water:
    /// the sea uses the per-model depth (<see cref="SurfaceSwimDepth"/>), inland water the fixed swim depth.
    /// </summary>
    public float SurfaceSwimZ(float x, float y, float z, uint modelId, float scale)
    {
        if (!TrySurface(x, y, z, out var surface))
            return z;
        var depth = OceanLevel is { } ocean && MathF.Abs(surface - ocean) < 0.01f
            ? SurfaceSwimDepth(modelId, scale)
            : StanceRules.MinDepthMetres;
        return surface - depth;
    }

    private static bool Inside(Vector2[] polygon, Vector2 p)
    {
        var inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }

        return inside;
    }
}
