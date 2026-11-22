using AAEmu.Geometry.Abstractions;
using System.Diagnostics;
using System.Numerics;


namespace AAEmu.Geometry;

/// <summary>
/// The static geometry of one zone (plan slice 6): every brush placed in the zone's cells with the default collision
/// proxy of its asset, as instances of the shared per-asset <see cref="CollisionMesh"/>es under a bounding-volume
/// hierarchy of their world boxes. Read-only once loaded, so every copy of a zone shares it (<see cref="ZoneDataShare"/>).
/// Instancing is the memory rule: zone 129 places 6 389 brushes of 512 assets — 165 k unique proxy triangles, 2.3 M
/// once placed. Continent coordinates throughout.
/// <para>
/// Sources per cell (<see cref="BrushPlacementFile"/>): <c>world_server/object_indoor.x2d</c> (where the client ships
/// none, main_world, the same indoor brushes from <c>client/visareas.dat</c>), then
/// <c>world_server/object.x2d</c> or, where the client ships no server file (main_world), <c>client/object.dat</c>.
/// Doodads, houses and vegetation are not static geometry here.
/// </para>
/// </summary>
public sealed class StaticCollision
{
    private static StaticCollisionOptions s_options = StaticCollisionOptions.FromEnvironment(Environment.GetEnvironmentVariable);

    /// <summary>The switches in force: set once at startup, else read from the environment on first use.</summary>
    public static StaticCollisionOptions Options
    {
        get => s_options;
        set => s_options = value;
    }

    private readonly Instance[] _instances;
    private readonly float[] _bounds;
    private readonly int[] _nodes;

    private StaticCollision(Instance[] instances, float[] bounds, int[] nodes)
    {
        _instances = instances;
        _bounds = bounds;
        _nodes = nodes;
    }

    public static StaticCollision Empty { get; } = new([], [], []);

    private const string VisAreasFile = "visareas.dat";

    /// <summary>
    /// Underground floors kill switch (<c>AAEMU_ZONE_UNDERGROUND_FLOORS=0</c>): main_world cells load no
    /// <c>client/visareas.dat</c> brushes (and <see cref="StaticCollision"/> callers keep the terrain as the lower bound
    /// of their floor search), as before. Read from <see cref="Options"/>.
    /// </summary>
    public static bool UndergroundFloorsEnabled => Options.UndergroundFloorsEnabled;

    private static bool VisAreaBrushesEnabled => UndergroundFloorsEnabled;

    /// <summary>
    /// Underground structure floors kill switch (<c>AAEMU_ZONE_UNDERGROUND_STRUCTURE=0</c>): a unit far under the heightmap
    /// with no brush under it takes the nav layer within the band before a structure floor, as before. Zone 246 (2026-11-08):
    /// the hall under the hill at (25800, 10775), template 12363, 17 units on a floor at 848.7–849.2 with no brush, nav
    /// 851–853 m and the terrain 851–857 m over it; the nav structure waypoints lie at 848.67–849.14 and native walks at
    /// 848.8–848.9 the whole trace. The host took the nav layer at 852.1 once it came within 3 m and from there the terrain
    /// (units 6129, 6012: median −5.7 / −5.1 m on native, +0.06 on the host). Read from <see cref="Options"/>.
    /// </summary>
    public static bool UndergroundStructureFloorsEnabled => Options.UndergroundStructureFloorsEnabled;

    /// <summary>Inland water kill switch (<c>AAEMU_ZONE_INLAND_WATER=0</c>): no water volumes are loaded, so swimmers are grounded as before. Read from <see cref="Options"/>.</summary>
    public static bool InlandWaterEnabled => Options.InlandWaterEnabled;

    /// <summary>Sea swimmers kill switch (<c>AAEMU_ZONE_SEA_SWIMMERS=0</c>): the sea under the world's ocean level is not water for units (they are grounded on the seabed, as before). Read from <see cref="Options"/>.</summary>
    public static bool SeaWaterEnabled => Options.SeaWaterEnabled;

    /// <summary>
    /// Inland fish kill switch (<c>AAEMU_ZONE_INLAND_FISH_SWIM=0</c>): a flier under an inland water surface keeps stance Fly
    /// (only the sea's turn Swim), as before. Zone 159 (2026-11-08): templates 3155 and 3156 (40 units) under the lake at
    /// 250.07 m are stance Relaxed, Fly, then Swim on native (1 847 Swim records, 30 Fly) like the sea fish; 7562 flying
    /// 25 m over dry ground stays Fly. Read from <see cref="Options"/>.
    /// </summary>
    public static bool InlandFishSwimEnabled => Options.InlandFishSwimEnabled;

