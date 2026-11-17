#nullable enable
using System.Collections.Concurrent;
using System.Numerics;
using AAEmu.Geometry;
using AAEmu.Geometry.Abstractions;
using AAEmu.Geometry.Source;
using NLog;

namespace AAEmu.Game.Physics.Static;

/// <summary>Reads a prefab's uniquely named body off the simulation thread when the slave has no authored OBB.</summary>
public sealed class ShipHullBoundsResolver(IGeometryFileSource source)
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public static ShipHullBoundsResolver Instance { get; } = new(new ClientFileCollisionAssetSource());
    private readonly ShipDoodadModelResolver _prefabs = new(source);
    private readonly ConcurrentDictionary<string, Lazy<Task<Vector3>>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Task<Vector3> ResolveAsync(string uri) => _cache.GetOrAdd(uri,
        key => new Lazy<Task<Vector3>>(() => Task.Run(() => Read(key)), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private Vector3 Read(string uri)
    {
        try
        {
            ShipDoodadMeshPart? body = null;
            ShipDoodadMeshPart? named = null;
            ShipDoodadMeshPart? single = null;
            var bodies = 0;
            var namedCount = 0;
            var shipBrushes = 0;
            foreach (var part in _prefabs.Resolve(uri))
            {
                if (string.Equals(part.Name, "body", StringComparison.OrdinalIgnoreCase))
                {
                    body = part;
                    bodies++;
                }
                if (!part.IsBrush || !part.AssetPath.Contains("/01_ship/", StringComparison.OrdinalIgnoreCase)) continue;
                single = part;
                shipBrushes++;
                if (part.Name?.Contains("_body", StringComparison.OrdinalIgnoreCase) == true)
                {
                    named = part;
                    namedCount++;
                }
            }
            if (bodies > 1) return Vector3.Zero;
            if (bodies == 0)
                body = namedCount == 1 ? named : namedCount == 0 && shipBrushes == 1 ? single : null;
            if (body is not { } item) return Vector3.Zero;
            var bytes = source.TryReadBytes(item.AssetPath);
            if (bytes == null) return Vector3.Zero;
            // RenderBounds reports each mesh in its own frame; use the full reader so node transforms are applied.
            var mesh = CgfRenderReader.Read(bytes, out _) ?? CgfPhysicsReader.Read(bytes, out _);
            if (mesh == null) return Vector3.Zero;
            var box = (mesh.Min, mesh.Max);
            var min = new Vector3(float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity);
            for (var mask = 0; mask < 8; mask++)
            {
                var corner = new Vector3((mask & 1) == 0 ? box.Min.X : box.Max.X, (mask & 2) == 0 ? box.Min.Y : box.Max.Y,
                    (mask & 4) == 0 ? box.Min.Z : box.Max.Z);
                var transformed = Vector3.Transform(corner, item.Local);
                min = Vector3.Min(min, transformed);
                max = Vector3.Max(max, transformed);
            }
            var size = max - min;
            return float.IsFinite(size.X) && float.IsFinite(size.Y) && size.X > 0f && size.Y > 0f ? size : Vector3.Zero;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Unable to read hull bounds from {0}", uri);
            return Vector3.Zero;
        }
    }
}
