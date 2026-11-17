using System.Globalization;
using System.Diagnostics;
using System.Xml;

using AAEmu.Commons.Utils.AAPak;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Xml;
using AAEmu.Geometry;
using AAEmu.Geometry.Abstractions;
using AAEmu.Geometry.Source;

namespace AAEmu.GeometryProbe;

/// <summary>
/// Offline probe of the static-geometry layer (AAEmu.Game/Physics/Static) against real client data.
/// Reads either a <c>game_pak</c> through <see cref="AAPak"/> or an unpacked <c>game</c> folder, and prints what
/// the readers found, so a report claim can be checked against the shipped files without starting the server.
/// <para>
/// Usage: <c>dotnet run --project tools/GeometryProbe -- [--game &lt;dir&gt; | --pak &lt;file&gt;] [command ...]</c>.
/// </para>
/// </summary>
internal static class Program
{
    private const string DefaultPak = @"e:\games\AAClassic\Live-3500_30.10.2026\game_pak";
    private const string DefaultGame = @"e:\games\AAClassic\Live-3500_30.10.2026\game";
    private const string DefaultWorld = "main_world";

    private static int Main(string[] args)
    {
        string? pakPath = null;
        string? gamePath = null;
        string? world = null;
        uint? zone = null;
        string? cell = null;
        var cgfAssets = new List<string>();
        var abAssets = new List<string>();
        string? abCell = null;
        string? stairCell = null;
        string? findCell = null;
        string? physicsCost = null;
        float? floorX = null;
        float? floorY = null;
        var abLimit = 30;
        var listPattern = "game/objects";
        var listLimit = 20;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pak" when i + 1 < args.Length: pakPath = args[++i]; break;
                case "--game" when i + 1 < args.Length: gamePath = args[++i]; break;
                case "--world" when i + 1 < args.Length: world = args[++i]; break;
                case "--zone" when i + 1 < args.Length: zone = uint.Parse(args[++i]); break;
                case "--cell" when i + 1 < args.Length: cell = args[++i]; break;
                case "--cgf" when i + 1 < args.Length: cgfAssets.Add(args[++i]); break;
                case "--cgf-ab" when i + 1 < args.Length: abAssets.Add(args[++i]); break;
                case "--ab-cell" when i + 1 < args.Length: abCell = args[++i]; break;
                case "--stair-cell" when i + 1 < args.Length: stairCell = args[++i]; break;
                case "--find-cell" when i + 1 < args.Length: findCell = args[++i]; break;
                case "--physics-cost" when i + 1 < args.Length: physicsCost = args[++i]; break;
                case "--floor" when i + 2 < args.Length: floorX = float.Parse(args[++i], CultureInfo.InvariantCulture); floorY = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--ab-limit" when i + 1 < args.Length: abLimit = int.Parse(args[++i]); break;
                case "--list" when i + 1 < args.Length: listPattern = args[++i]; break;
                case "--list-limit" when i + 1 < args.Length: listLimit = int.Parse(args[++i]); break;
                case "--help" or "-h":
                    Console.WriteLine(Help);
                    return 0;
            }
        }

        // A directory wins over the pak: it is faster and shows the shipped bytes without an archive layer.
        var source = gamePath != null || pakPath == null
            ? FromDirectory(gamePath ?? DefaultGame)
            : FromPak(pakPath);
        if (source == null)
            return 1;

        try
        {
            Console.WriteLine($"source: {source}");
            world ??= DefaultWorld;

            var ok = true;
            if (physicsCost != null && zone is { } costZone)
            {
                ProbePhysicsCost(source, world, costZone, physicsCost);
            }
            else if (stairCell != null && zone is { } stairZone)
            {
                ProbeStairProfile(source, world, stairZone, stairCell);
            }
            else if (floorX is { } fx && floorY is { } fy && zone is { } floorZone)
            {
                ProbeFloor(source, world, floorZone, fx, fy);
            }
            else if (abCell != null)
            {
                ProbeAbCell(source, world, abCell, abLimit);
            }
            else if (abAssets.Count > 0)
            {
                ProbeAb(source, abAssets);
            }
            else if (cgfAssets.Count > 0)
            {
                foreach (var asset in cgfAssets)
                    ok &= ProbeCgf(source, asset);
            }
            else if (findCell != null)
            {
                FindZoneOfCell(source, world, findCell);
            }
            else
            {
                ProbeWorldXml(source, world);
                if (zone is { } zoneKey)
                    ProbeZoneCells(source, world, zoneKey);
                if (cell is { } cellKey)
                    ProbeCellFiles(source, world, cellKey);
                ProbeList(source, listPattern, listLimit);
            }

            return ok ? 0 : 2;
        }
        finally
        {
            source.Dispose();
        }
    }

    private const string Help = """
        GeometryProbe вЂ” reads the static-geometry layer against real client files.

          --game <dir>        unpacked game folder (default: the local 3.5 client, used when no --pak is given)
          --pak <path>        game_pak to read instead of a folder
          --world <name>      world to read, default main_world
          --zone <key>        zone key from world.xml, prints its cells and static-geometry counters
          --cell <XXX_YYY>    read one cell's object.dat / visareas.dat directly and print the counts
          --cgf <path>        probe one .cgf/.cga proxy (repeatable); skips the world report
          --cgf-ab <path>     compare an asset's collision proxy with its render mesh (repeatable)
          --ab-cell <X_Y>     compare every asset that cell's object.dat places (--ab-limit, default 30)
          --floor <x> <y>     raycast the floor under a world position of --zone (plan P2: accuracy and timing)
          --stair-cell <X_Y>  plan P2: walk a cell's brushes and report the floor step profile
          --find-cell <X_Y>   which world and zone key owns a cell
          --physics-cost <X_Y>  plan P5: cost of creating one static body per brush of that cell's zone
          --list <dir>        list files under a folder (default game/objects)
          --list-limit <n>    how many names to print (default 20)
        """;

    private static ClientFiles? FromDirectory(string root)
    {
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"game folder not found: {root}");
            return null;
        }

        // Accept both "вЂ¦\game" and the install root that holds it.
        var nested = Path.Combine(root, "game");
        if (!Directory.Exists(Path.Combine(root, "worlds")) && Directory.Exists(Path.Combine(nested, "worlds")))
            root = nested;
        return new ClientFiles(root);
    }

    private static ClientFiles? FromPak(string pakPath)
    {
        if (!File.Exists(pakPath))
        {
            Console.Error.WriteLine($"game_pak not found: {pakPath}");
            return null;
        }

        var pak = new AAPak(pakPath, true);
        if (!pak.isOpen)
        {
            Console.Error.WriteLine($"cannot open {pakPath}");
            return null;
        }

        return new ClientFiles(pak);
    }

    /// <summary>
    /// Plan P1: the collision proxy against the render mesh of the same asset — triangle counts, extents and how
    /// much of the render surface the proxy actually covers.
    /// </summary>
    private static void ProbeAb(ClientFiles source, List<string> assets)
    {
        Console.WriteLine("asset | proxy tris | render tris | ratio | proxy bbox | render bbox | proxy boxes/tris/rounds");
        Console.WriteLine("---|---|---|---|---|---|---");
        foreach (var asset in assets)
        {
            var data = source.Read(asset);
            var name = asset.StartsWith("game/objects/", StringComparison.OrdinalIgnoreCase) ? asset[13..] : asset;
            if (data == null)
            {
                Console.WriteLine($"{name} | MISSING");
                continue;
            }

            var proxy = CgfPhysicsReader.Read(data, out var proxyCounts);
            var render = CgfRenderReader.Read(data, out var renderCounts);
            var proxyTris = proxy?.TriangleCount ?? 0;
            var renderTris = render?.TriangleCount ?? 0;
            var ratio = renderTris > 0 ? (proxyTris / (double)renderTris).ToString("P0") : "-";
            Console.WriteLine($"{name} | {proxyTris} | {renderTris} | {ratio} | {Box(proxy)} | {Box(render)} | " +
                              $"{proxyCounts.Boxes}/{proxyCounts.Meshes}/{proxyCounts.Rounds}, unreadable {proxyCounts.Unreadable}");
        }
    }

    private static string Box(CollisionMesh? mesh)
    {
        if (mesh == null)
            return "none";
        var size = mesh.Max - mesh.Min;
        return $"{size.X:F2}x{size.Y:F2}x{size.Z:F2}";
    }

    private static bool ProbeCgf(ClientFiles source, string asset)
    {
        var data = source.Read(asset);
        if (data == null)
        {
            Console.WriteLine($"cgf {asset}: MISSING");
            return false;
        }

        var watch = Stopwatch.StartNew();
        var mesh = CgfPhysicsReader.Read(data, out var counts);
        watch.Stop();
        var version = data.Length >= 16 ? BitConverter.ToUInt32(data, 12) : 0u;
        Console.WriteLine($"cgf {asset}: {data.Length} B, version 0x{version:X}, " +
                          $"box {counts.Boxes}, trimesh {counts.Meshes}, round {counts.Rounds}, " +
                          $"lod-skipped {counts.SkippedLods}, unreadable {counts.Unreadable}, " +
                          $"{mesh?.TriangleCount ?? 0} triangles, {watch.Elapsed.TotalMilliseconds:F1} ms");
        return mesh != null || counts.Boxes + counts.Meshes + counts.Rounds > 0;
    }

    private static void ProbeWorldXml(ClientFiles source, string world)
    {
        var (xml, template) = ReadWorld(source, world);
        if (xml == null || template == null)
            return;

        var keys = xml.Zones.Keys.OrderBy(k => k).ToList();
        Console.WriteLine($"world.xml {world}: zones {keys.FirstOrDefault()}..{keys.LastOrDefault()} ({xml.Zones.Count}), " +
                          $"cells {template.CellX}x{template.CellY}, ocean {xml.OceanLevel}");
        foreach (var key in keys.Take(5))
            PrintZone(xml, key);
        if (keys.Count > 5)
            Console.WriteLine("  ...");
    }

    private static (XmlWorld? Xml, WorldTemplate? Template) ReadWorld(ClientFiles source, string world)
    {
        var data = source.Read($"game/worlds/{world}/world.xml");
        if (data == null)
        {
            Console.WriteLine($"world.xml: MISSING game/worlds/{world}/world.xml");
            return (null, null);
        }

        using var stream = new MemoryStream(data);
        var doc = new XmlDocument();
        doc.Load(stream);
        var template = new WorldTemplate { Name = world };
        var xml = new XmlWorld();
        xml.ReadNode(doc.DocumentElement!, template);
        return (xml, template);
    }

    private static void ProbeZoneCells(ClientFiles source, string world, uint zoneKey)
    {
        var (xml, template) = ReadWorld(source, world);
        if (xml == null || template == null)
            return;
        PrintZone(xml, zoneKey);
        if (!template.XmlWorldZones.TryGetValue(zoneKey, out var zone))
            return;

        var cells = zone.Cells.Keys.Select(c => new StaticCollisionCell(world, c.Item1, c.Item2, xml.OceanLevel)).ToList();
        var watch = Stopwatch.StartNew();
        var collision = StaticCollision.Load(source, cells);
        watch.Stop();
        Console.WriteLine($"  static geometry: {collision}");
        Console.WriteLine($"  built in {watch.ElapsedMilliseconds} ms, {collision.Bytes / (1024.0 * 1024):F2} MB zone + " +
                          $"{collision.MeshBytes / (1024.0 * 1024):F2} MB meshes");
    }

    /// <summary>
    /// Plan P1 in bulk: every asset a cell's object.dat places, proxy against render.
    /// </summary>
    private static void ProbeAbCell(ClientFiles source, string world, string cell, int limit)
    {
        var path = $"game/worlds/{world}/cells/{cell}/client/object.dat";
        var data = source.Read(path);
        if (data == null)
        {
            Console.WriteLine($"ab-cell {path}: MISSING");
            return;
        }

        var brushes = BrushPlacementFile.Read(data);
        var assets = brushes.Select(b => b.AssetPath)
            .Where(a => a.EndsWith(".cgf", StringComparison.OrdinalIgnoreCase) || a.EndsWith(".cga", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
        Console.WriteLine($"ab-cell {world}/{cell}: {brushes.Count} brushes, {assets.Count} assets of " +
                          $"{brushes.Select(b => b.AssetPath).Distinct(StringComparer.OrdinalIgnoreCase).Count()}");
        ProbeAb(source, assets);
    }

    /// <summary>The world and zone key that own a cell.</summary>
    private static void FindZoneOfCell(ClientFiles source, string world, string cellName)
    {
        var parts = cellName.Split('_');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var cellX) || !int.TryParse(parts[1], out var cellY))
        {
            Console.WriteLine($"find-cell: bad cell name {cellName}");
            return;
        }

        foreach (var candidate in new[] { world, "main_world", "instance_sal_temple_hard" })
        {
            var (xml, template) = ReadWorldQuiet(source, candidate);
            if (xml == null || template == null)
                continue;
            var owners = template.XmlWorldZones.Where(kv => kv.Value.Cells.ContainsKey((cellX, cellY))).Select(kv => kv.Key).ToList();
            Console.WriteLine($"find-cell {candidate}/{cellName}: zones [{string.Join(", ", owners)}]");
            if (owners.Count > 0)
                return;
        }
    }

    private static (XmlWorld? Xml, WorldTemplate? Template) ReadWorldQuiet(ClientFiles source, string world)
    {
        var data = source.Read($"game/worlds/{world}/world.xml");
        if (data == null)
            return (null, null);
        using var stream = new MemoryStream(data);
        var doc = new System.Xml.XmlDocument();
        doc.Load(stream);
        var template = new WorldTemplate { Name = world };
        var xml = new XmlWorld();
        xml.ReadNode(doc.DocumentElement!, template);
        return (xml, template);
    }

    /// <summary>
    /// Plan P2, the acceptance criterion: walk a zone's brush geometry along a line and report the floor profile, so
    /// the steps of a staircase are visible as a sequence of heights rather than one number.
    /// </summary>
    private static void ProbeStairProfile(ClientFiles source, string world, uint zoneKey, string cellName)
    {
        var (xml, template) = ReadWorld(source, world);
        if (xml == null || template == null)
            return;
        if (!template.XmlWorldZones.TryGetValue(zoneKey, out var zone))
        {
            Console.WriteLine($"stairs: zone {zoneKey} absent");
            return;
        }

        var cells = zone.Cells.Keys.Select(c => new StaticCollisionCell(world, c.Item1, c.Item2, xml.OceanLevel)).ToList();
        var watch = Stopwatch.StartNew();
        var collision = StaticCollision.Load(source, cells);
        Console.WriteLine($"stairs: zone {zoneKey} in {watch.ElapsedMilliseconds} ms, {collision}");

        // The P2 criterion is about following a surface, not about a point: walk one line across the cell and report
        // the floor under every 0.25 m, so stairs show as a chain of distinct heights.
        var walkLine = DescribeFloorLine(collision, cellName, step: 0.25f);

        // A flat snapshot of one cell's floor: for each 2 m node along X at a few Y values, the floor height.
        var parts = cellName.Split('_');
        var cellX = int.Parse(parts[0]);
        var cellY = int.Parse(parts[1]);
        var x0 = cellX * StaticGeometry.CellSize;
        var y0 = cellY * StaticGeometry.CellSize;
        var terrain = new TerrainReader(source.Read);

        var floors = 0;
        var above = 0f;
        var sample = 0;
        var worstAboveTerrain = 0f;
        var highest = float.MinValue;
        var lowest = float.MaxValue;
        for (var dx = 0; dx < StaticGeometry.CellSize; dx += 8)
        {
            for (var dy = 0; dy < StaticGeometry.CellSize; dy += 8)
            {
                var x = x0 + dx;
                var y = y0 + dy;
                above = terrain.Height(x, y) is { } t ? t + 80f : 300f;
                if (!collision.TryFloor(x, y, above, -100f, out var z))
                    continue;
                floors++;
                highest = MathF.Max(highest, z);
                lowest = MathF.Min(lowest, z);
                if (terrain.Height(x, y) is { } ground)
                    worstAboveTerrain = MathF.Max(worstAboveTerrain, MathF.Abs(z - ground));
                sample++;
            }
        }

        Console.WriteLine($"cell {cellName}: {sample} sampled points have a static floor, " +
                          $"z {lowest:F2}..{highest:F2}, worst |floor - terrain| {worstAboveTerrain:F3} m");
        Console.WriteLine(walkLine);

        _ = floors;
        _ = above;
    }

    /// <summary>
    /// <summary>
    /// Plan P5 cost check, without a game world: how long it takes to create one static body per brush, and what a
    /// fixed 25 Hz step costs with those bodies present. The server run is still needed for real ships, but this
    /// answers the question that blocks the deploy: does the body count fit the tick budget.
    /// </summary>
    private static void ProbePhysicsCost(ClientFiles source, string world, uint zoneKey, string cellName)
    {
        //var (xml, template) = ReadWorld(source, world);
        //if (xml == null || template == null)
        //    return;
        //if (!template.XmlWorldZones.TryGetValue(zoneKey, out var zone))
        //{
        //    Console.WriteLine($"physics-cost: zone {zoneKey} absent");
        //    return;
        //}

        //var cells = zone.Cells.Keys.Select(c => new StaticCollisionCell(world, c.Item1, c.Item2, xml.OceanLevel)).ToList();
        //var build = Stopwatch.StartNew();
        //var collision = StaticCollision.Load(source, cells);
        //build.Stop();
        //var shapes = collision.StaticShapes().ToList();
        //Console.WriteLine($"physics-cost: {collision.Instances} brushes, geometry built in {build.ElapsedMilliseconds} ms");

        //var physWorld = new Jitter2.World();
        //var create = Stopwatch.StartNew();
        //var bodies = new List<Jitter2.Dynamics.RigidBody>(shapes.Count);
        //foreach (var shape in shapes)
        //{
        //    var body = physWorld.CreateRigidBody();
        //    body.AddShape(new Jitter2.Collision.Shapes.BoxShape(shape.HalfExtents.X, shape.HalfExtents.Z, shape.HalfExtents.Y));
        //    body.Position = new Jitter2.LinearMath.JVector(shape.Origin.X, shape.Origin.Z, shape.Origin.Y);
        //    body.MotionType = Jitter2.Dynamics.MotionType.Static;
        //    bodies.Add(body);
        //}

        //create.Stop();
        //Console.WriteLine($"physics-cost: {bodies.Count} static bodies created in {create.ElapsedMilliseconds} ms " +
        //                  $"({create.Elapsed.TotalMilliseconds / Math.Max(1, bodies.Count):F3} ms per body)");

        //// A fixed 25 Hz step with the bodies in place. Without dynamic bodies the broad phase still walks every proxy,
        //// which is the cost the ships will pay alongside.
        //const float step = 1f / 25f;
        //for (var i = 0; i < 5; i++)
        //    physWorld.Step(step, false);
        //const int steps = 100;
        //var watch = Stopwatch.StartNew();
        //for (var i = 0; i < steps; i++)
        //    physWorld.Step(step, false);
        //watch.Stop();
        //var perStep = watch.Elapsed.TotalMilliseconds / steps;
        //Console.WriteLine($"physics-cost: fixed step {perStep:F3} ms per 40 ms tick " +
        //                  $"({perStep / 40.0 * 100:F1} % of the budget), p99 not sampled");
    }

    /// <summary>
    /// The P2 acceptance walk: the floor along one line of a cell, sampled every few centimetres, reporting the
    /// height changes (stairs) — the plan's "follow the visible surface" criterion as data.
    /// </summary>
    private static string DescribeFloorLine(StaticCollision collision, string cellName, float step)
    {
        var parts = cellName.Split('_');
        var x0 = int.Parse(parts[0]) * StaticGeometry.CellSize;
        var y0 = int.Parse(parts[1]) * StaticGeometry.CellSize;
        var y = y0 + StaticGeometry.CellSize / 2f;
        var heights = new List<float>();
        for (var dx = 0f; dx < StaticGeometry.CellSize; dx += step)
        {
            if (collision.TryFloor(x0 + dx, y, 900f, -100f, out var z))
                heights.Add(z);
        }

        if (heights.Count < 2)
            return $"  line y={y:F0}: {heights.Count} points with a floor (too few to describe)";

        var steps = new List<float>();
        for (var i = 1; i < heights.Count; i++)
        {
            var d = MathF.Abs(heights[i] - heights[i - 1]);
            if (d >= 0.05f)
                steps.Add(d);
        }

        steps.Sort();
        var maxStep = steps.Count > 0 ? steps[^1] : 0f;
        var medianStep = steps.Count > 0 ? steps[steps.Count / 2] : 0f;
        return $"  line y={y:F0} step {step:F2} m: {heights.Count} points with a floor, " +
               $"z {heights.Min():F2}..{heights.Max():F2}, {steps.Count} height changes, " +
               $"median {medianStep:F3} m, max {maxStep:F3} m";
    }

    /// Plan P2: raycast accuracy and timing. Reports the floor under the point, the same point against the terrain
    /// heightmap, and the p50/p99 cost of a single <see cref="StaticCollision.TryFloor"/> over the zone index.
    /// </summary>
    private static void ProbeFloor(ClientFiles source, string world, uint zoneKey, float x, float y)
    {
        var (xml, template) = ReadWorld(source, world);
        if (xml == null || template == null)
            return;
        if (!template.XmlWorldZones.TryGetValue(zoneKey, out var zone))
        {
            Console.WriteLine($"floor: zone {zoneKey} absent");
            return;
        }

        var cells = zone.Cells.Keys.Select(c => new StaticCollisionCell(world, c.Item1, c.Item2, xml.OceanLevel)).ToList();
        var watch = Stopwatch.StartNew();
        var collision = StaticCollision.Load(source, cells);
        var loadMs = watch.ElapsedMilliseconds;
        Console.WriteLine($"floor: zone {zoneKey}, {collision.Instances} brushes built in {loadMs} ms");

        var above = 600f;
        var below = -100f;
        var terrainReader = new TerrainReader(source.Read);
        var found = collision.TryFloor(x, y, above, below, out var floor);
        var terrainValue = terrainReader.Height(x, y);
        if (terrainValue is not { } terrain)
        {
            Console.WriteLine("floor: no heightmap for this cell");
            return;
        }
        Console.WriteLine($"floor at ({x:F1}, {y:F1}): {(found ? $"{floor:F3}" : "none")}, terrain {terrain:F3}, " +
                          $"delta {(found ? $"{floor - terrain:+0.###;-0.###}" : "-")}");

        // Single-query cost over the same point (the index is already warm).
        const int samples = 2000;
        var times = new double[samples];
        var hits = 0;
        for (var i = 0; i < samples; i++)
        {
            var t0 = Stopwatch.GetTimestamp();
            if (collision.TryFloor(x, y, above, below, out _))
                hits++;
            times[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        }

        Array.Sort(times);
        Console.WriteLine($"tryfloor x{samples}: p50 {times[samples / 2]:F4} ms, p99 {times[samples * 99 / 100]:F4} ms, " +
                          $"max {times[^1]:F4} ms, hits {hits}");

        // A wider sweep: does a downward ray find the same surface over a grid of points?
        var missed = 0;
        var worst = 0f;
        var checkedPoints = 0;
        for (var dx = -128; dx <= 128; dx += 8)
        {
            for (var dy = -128; dy <= 128; dy += 8)
            {
                checkedPoints++;
                if (!collision.TryFloor(x + dx, y + dy, above, below, out var z))
                {
                    missed++;
                    continue;
                }

                if (terrainReader.Height(x + dx, y + dy) is not { } t)
                    continue;
                worst = MathF.Max(worst, MathF.Abs(z - t));
            }
        }

        Console.WriteLine($"sweep 33x33 (±128 m): {checkedPoints} points, {missed} with no static floor, " +
                          $"worst |floor - terrain| {worst:F3} m");
    }

    private static void ProbeCellFiles(ClientFiles source, string world, string cell)
    {
        var folder = $"game/worlds/{world}/cells/{cell}/client";
        ProbeObjectFile(source, $"{folder}/object.dat");
        ProbeObjectFile(source, $"{folder}/visareas.dat");
        // What StaticCollision will actually pick for this cell.
        var indoor = $"game/worlds/{world}/cells/{cell}/world_server/object_indoor.x2d";
        var outdoor = $"game/worlds/{world}/cells/{cell}/world_server/object.x2d";
        Console.WriteLine($"    sources: object_indoor.x2d={(source.Read(indoor) != null ? "present" : "absent")}, " +
                          $"object.x2d={(source.Read(outdoor) != null ? "present" : "absent")}, " +
                          $"visareas.dat={(source.Read($"{folder}/visareas.dat") != null ? "present" : "absent")}");

        // What one cell alone contributes, with the same sources StaticCollision picks.
        var parts = cell.Split('_');
        var build = StaticCollision.Load(source, [new StaticCollisionCell(world, int.Parse(parts[0]), int.Parse(parts[1]), 100f)]);
        Console.WriteLine($"    one cell: {build}");
    }



    private static void ProbeObjectFile(ClientFiles source, string path)
    {
        var data = source.Read(path);
        if (data == null)
        {
            Console.WriteLine($"object {path}: MISSING");
            return;
        }

        var watch = Stopwatch.StartNew();
        try
        {
            var isVisAreas = path.EndsWith("visareas.dat", StringComparison.OrdinalIgnoreCase);
            var brushes = isVisAreas ? BrushPlacementFile.ReadVisAreas(data) : BrushPlacementFile.Read(data);
            // Only the outdoor object file holds water volumes; visareas.dat has none.
            var water = isVisAreas ? [] : BrushPlacementFile.ReadWater(data);
            watch.Stop();
            var assets = brushes.Select(b => b.AssetPath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            Console.WriteLine($"object {path}: {data.Length} B, {brushes.Count} brushes of {assets} assets, " +
                              $"{water.Count} water volumes, {watch.ElapsedMilliseconds} ms");
            foreach (var name in brushes.Take(3).Select(b => b.AssetPath))
            {
                var bytes = source.Read(name);
                if (bytes == null)
                {
                    Console.WriteLine($"    {name}: MISSING in source");
                    continue;
                }

                var mesh = CgfPhysicsReader.Read(bytes, out var counts);
                Console.WriteLine($"    {name}: {bytes.Length} B, tris={mesh?.TriangleCount ?? 0}, " +
                                  $"box={counts.Boxes} tri={counts.Meshes} round={counts.Rounds} bad={counts.Unreadable}");
            }
        }
        catch (Exception ex)
        {
            watch.Stop();
            Console.WriteLine($"object {path}: {ex.GetType().Name}: {ex.Message} (first bytes: " +
                              $"{string.Join(' ', data.Take(16).Select(b => b.ToString("X2")))}, " +
                              $"i32 = {BitConverter.ToInt32(data, 0)}, {BitConverter.ToInt32(data, 4)}, " +
                              $"{BitConverter.ToInt32(data, 8)}, {BitConverter.ToInt32(data, 12)})");
        }
    }

    private static void PrintZone(XmlWorld xml, uint zoneKey)
    {
        if (!xml.Zones.TryGetValue(zoneKey, out var zone))
        {
            Console.WriteLine($"  zone {zoneKey}: absent");
            return;
        }

        var cells = zone.Cells.Keys.Select(c => $"{c.Item1:000}_{c.Item2:000}");
        Console.WriteLine($"  zone {zoneKey} ({zone.Name}): {zone.Cells.Count} cells [{string.Join(", ", cells.Take(6))}]");
    }

    private static void ProbeList(ClientFiles source, string directory, int limit)
    {
        var files = source.List(directory);
        Console.WriteLine($"list {directory}: {files.Count} files");
        foreach (var (name, size) in files.Take(limit))
            Console.WriteLine($"  {name} ({size} B)");
    }

    /// <summary>
    /// Client files from an unpacked <c>game</c> folder or a <c>game_pak</c>, behind the geometry layer's
    /// <see cref="IGeometryFileSource"/>. Paths are pak-style (<c>game/вЂ¦</c>, forward slashes); the folder form
    /// is resolved under the folder's parent.
    /// </summary>
    private sealed class ClientFiles : IGeometryFileSource, IDisposable
    {
        private readonly string _pakPath;
        private readonly AAPak? _pak;
        private readonly string? _root;

        public ClientFiles(string root)
        {
            _root = root;
            _pakPath = root;
        }

        public ClientFiles(AAPak pak)
        {
            _pak = pak;
            _pakPath = pak._gpFilePath;
        }

        public override string ToString() =>
            _pak is { } pak ? $"pak {_pakPath} ({pak.pakFiles.Count} files)" : $"folder {_root} (read on demand)";

        public byte[]? TryReadBytes(string path) => Read(path);

        public byte[]? Read(string path)
        {
            if (_pak is { } pak)
            {
                if (!pak.FileExists(path))
                    return null;
                using var stream = pak.ExportFileAsStream(path);
                if (stream == null)
                    return null;
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }

            var full = ToLocal(path);
            return full != null && File.Exists(full) ? File.ReadAllBytes(full) : null;
        }

        public List<(string Name, long Size)> List(string directory)
        {
            var result = new List<(string, long)>();
            if (_pak is { } pak)
            {
                // The pak holds no folder entries: match by name prefix.
                var prefix = directory.TrimEnd('/').ToLowerInvariant() + "/";
                foreach (var file in pak.pakFiles.Values)
                    if (file.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        result.Add((file.name, file.size));
                return result;
            }

            var local = ToLocal(directory);
            if (local == null || !Directory.Exists(local))
                return result;
            foreach (var file in Directory.EnumerateFiles(local))
            {
                var size = new FileInfo(file).Length;
                result.Add(($"game/{Path.GetRelativePath(_root!, file).Replace('\\', '/')}", size));
            }
            return result;
        }

        /// <summary><c>game/worlds/x</c> в†’ <c>&lt;root&gt;/worlds/x</c>, since the folder <em>is</em> the <c>game</c> tree.</summary>
        private string? ToLocal(string path)
        {
            var relative = path.Replace('\\', '/');
            var prefix = "game/";
            if (relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                relative = relative[prefix.Length..];
            else if (string.Equals(relative, "game", StringComparison.OrdinalIgnoreCase))
                relative = string.Empty;

            var full = Path.GetFullPath(Path.Combine(_root!, relative.Replace('/', Path.DirectorySeparatorChar)));
            // Never escape the tree (a brush path is data, not a command).
            var rootFull = Path.GetFullPath(_root!);
            return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        public void Dispose()
        {
            if (_pak == null)
                return;
            if (_pak.isOpen)
                _pak.ClosePak();
        }
    }
}