    /// <summary>
    /// Swim stance kill switch (<c>AAEMU_ZONE_SWIM_STANCE=0</c>): a ground unit in the water keeps stance Relaxed, as before
    /// (see <see cref="ZoneWater.SwimStanceMinDepthMetres"/>). Read from <see cref="Options"/>.
    /// </summary>
    public static bool SwimStanceEnabled => Options.SwimStanceEnabled;

    /// <summary>
    /// Settle kill switch (<c>AAEMU_ZONE_SETTLE_NO_RISE=0</c>): a spawn settle may climb onto the nav layer over the unit, as
    /// before. Zone 159 (2026-11-08), template 3150 at (22855, 11590): the World put the units at 263.15 / 259.16, the nav
    /// lies at 265.5 / 260.9 with no brush, the terrain at 259.2 / 254.4; native fell to the terrain (medians +0.08 / +0.04
    /// over it), the host climbed onto the nav and walked it 4–6 m over the terrain the whole trace (5268, 5397, 5398, 5405).
    /// Zone 257's hall units 6581 / 6850 (World 125.1 / 125.4, native 122.1 / 118.6) climbed to 126.1 the same way. Now the
    /// settle takes the highest brush or terrain under the start, else holds the start. Read from <see cref="Options"/>.
    /// </summary>
    public static bool SettleNoRiseEnabled => Options.SettleNoRiseEnabled;


    /// <summary>
    /// Per-model surface depth kill switch (<c>AAEMU_ZONE_SURFACE_DEPTH_BY_MODEL=0</c>): every unit the World put on the
    /// sea's surface swims <c>MovementRules.SeaSurfaceSwimDepthMetres</c> × scale under it, as before
    /// (see <see cref="ZoneWater.SurfaceSwimDepth"/>). Read from <see cref="Options"/>.
    /// </summary>
    public static bool SurfaceDepthByModelEnabled => Options.SurfaceDepthByModelEnabled;

    /// <summary>The zone's inland water volumes (lakes, ponds, river pools) from the same outdoor object files; empty when not loaded.</summary>
    public ZoneWater Water { get; init; } = ZoneWater.Empty;

    public int Placements { get; private init; }
    public int Instances => _instances.Length;
    public int Assets { get; private init; }
    public int AssetsWithoutCollision { get; private init; }
    public long UniqueTriangles { get; private init; }
    public long PlacedTriangles { get; private init; }
    /// <summary>This zone's own cost: instances and their hierarchy.</summary>
    public long Bytes => _instances.Length * 160L + 4L * (_bounds.Length + _nodes.Length);
    /// <summary>The meshes it uses (shared with any other zone using the same assets).</summary>
    public long MeshBytes { get; private init; }
    public TimeSpan LoadTime { get; private init; }

    public override string ToString() =>
        $"{Instances} brushes of {Assets - AssetsWithoutCollision} assets ({AssetsWithoutCollision} without collision, {Placements - Instances} placements dropped), " +
        $"{UniqueTriangles} unique / {PlacedTriangles} placed triangles, zone {Bytes / (1024.0 * 1024):F2} MB + meshes {MeshBytes / (1024.0 * 1024):F2} MB, {Water.Bodies} water volumes, {LoadTime.TotalMilliseconds:F0} ms";

