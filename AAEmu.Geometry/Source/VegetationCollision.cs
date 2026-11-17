using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Xml;

using AAEmu.Geometry.Abstractions;
using AAEmu.Geometry.Source;

namespace AAEmu.Geometry;

/// <summary>
/// The vegetation native's dedicate gives collision: the instances of a cell's <c>client/vegetation.dat</c> whose group
/// (the world's <c>vegetation.xml</c>) names a model with a default physics proxy and whose radius × scale is 1 m or
/// more (<c>e_vegetation_min_size</c>; <c>e_vegetation_create_collision_only</c> drops the rest). Continent coordinates.
/// <para>
/// Ported from ArcheageZones (10.x): the client data is read through <see cref="IGeometryFileSource"/> instead of an
/// unpacked folder, so the same code serves the pak and a folder.
/// </para>
/// </summary>
public static class VegetationCollision
{
    private const int Version = 1;
    private const int Sectors = 16;
    private const int SectorsPerSide = 4;
    private const int HeaderSize = 4 + 2 * 4 * Sectors;
    private const int RecordSize = 64;
    private const float SectorMetres = 256f;
    private const float MinSize = 1f;
    private const float MinScale = 0.1f, MaxScale = 5f;

    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<int, Group>>> s_groupTables = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, float> s_radii = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One placed instance: its 256 m sector of the cell (x × 4 + y), position (x and y from the sector's corner), scale, group and angle bytes.</summary>
    public readonly record struct Instance(int Sector, Vector3 Position, float Scale, short Group, byte AngleX, byte AngleY, byte AngleZ);

    /// <summary>A vegetation group: its model and whether it aligns to the terrain.</summary>
    public sealed record Group(int Id, string Name, string Model, bool AlignToTerrain);

    /// <summary>The instances of a <c>vegetation.dat</c>: a 0x84-byte header (version, 16 sector offsets and sizes), then 64-byte records.</summary>
    public static IReadOnlyList<Instance> Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || BitConverter.ToInt32(data) != Version)
            throw new InvalidDataException("not a version 1 vegetation.dat");
        var instances = new List<Instance>();
        for (var k = 0; k < Sectors; k++)
        {
            var offset = BitConverter.ToInt32(data[(4 + 4 * k)..]);
            var size = BitConverter.ToInt32(data[(4 + 4 * Sectors + 4 * k)..]);
            if (size <= 0)
                continue;
            if (offset < HeaderSize || (long)offset + size > data.Length)
                throw new InvalidDataException($"sector {k} runs past the file");
            for (var r = 0; r < size / RecordSize; r++)
            {
                var record = data.Slice(offset + r * RecordSize, RecordSize);
                instances.Add(new Instance(k,
                    new Vector3(BitConverter.ToSingle(record[0x27..]), BitConverter.ToSingle(record[0x2b..]), BitConverter.ToSingle(record[0x2f..])),
                    BitConverter.ToSingle(record[0x33..]), BitConverter.ToInt16(record[0x37..]), record[0x39], record[0x3a], record[0x3c]));
            }
        }

