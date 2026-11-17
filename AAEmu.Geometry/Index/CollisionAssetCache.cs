using AAEmu.Geometry.Source;
using AAEmu.Geometry.Abstractions;


namespace AAEmu.Geometry;

/// <summary>
/// Process-wide asset path → <see cref="CollisionMesh"/> (shared read-only data). Every cell's
/// <see cref="StaticCollision"/> takes its meshes from here, so an asset used by several cells
/// is read and built once. Entries are weak: a mesh lives as long as some cell references it and
/// is read again after the last one dropped it. Thread-safe (cell loads run on the game thread or
/// in parallel tests).
/// </summary>
public sealed class CollisionAssetCache
{
    private readonly object _lock = new();
    private readonly Dictionary<string, WeakReference<CollisionMesh>?> _entries = new(StringComparer.OrdinalIgnoreCase);

    public static CollisionAssetCache Shared { get; } = new();

    /// <summary>Files read (a cache hit reads nothing).</summary>
    public int Reads { get; private set; }

    /// <summary>The client's surface types, read once from the first source that asks for a mesh.</summary>
    private SurfaceCatalog? _surfaces;
    private IGeometryFileSource? _surfacesSource;

    /// <summary>The surface types of the client's data (the cache's source).</summary>
    public SurfaceCatalog Surfaces(IGeometryFileSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_surfaces != null && ReferenceEquals(_surfacesSource, source))
            return _surfaces;
        lock (_lock)
        {
            if (_surfaces == null || !ReferenceEquals(_surfacesSource, source))
            {
                _surfaces = SurfaceCatalog.Load(source);
                _surfacesSource = source;
            }
            return _surfaces;
        }
    }

    /// <summary>
    /// The asset's collision mesh, or null when it has none (no file, no cgf, no default proxy).
    /// <paramref name="assetPath"/> is the path the object file names (<c>game/objects/…</c>).
    /// </summary>
    public CollisionMesh? Get(IGeometryFileSource source, string assetPath) =>
        Get(source, assetPath, StaticCollision.Options.PhysicsSource);

    /// <summary>
    /// The asset's collision mesh under an explicit <paramref name="physicsSource"/>; the cache is keyed by asset path
    /// only, so the first mode asked for an asset wins (the server picks its mode once at startup).
    /// </summary>
    /// <param name="surfaces">The client's surface types: they give each mesh the surface its material names.</param>
    public CollisionMesh? Get(IGeometryFileSource source, string assetPath, GeometryPhysicsSource physicsSource, SurfaceCatalog? surfaces = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var key = assetPath.Replace('\\', '/');
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var weak))
            {
                if (weak == null)
                    return null; // known to have no collision
                if (weak.TryGetTarget(out var cached))
                    return cached;
            }
        }

        var mesh = Load(source, key, physicsSource, surfaces);
        lock (_lock)
        {
            Reads++;
            if (_entries.TryGetValue(key, out var raced) && raced != null && raced.TryGetTarget(out var other))
                return other;
            _entries[key] = mesh == null ? null : new WeakReference<CollisionMesh>(mesh);
        }

        return mesh;
    }

    private static CollisionMesh? Load(IGeometryFileSource source, string key, GeometryPhysicsSource mode, SurfaceCatalog? surfaces)
    {
        var bytes = source.TryReadBytes(key);
        if (bytes == null)
        {
            GeometryLog.Debug($"collision asset {key} not found");
            return null;
        }

        try
        {
            if (mode != GeometryPhysicsSource.Render)
            {
                var proxy = CgfPhysicsReader.Read(bytes, out var proxyCounts);
                if (proxyCounts.Unreadable > 0)
                    GeometryLog.Warn($"collision asset {key}: {proxyCounts.Unreadable} proxy(ies) unreadable, {proxyCounts.Boxes + proxyCounts.Meshes + proxyCounts.Rounds} read");
                if (proxy is { TriangleCount: > 0 })
                {
                    // Without a material chunk CryEngine takes the material named as the asset.
                    proxy.Material = MaterialFile.PathOf(proxyCounts.Material ?? Path.GetFileNameWithoutExtension(key), Path.GetDirectoryName(key));
                    proxy.MaterialSlot = proxyCounts.MaterialSlot;
                    proxy.Surface = SurfaceOf(source, proxy, surfaces);
                    return proxy;
                }
                if (mode == GeometryPhysicsSource.Proxy)
                    return null; // proxy-only: an asset without one has no collision
            }

            var render = CgfRenderReader.Read(bytes, out var renderCounts);
            if (renderCounts.Unreadable > 0)
                GeometryLog.Warn($"collision asset {key}: {renderCounts.Unreadable} render mesh(es) unreadable, {renderCounts.Meshes} read");
            if (render is { TriangleCount: > 0 })
            {
                render.Material = MaterialFile.PathOf(Path.GetFileNameWithoutExtension(key), Path.GetDirectoryName(key));
                render.Surface = SurfaceOf(source, render, surfaces);
                return render;
            }

            GeometryLog.Debug($"collision asset {key}: no collision geometry");
            return null;
        }
        catch (Exception ex)
        {
            GeometryLog.Warn($"collision asset {key} unreadable", ex);
            return null;
        }
    }

    /// <summary>
    /// The surface the asset's material names: the sub-material the proxy points at, read from the material file.
    /// The name itself is kept on the mesh; the id comes from the client's surface types.
    /// </summary>
    private static int SurfaceOf(IGeometryFileSource source, CollisionMesh mesh, SurfaceCatalog? surfaces)
    {
        if (surfaces == null || mesh.Material == null)
            return 0;
        var mtl = source.TryReadBytes(mesh.Material);
        if (mtl == null)
            return 0;
        try
        {
            var material = MaterialFile.Read(mtl);
            var name = mesh.MaterialSlot >= 0 && mesh.MaterialSlot < material.SubMaterials.Count
                ? material.SubMaterials[mesh.MaterialSlot]
                : material.SurfaceType;
            mesh.SurfaceType = name;
            return surfaces.IdOf(name);
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
        {
            GeometryLog.Debug($"material {mesh.Material} unreadable");
            return 0;
        }
    }
}