    /// <summary>
    /// Builds the static geometry of a set of cells (one zone): every brush placed in them with the default collision
    /// proxy of its asset, as instances of the shared per-asset <see cref="CollisionMesh"/>es under a bounding-volume
    /// hierarchy of their world boxes. Read-only once built. Continent coordinates throughout.
    /// <para>
    /// Sources per cell (<see cref="BrushPlacementFile"/>): <c>world_server/object_indoor.x2d</c> (where the client ships
    /// none, main_world, the same indoor brushes from <c>client/visareas.dat</c>), then
    /// <c>world_server/object.x2d</c> or, where the client ships no server file (main_world), <c>client/object.dat</c>.
    /// Doodads and houses are not static geometry here; the colliding vegetation is, when
    /// <paramref name="includeVegetation"/> is set (native's dedicate gives it collision too).
    /// </para>
    /// </summary>
    public static StaticCollision Load(IGeometryFileSource source, IReadOnlyList<StaticCollisionCell> cells, CollisionAssetCache? cache = null, bool includeVegetation = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(cells);
        cache ??= CollisionAssetCache.Shared;
        var watch = Stopwatch.StartNew();
        var placements = 0;
        var instances = new List<Instance>();
        var meshes = new Dictionary<string, CollisionMesh?>(StringComparer.OrdinalIgnoreCase);
        var water = new List<ZoneWater.Body>();
        var vegetation = 0;

        foreach (var cell in cells)
        {
            var server = $"{cell.CellFolder}/world_server";
            var indoor = $"{server}/object_indoor.x2d";
            // Without the server's indoor file (main_world), the indoor brushes come from the client's vis areas.
            var files = new List<string> { indoor };
            if (VisAreaBrushesEnabled && source.TryReadBytes(indoor) == null)
                files[0] = $"{cell.CellFolder}/client/{VisAreasFile}";
            // The outdoor objects: the server's own file where the client ships one, else the client's object.dat.
            var outdoor = $"{server}/object.x2d";
            files.Add(source.TryReadBytes(outdoor) == null ? $"{cell.CellFolder}/client/object.dat" : outdoor);

            var toContinent = Matrix4x4.CreateTranslation(cell.CellOffset.X, cell.CellOffset.Y, 0f);
            foreach (var file in files)
            {
                IReadOnlyList<BrushPlacementFile.Brush> brushes;
                byte[]? bytes = null;
                try
                {
                    bytes = source.TryReadBytes(file);
                    if (bytes == null)
                        continue;
                    brushes = file.EndsWith(VisAreasFile, StringComparison.OrdinalIgnoreCase) ? BrushPlacementFile.ReadVisAreas(bytes) : BrushPlacementFile.Read(bytes);
                    // The outdoor object file also holds the cell's water volumes.
                    if (InlandWaterEnabled && file == files[^1])
                        water.AddRange(ZoneWater.FromCell(BrushPlacementFile.ReadWater(bytes), cell.CellX, cell.CellY, cell.OceanLevel));
                }
                catch (Exception ex)
                {
                    GeometryLog.Warn($"cell {cell.CellFolder}: brush file {file} unreadable", ex);
                    continue;
                }

                foreach (var brush in brushes)
                {
                    placements++;
                    if (!meshes.TryGetValue(brush.AssetPath, out var mesh))
                        meshes[brush.AssetPath] = mesh = cache.Get(source, brush.AssetPath);
                    if (mesh == null)
                        continue;
                    var toWorld = brush.ToCell * toContinent;
                    if (!Matrix4x4.Invert(toWorld, out var toLocal))
                        continue;
                    instances.Add(new Instance(mesh, toWorld, toLocal, WorldBox(mesh, toWorld)));
                }
            }

            // The colliding vegetation of the cell (native's dedicate gives it collision too): the same index, so a
            // ship or a walker sees the trees as well as the walls.
            if (includeVegetation && Options.VegetationEnabled)
            {
                foreach (var (mesh, toWorld) in VegetationCollision.Instances(source, cells[0].WorldName, cell.CellX, cell.CellY, cache))
                {
                    if (!Matrix4x4.Invert(toWorld, out var toLocal))
                        continue;
                    instances.Add(new Instance(mesh, toWorld, toLocal, WorldBox(mesh, toWorld)));
                    vegetation++;
                }
            }
        }

        var (bounds, nodes, ordered) = BuildHierarchy(instances);
        var assetsWithoutCollision = 0;
        long uniqueTriangles = 0;
        long meshBytes = 0;
        foreach (var mesh in meshes.Values)
        {
            if (mesh == null)
            {
                assetsWithoutCollision++;
                continue;
            }
            uniqueTriangles += mesh.TriangleCount;
            meshBytes += mesh.Bytes;
        }
        long placedTriangles = 0;
        foreach (var instance in instances)
            placedTriangles += instance.Mesh.TriangleCount;
        var oceanLevel = cells.Count > 0 ? cells[0].OceanLevel : 0f;
        var collision = new StaticCollision(ordered, bounds, nodes)
        {
            Placements = placements + vegetation,
            Assets = meshes.Count,
            AssetsWithoutCollision = assetsWithoutCollision,
            UniqueTriangles = uniqueTriangles,
            PlacedTriangles = placedTriangles,
            MeshBytes = meshBytes,
            Water = ZoneWater.FromCells(water, SeaWaterEnabled && oceanLevel > 0f ? oceanLevel : null),
            LoadTime = watch.Elapsed
        };
        var cellFolders = new string[cells.Count];
        for (var i = 0; i < cells.Count; i++)
            cellFolders[i] = cells[i].CellFolder;
        GeometryLog.Info($"cells {string.Join(", ", cellFolders)}: static collision {collision}");
        return collision;
    }