        return instances;
    }

    /// <summary>A world's groups by id (<c>worlds/&lt;world&gt;/vegetation.xml</c>), read once; empty when the world has none.</summary>
    public static IReadOnlyDictionary<int, Group> Groups(IGeometryFileSource source, string worldName) =>
        s_groupTables.GetOrAdd($"{worldName}|{source.GetHashCode()}", _ => new Lazy<IReadOnlyDictionary<int, Group>>(() => ReadGroups(source, worldName))).Value;

    private static Dictionary<int, Group> ReadGroups(IGeometryFileSource source, string worldName)
    {
        var groups = new Dictionary<int, Group>();
        var bytes = source.TryReadBytes($"game/worlds/{worldName}/vegetation.xml");
        if (bytes == null)
            return groups;
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Name != "group"
                    || !int.TryParse(reader.GetAttribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    continue;
                groups.TryAdd(id, new Group(id, reader.GetAttribute("name") ?? "", reader.GetAttribute("modelFileName") ?? "",
                    reader.GetAttribute("bAlignToTerrain") == "1"));
            }
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            GeometryLog.Warn($"vegetation groups of world {worldName} unreadable", ex);
        }

        return groups;
    }

    /// <summary>The instance's placement: rotation about x, y then z from the angle bytes (byte × 360 / 255 degrees), uniform scale, position in the cell.</summary>
    public static Matrix4x4 ToWorld(Instance instance, int cellX, int cellY)
    {
        const float radiansPerByte = 2 * MathF.PI / 255f;
        var corner = new Vector3(cellX * (float)StaticGeometry.CellSize + instance.Sector / SectorsPerSide * SectorMetres,
            cellY * (float)StaticGeometry.CellSize + instance.Sector % SectorsPerSide * SectorMetres, 0f);
        return Matrix4x4.CreateScale(Scale(instance)) * Matrix4x4.CreateRotationX(instance.AngleX * radiansPerByte)
            * Matrix4x4.CreateRotationY(instance.AngleY * radiansPerByte) * Matrix4x4.CreateRotationZ(instance.AngleZ * radiansPerByte)
            * Matrix4x4.CreateTranslation(corner + instance.Position);
    }

    private static float Scale(Instance instance) => Math.Clamp(instance.Scale, MinScale, MaxScale);

    /// <summary>The instance's collision mesh when native's dedicate gives it collision; null otherwise.</summary>
    public static CollisionMesh? Colliding(IGeometryFileSource source, Instance instance, IReadOnlyDictionary<int, Group> groups, CollisionAssetCache cache)
    {
        if (!groups.TryGetValue(instance.Group, out var group) || string.IsNullOrWhiteSpace(group.Model))
            return null;
        var mesh = cache.Get(source, AssetPath(group.Model));
        return mesh != null && Radius(source, group.Model) * Scale(instance) >= MinSize ? mesh : null;
    }

    /// <summary>
    /// The model's path as the data names it (the world file writes it without the client's <c>game/</c> prefix) — the
    /// path the file source can read.
    /// </summary>
    public static string AssetPath(string model)
    {
        var path = model.Replace('\\', '/').TrimStart('/');
        return path.StartsWith("game/", StringComparison.OrdinalIgnoreCase) ? path : "game/" + path;
    }

    /// <summary>Half the diagonal of the model's mesh box, read once per model.</summary>
    private static float Radius(IGeometryFileSource source, string model) => s_radii.GetOrAdd(model.Replace('\\', '/'), key =>
    {
        var bytes = source.TryReadBytes(AssetPath(key));
        if (bytes == null)
            return 0f;
        try
        {
            return CgfPhysicsReader.RenderBounds(bytes) is var (min, max) ? Vector3.Distance(min, max) / 2 : 0f;
        }
        catch (IOException)
        {
            return 0f;
        }
    });

    /// <summary>The colliding vegetation of the given cells.</summary>
    public static StaticCollision Load(IGeometryFileSource source, string worldName, IEnumerable<(int X, int Y)> cells, CollisionAssetCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        cache ??= CollisionAssetCache.Shared;
        var watch = Stopwatch.StartNew();
        var groups = Groups(source, worldName);
        var placed = new List<(CollisionMesh, Matrix4x4)>();
        var read = 0;
        foreach (var (cx, cy) in cells)
        {
            read += ReadCell(source, worldName, cx, cy, groups, cache, placed);
        }

        var collision = StaticCollision.FromInstances(placed);
        GeometryLog.Info($"world {worldName}: {collision.Instances} colliding vegetation of {read} instances, {watch.Elapsed.TotalMilliseconds:F0} ms");
        return collision;
    }

    /// <summary>
    /// The colliding vegetation of one cell: the instances native's dedicate gives collision, with their placements.
    /// </summary>
    public static IEnumerable<(CollisionMesh Mesh, Matrix4x4 ToWorld)> Instances(IGeometryFileSource source, string worldName, int cellX, int cellY, CollisionAssetCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        cache ??= CollisionAssetCache.Shared;
        var placed = new List<(CollisionMesh, Matrix4x4)>();
        ReadCell(source, worldName, cellX, cellY, Groups(source, worldName), cache, placed);
        return placed;
    }

    /// <summary>Reads one cell's vegetation file and appends the colliding instances; returns how many it read.</summary>
    private static int ReadCell(IGeometryFileSource source, string worldName, int cellX, int cellY, IReadOnlyDictionary<int, Group> groups,
        CollisionAssetCache cache, List<(CollisionMesh, Matrix4x4)> placed)
    {
        var bytes = source.TryReadBytes($"game/worlds/{worldName}/cells/{cellX:000}_{cellY:000}/client/vegetation.dat");
        if (bytes == null)
            return 0;
        IReadOnlyList<Instance> instances;
        try
        {
            instances = Read(bytes);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            GeometryLog.Warn($"vegetation file of cell {cellX:000}_{cellY:000} unreadable", ex);
            return 0;
        }

        foreach (var instance in instances)
        {
            if (Colliding(source, instance, groups, cache) is { } mesh)
                placed.Add((mesh, ToWorld(instance, cellX, cellY)));
        }

        return instances.Count;
    }
}
