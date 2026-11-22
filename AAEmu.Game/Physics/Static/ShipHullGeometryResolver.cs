#nullable enable
using System.Collections.Concurrent;
using System.Numerics;
using System.Xml;
using System.Xml.Linq;
using AAEmu.Game.Physics.Hulls;
using AAEmu.Geometry;
using AAEmu.Geometry.Abstractions;
using AAEmu.Geometry.Source;
using NLog;

namespace AAEmu.Game.Physics.Static;

public readonly record struct ShipHullGeometry(Vector3 Bounds, HullBuoyancy? Buoyancy, GeometryBounds? BrushBounds = null);

/// <summary>Resolves collision bounds and the model's flotation geometry off the simulation thread.</summary>
public sealed class ShipHullGeometryResolver(IGeometryFileSource source)
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public static ShipHullGeometryResolver Instance { get; } = new(new ClientFileCollisionAssetSource());
    private readonly ShipDoodadModelResolver _prefabs = new(source, includeUnresolvedBrushes: true);
    private readonly record struct Request(string Uri, float Radius, float HalfLength, float OffsetZ);
    private readonly ConcurrentDictionary<Request, Lazy<Task<ShipHullGeometry>>> _cache = new();

    public Task<ShipHullGeometry> ResolveAsync(string uri, float tubeRadius = 0, float tubeHalfLength = 0, float tubeOffsetZ = 0) =>
        _cache.GetOrAdd(new Request(uri.ToLowerInvariant(), tubeRadius, tubeHalfLength, tubeOffsetZ),
            key => new Lazy<Task<ShipHullGeometry>>(() => Task.Run(() => Read(key)))).Value;

    private ShipHullGeometry Read(Request request)
    {
        try
        {
            var parts = _prefabs.Resolve(request.Uri);
            var buoyancy = ReadBuoyancy(parts, out var hasNamedBuoys);
            var brushes = ReadRenderBounds(parts, isBrush: true);
            if (!hasNamedBuoys)
                buoyancy = ReadTube(parts, request, brushes);
            return new ShipHullGeometry(ReadBounds(parts), buoyancy, brushes);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Unable to read hull geometry from {0}", request.Uri);
            return default;
        }
    }

    private HullBuoyancy? ReadTube(IReadOnlyList<ShipDoodadMeshPart> parts, Request request, GeometryBounds? brushes)
    {
        // HullBuilder can place an authored tube with an empty prefab. Only zero fields need mesh bounds.
        if (request.Radius != 0f && request.HalfLength != 0f && request.OffsetZ != 0f)
            return HullBuoyancy.FromTube(request.Radius, request.HalfLength, request.OffsetZ);

        var animated = ReadRenderBounds(parts, isBrush: false);
        if (!brushes.HasValue && !animated.HasValue)
            return null;
        // An entity with no brush meshes starts with a point at the origin. Its AnimObjects expand that box.
        var bounds = brushes.GetValueOrDefault();
        if (animated.HasValue)
            bounds = bounds.Union(animated.Value);
        var half = (bounds.Max - bounds.Min) * 0.5f;
        return HullBuoyancy.FromTube(request.Radius == 0f ? half.X : request.Radius,
            request.HalfLength == 0f ? half.Y : request.HalfLength, request.OffsetZ == 0f ? half.X : request.OffsetZ);
    }

    private static bool IsSlot(ShipDoodadMeshPart part) =>
        part.AssetPath.Length > 0 && (!part.IsBrush || part.Name?.Contains("ignore_slotbrush", StringComparison.Ordinal) != true);

    private GeometryBounds? ReadRenderBounds(IReadOnlyList<ShipDoodadMeshPart> parts, bool isBrush)
    {
        GeometryBounds? result = null;
        foreach (var part in parts)
        {
            if (part.IsBrush == isBrush && IsSlot(part) && ReadPartRenderBounds(part) is { } box)
                result = result?.Union(box) ?? box;
        }
        return result;
    }

    private GeometryBounds? ReadPartRenderBounds(ShipDoodadMeshPart part)
    {
        var bytes = source.TryReadBytes(part.AssetPath);
        if (!part.IsBrush && bytes != null && part.AssetPath.EndsWith(".cdf", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                using var reader = XmlReader.Create(stream);
                var model = (string?)XDocument.Load(reader).Root?.Element("Model")?.Attribute("File");
                bytes = string.IsNullOrWhiteSpace(model) ? null : source.TryReadBytes(ShipDoodadModelResolver.AssetPath(model));
            }
            catch (XmlException)
            {
                return null;
            }
        }
        return CgfPhysicsReader.ObjectRenderBounds(bytes)?.Transform(part.Local);
    }

    private HullBuoyancy? ReadBuoyancy(IReadOnlyList<ShipDoodadMeshPart> parts, out bool hasNamedBuoys)
    {
        hasNamedBuoys = false;
        var boxes = new List<HullBuoyancy.Surface>();
        foreach (var part in parts)
        {
            if (!part.IsBrush || part.Name is not { } name || !name.StartsWith("buoy", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("ignore_slotbrush", StringComparison.Ordinal)) continue;
            hasNamedBuoys = true;
            if (part.AssetPath.Length == 0) return null;
            var bytes = source.TryReadBytes(part.AssetPath);
            var mesh = CgfPhysicsReader.Read(bytes, out var counts);
            if (mesh == null || counts is not { Boxes: 1, Meshes: 0, Rounds: 0, Unreadable: 0 } ||
                HullBuoyancy.Surface.FromBoxProxy(mesh, part.Local) is not { } box) return null;
            boxes.Add(box);
        }
        return boxes.Count == 0 ? null : new HullBuoyancy(boxes);
    }

    private Vector3 ReadBounds(IReadOnlyList<ShipDoodadMeshPart> parts)
    {
        ShipDoodadMeshPart? body = null;
        ShipDoodadMeshPart? named = null;
        ShipDoodadMeshPart? single = null;
        var bodies = 0;
        var namedCount = 0;
        var shipBrushes = 0;
        foreach (var part in parts)
        {
            if (part.AssetPath.Length == 0) continue;
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
        var mesh = CgfRenderReader.Read(bytes, out _) ?? CgfPhysicsReader.Read(bytes, out _);
        if (mesh == null) return Vector3.Zero;
        var box = new GeometryBounds(mesh.Min, mesh.Max).Transform(item.Local);
        var size = box.Max - box.Min;
        return float.IsFinite(size.X) && float.IsFinite(size.Y) && size.X > 0f && size.Y > 0f ? size : Vector3.Zero;
    }
}