    /// <summary>A collision built from given placements (tests).</summary>
    /// <summary>A collision built from given placements (vegetation, doodads — anything placed by hand).</summary>
    public static StaticCollision FromInstances(IEnumerable<(CollisionMesh Mesh, Matrix4x4 ToWorld)> placed, ZoneWater? water = null)
    {
        var instances = new List<Instance>();
        foreach (var (mesh, toWorld) in placed)
        {
            if (Matrix4x4.Invert(toWorld, out var toLocal))
                instances.Add(new Instance(mesh, toWorld, toLocal, WorldBox(mesh, toWorld)));
        }

        var (bounds, nodes, ordered) = BuildHierarchy(instances);
        return new StaticCollision(ordered, bounds, nodes) { Placements = instances.Count, Water = water ?? ZoneWater.Empty };
    }

    /// <summary>One brush instance as physics input: its world centre and its half-sizes, in the game frame (Z up).</summary>
    public readonly record struct StaticShape(Vector3 Origin, Vector3 HalfExtents);

    /// <summary>
    /// The zone's brushes as box inputs for a physics engine. Every instance is one axis-aligned box: that is the
    /// fidelity the native dedicate uses (it physicalises the brush's own box, not its trimesh), and it keeps the
    /// body count equal to the placement count.
    /// </summary>
    public IEnumerable<StaticShape> StaticShapes()
    {
        foreach (var instance in _instances)
            yield return ShapeOf(instance);
    }

    /// <summary>
    /// The brushes whose footprint on the ground plane falls into a square around a point: <see cref="StaticShapes"/>
    /// with a cheap bounds test in front. A host that physicalises a zone pays memory and tick time per body, and a
    /// ship only ever touches the brushes of the water it sails; the rest of the zone is answered by the BVH queries.
    /// </summary>
    /// <param name="centerX">Footprint centre X in the game frame (Z up).</param>
    /// <param name="centerY">Footprint centre Y in the game frame (Z up).</param>
    /// <param name="radius">Half-size of the square, in metres; measured from the box edges, not centres.</param>
    public IEnumerable<StaticShape> StaticShapesWithin(float centerX, float centerY, float radius)
    {
        foreach (var (_, shape) in StaticShapeInstancesWithin(centerX, centerY, radius))
            yield return shape;
    }

    /// <summary>
    /// Shapes with stable placement identities local to this collision index. A host selecting overlapping cell
    /// footprints can suppress the same placement while retaining distinct placements with identical boxes.
    /// </summary>
    public IEnumerable<(int InstanceId, StaticShape Shape)> StaticShapeInstancesWithin(float centerX, float centerY, float radius)
    {
        var minimumX = centerX - radius;
        var maximumX = centerX + radius;
        var minimumY = centerY - radius;
        var maximumY = centerY + radius;
        for (var i = 0; i < _instances.Length; i++)
        {
            var instance = _instances[i];
            var box = instance.Box;
            // WorldBox transforms directly into the game's Z-up frame: the ground plane is X/Y.
            if (box.Max.X < minimumX || box.Min.X > maximumX || box.Max.Y < minimumY || box.Min.Y > maximumY)
                continue;
            yield return (i, ShapeOf(instance));
        }
    }

    private static StaticShape ShapeOf(Instance instance)
    {
        var box = instance.Box;
        var size = box.Max - box.Min;
        // Flat brushes retain a small thickness so every returned box remains usable by a physics host.
        const float MinHalfExtent = 0.01f;
        var half = new Vector3(
            MathF.Max(size.X / 2f, MinHalfExtent),
            MathF.Max(size.Y / 2f, MinHalfExtent),
            MathF.Max(size.Z / 2f, MinHalfExtent));
        return new StaticShape((box.Min + box.Max) / 2f, half);
    }

    /// <summary>
    /// Nearest hit of the segment <paramref name="from"/> → <paramref name="to"/>: the fraction along it (0..1) and the
    /// world normal of the triangle hit (unnormalised). <paramref name="minUpNormal"/> &gt; 0 ignores triangles whose
    /// normal is steeper than that (|n.z| / |n| below it: walls, for floor queries).
    /// </summary>
    public bool Raycast(Vector3 from, Vector3 to, out float fraction, out Vector3 normal, float minUpNormal = 0f)
    {
        fraction = 1f;
        normal = default;
        if (_nodes.Length == 0)
            return false;
        var direction = to - from;
        var inv = new Vector3(1f / direction.X, 1f / direction.Y, 1f / direction.Z);
        Span<int> stack = stackalloc int[64];
        var top = 0;
        stack[top++] = 0;
        var best = 1f;
        var hit = false;
        while (top > 0)
        {
            var node = stack[--top];
            if (!CollisionMesh.SlabHit(_bounds, node, from, inv, best))
                continue;
            var first = _nodes[2 * node];
            var count = _nodes[2 * node + 1];
            if (count == 0)
            {
                if (top + 2 <= stack.Length)
                {
                    stack[top++] = first;
                    stack[top++] = first + 1;
                }

                continue;
            }

            for (var i = first; i < first + count; i++)
            {
                var instance = _instances[i];
                var localFrom = Vector3.Transform(from, instance.ToLocal);
                var localDirection = Vector3.TransformNormal(direction, instance.ToLocal);
                var t = best;
                if (instance.Mesh.Raycast(localFrom, localDirection, ref t, out var localNormal, minUpNormal, instance.ToWorld) && t < best)
                {
                    best = t;
                    // Normals go back through the inverse transpose; for floor checks the sign does not matter.
                    normal = Vector3.TransformNormal(localNormal, Matrix4x4.Transpose(instance.ToLocal));
                    hit = true;
                }
            }
        }

        fraction = best;
        return hit;
    }

