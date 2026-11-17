#nullable enable
using System.Collections.Concurrent;
using System.Numerics;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Geometry;
using AAEmu.Geometry.Abstractions;
using NLog;

namespace AAEmu.Game.Physics.Static;

/// <summary>Immutable snapshots of live ship-blocking doodads, built off the simulation thread.</summary>
public sealed class ShipDoodadCollision
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public static ShipDoodadCollision Instance { get; } = new(new ClientFileCollisionAssetSource());
    private readonly IGeometryFileSource _source;
    private readonly ShipDoodadModelResolver _resolver;
    private readonly CollisionAssetCache _assets = new();
    private readonly ConcurrentDictionary<uint, World> _worlds = new();
    private ShipDoodadModelCatalog _catalog = ShipDoodadModelCatalog.Empty;

    private readonly record struct Placement(uint TemplateId, uint Phase, Vector3 Position, Quaternion Rotation, float Scale);
    private sealed class World
    {
        public readonly object Gate = new();
        public readonly Dictionary<uint, Placement> Items = new();
        public StaticCollision Collision = StaticCollision.Empty;
        public long Version;
        public bool Building;
        public bool Closed;
    }

    public ShipDoodadCollision(IGeometryFileSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _resolver = new ShipDoodadModelResolver(source);
    }

    public void SetCatalog(ShipDoodadModelCatalog catalog) => Volatile.Write(ref _catalog, catalog);

    public void Upsert(uint worldId, Doodad doodad)
    {
        ArgumentNullException.ThrowIfNull(doodad);
        if (!Volatile.Read(ref _catalog).TryGetModel(doodad.TemplateId, doodad.FuncGroupId, out _) ||
            doodad.Transform.Parent != null || doodad.Transform.StickyParent != null ||
            !float.IsFinite(doodad.Scale) || doodad.Scale <= 0f)
        {
            Remove(worldId, doodad.ObjId);
            return;
        }
        var transform = doodad.Transform.World;
        var placement = new Placement(doodad.TemplateId, doodad.FuncGroupId, transform.Position, transform.ToQuaternion(), doodad.Scale);
        var world = _worlds.GetOrAdd(worldId, _ => new World());
        lock (world.Gate)
        {
            if (world.Closed || (world.Items.TryGetValue(doodad.ObjId, out var old) && old == placement)) return;
            world.Items[doodad.ObjId] = placement;
            Schedule(world);
        }
    }

    public void Remove(uint worldId, uint objectId)
    {
        if (!_worlds.TryGetValue(worldId, out var world)) return;
        lock (world.Gate)
        {
            if (!world.Closed && world.Items.Remove(objectId)) Schedule(world);
        }
    }

    public void RemoveWorld(uint worldId)
    {
        if (!_worlds.TryRemove(worldId, out var world)) return;
        lock (world.Gate) { world.Closed = true; world.Items.Clear(); Volatile.Write(ref world.Collision, StaticCollision.Empty); }
    }

    public StaticCollision Current(uint worldId) => _worlds.TryGetValue(worldId, out var world)
        ? Volatile.Read(ref world.Collision) : StaticCollision.Empty;

    private void Schedule(World world)
    {
        world.Version++;
        if (world.Building) return;
        world.Building = true;
        _ = Task.Run(() => Build(world));
    }

    private void Build(World world)
    {
        try
        {
            while (true)
            {
                Placement[] placements;
                long version;
                lock (world.Gate)
                {
                    if (world.Closed) { world.Building = false; return; }
                    placements = new Placement[world.Items.Count];
                    world.Items.Values.CopyTo(placements, 0);
                    version = world.Version;
                }
                var shapes = new List<(CollisionMesh Mesh, Matrix4x4 ToWorld)>();
                var catalog = Volatile.Read(ref _catalog);
                foreach (var placement in placements)
                {
                    if (!catalog.TryGetModel(placement.TemplateId, placement.Phase, out var uri)) continue;
                    var worldPose = Matrix4x4.CreateScale(placement.Scale) * Matrix4x4.CreateFromQuaternion(placement.Rotation) * Matrix4x4.CreateTranslation(placement.Position);
                    foreach (var part in _resolver.Resolve(uri))
                    {
                        // Character meshes can lack a default CGF proxy even though the visible animated object blocks
                        // ships. Keep a static render-mesh fallback until its skeletal physics can be reconstructed.
                        var mode = part.AssetPath.EndsWith(".chr", StringComparison.OrdinalIgnoreCase)
                            ? GeometryPhysicsSource.ProxyThenRender : GeometryPhysicsSource.Proxy;
                        var mesh = _assets.Get(_source, part.AssetPath, mode);
                        if (mesh != null) shapes.Add((mesh, part.Local * worldPose));
                    }
                }
                var collision = StaticCollision.FromInstances(shapes);
                lock (world.Gate)
                {
                    if (world.Closed) { world.Building = false; return; }
                    if (version != world.Version) continue;
                    Volatile.Write(ref world.Collision, collision);
                    world.Building = false;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to rebuild ship doodad collision");
            lock (world.Gate) world.Building = false;
        }
    }
}