    /// <summary>
    /// Whether any brush's world box overlaps the box <paramref name="min"/>–<paramref name="max"/> (no triangle test): the
    /// walkers' cheap "nothing near" check before their wall rays (<c>Ai.WalkerCollision</c>, agent M5).
    /// </summary>
    public bool AnyBoxOverlaps(Vector3 min, Vector3 max)
    {
        if (_nodes.Length == 0)
            return false;
        Span<int> stack = stackalloc int[64];
        var top = 0;
        stack[top++] = 0;
        while (top > 0)
        {
            var node = stack[--top];
            var b = 6 * node;
            if (_bounds[b] > max.X || _bounds[b + 3] < min.X || _bounds[b + 1] > max.Y || _bounds[b + 4] < min.Y || _bounds[b + 2] > max.Z || _bounds[b + 5] < min.Z)
                continue;
            var first = _nodes[2 * node];
            var count = _nodes[2 * node + 1];
            if (count == 0)
            {
                if (top + 2 > stack.Length)
                    return true; // too deep to tell: say yes, the rays decide
                stack[top++] = first;
                stack[top++] = first + 1;
                continue;
            }

            for (var i = first; i < first + count; i++)
            {
                var box = _instances[i].Box;
                if (box.Min.X <= max.X && box.Max.X >= min.X && box.Min.Y <= max.Y && box.Max.Y >= min.Y && box.Min.Z <= max.Z && box.Max.Z >= min.Z)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The highest static surface under (x, y) between <paramref name="above"/> and <paramref name="below"/> (a downward
    /// segment: the first floor it meets), ignoring walls. False when there is none.
    /// </summary>
    public bool TryFloor(float x, float y, float above, float below, out float z)
    {
        z = below;
        if (above <= below || !Raycast(new Vector3(x, y, above), new Vector3(x, y, below), out var fraction, out _, FloorMinUpNormal))
            return false;
        z = above + (below - above) * fraction;
        return true;
    }

    /// <summary>A floor faces up by at least this (|n.z| / |n|; 0.5 = 60° slope): steeper triangles are walls.</summary>
    public const float FloorMinUpNormal = 0.5f;

    private static (Vector3 Min, Vector3 Max) WorldBox(CollisionMesh mesh, Matrix4x4 toWorld)
    {
        var box = new GeometryBounds(mesh.Min, mesh.Max).Transform(toWorld);
        return (box.Min, box.Max);
    }

    private static (float[] Bounds, int[] Nodes, Instance[] Ordered) BuildHierarchy(List<Instance> instances)
    {
        if (instances.Count == 0)
            return ([], [], []);
        var order = new int[instances.Count];
        var boxes = new (Vector3 Min, Vector3 Max)[instances.Count];
        var centroids = new Vector3[instances.Count];
        for (var i = 0; i < instances.Count; i++)
        {
            order[i] = i;
            boxes[i] = instances[i].Box;
            centroids[i] = (boxes[i].Min + boxes[i].Max) / 2f;
        }
        var bounds = new List<float>(instances.Count * 3);
        var nodes = new List<int>(instances.Count);
        bounds.AddRange(new float[6]);
        nodes.AddRange([0, 0]);
        CollisionMesh.BuildInto(0, order, 0, instances.Count, boxes, centroids, bounds, nodes);
        var ordered = new Instance[instances.Count];
        for (var i = 0; i < order.Length; i++)
            ordered[i] = instances[order[i]];
        return ([.. bounds], [.. nodes], ordered);
    }

    private sealed record Instance(CollisionMesh Mesh, Matrix4x4 ToWorld, Matrix4x4 ToLocal, (Vector3 Min, Vector3 Max) Box);
}
