#nullable enable

using System.Globalization;
using System.Numerics;
using System.Xml;

using AAEmu.Geometry.Abstractions;

using NLog;

namespace AAEmu.Game.Physics.Static;

public readonly record struct ShipDoodadMeshPart(string AssetPath, Matrix4x4 Local, string? Name = null, bool IsBrush = true);

/// <summary>Resolves CGF/CGA and prefab model URIs through the configured client file source, including packed assets.</summary>
public sealed class ShipDoodadModelResolver(IGeometryFileSource source)
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ShipDoodadMeshPart[]> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, ShipDoodadMeshPart[]>> _libraries = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ShipDoodadMeshPart> Resolve(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return Array.Empty<ShipDoodadMeshPart>();
        lock (_gate)
        {
            if (_models.TryGetValue(uri, out var cached))
                return cached;
            var parts = ResolveUncached(uri);
            _models.Add(uri, parts);
            return parts;
        }
    }

    private ShipDoodadMeshPart[] ResolveUncached(string uri)
    {
        var scheme = uri.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0)
            return [];
        var kind = uri[..scheme];
        var relative = uri[(scheme + 3)..].Replace('\\', '/');
        if (kind.Equals("cgf", StringComparison.OrdinalIgnoreCase) || kind.Equals("cga", StringComparison.OrdinalIgnoreCase))
            return [new ShipDoodadMeshPart(AssetPath(relative), Matrix4x4.Identity)];
        if (!kind.Equals("prefab", StringComparison.OrdinalIgnoreCase))
            return [];
        var end = relative.IndexOf(".xml/", StringComparison.OrdinalIgnoreCase);
        if (end < 0)
            return [];
        var library = Library(AssetPath(relative[..(end + 4)]));
        return library.TryGetValue(relative[(end + 5)..], out var parts) ? parts : [];
    }

    private Dictionary<string, ShipDoodadMeshPart[]> Library(string path)
    {
        if (_libraries.TryGetValue(path, out var known))
            return known;
        var result = new Dictionary<string, ShipDoodadMeshPart[]>(StringComparer.OrdinalIgnoreCase);
        _libraries.Add(path, result);
        try
        {
            var bytes = source.TryReadBytes(path);
            if (bytes == null)
                return result;
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            var parsed = new Dictionary<string, List<ShipDoodadMeshPart>>(StringComparer.OrdinalIgnoreCase);
            string? prefab = null;
            var prefabDepth = -1;
            var objectsDepth = -1;
            var entityDepth = -1;
            var entityPose = Matrix4x4.Identity;
            string? entityName = null;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.Depth == entityDepth)
                        entityDepth = -1;
                    if (reader.Depth == objectsDepth)
                        objectsDepth = -1;
                    if (reader.Depth == prefabDepth)
                    {
                        prefab = null;
                        prefabDepth = -1;
                    }
                    continue;
                }
                if (reader.NodeType != XmlNodeType.Element)
                    continue;
                if (reader.Name == "Prefab" && prefab == null)
                {
                    prefab = reader.GetAttribute("Name");
                    prefabDepth = reader.IsEmptyElement ? -1 : reader.Depth;
                    if (prefab != null)
                        parsed.TryAdd(prefab, []);
                    if (reader.IsEmptyElement)
                        prefab = null;
                    continue;
                }
                if (prefab == null)
                    continue;
                if (entityDepth >= 0 && reader.Name == "Properties" && reader.Depth == entityDepth + 1)
                {
                    var model = reader.GetAttribute("object_Model");
                    if (!string.IsNullOrWhiteSpace(model))
                        parsed[prefab].Add(new ShipDoodadMeshPart(AssetPath(model), entityPose, entityName, false));
                    continue;
                }
                if (reader.Name == "Objects" && reader.Depth == prefabDepth + 1)
                {
                    objectsDepth = reader.IsEmptyElement ? -1 : reader.Depth;
                    continue;
                }
                if (objectsDepth < 0 || reader.Name != "Object" || reader.Depth != objectsDepth + 1)
                    continue;
                var type = reader.GetAttribute("Type");
                if (string.Equals(type, "Entity", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(reader.GetAttribute("EntityClass"), "AnimObject", StringComparison.OrdinalIgnoreCase))
                {
                    entityDepth = reader.IsEmptyElement ? -1 : reader.Depth;
                    entityPose = Pose(reader.GetAttribute("Pos"), reader.GetAttribute("Rotate"), reader.GetAttribute("Scale"));
                    entityName = reader.GetAttribute("Name");
                    continue;
                }
                if (!string.Equals(type, "Brush", StringComparison.OrdinalIgnoreCase)) continue;
                var asset = reader.GetAttribute("Prefab");
                if (!string.IsNullOrWhiteSpace(asset))
                    parsed[prefab].Add(new ShipDoodadMeshPart(AssetPath(asset), Pose(reader.GetAttribute("Pos"), reader.GetAttribute("Rotate"), reader.GetAttribute("Scale")), reader.GetAttribute("Name")));
            }
            foreach (var (name, parts) in parsed)
                result.Add(name, parts.ToArray());
        }
        catch (Exception ex) when (ex is XmlException or IOException)
        {
            Logger.Warn(ex, "ship doodad prefab {0} unreadable", path);
        }
        return result;
    }

    private static Matrix4x4 Pose(string? position, string? rotation, string? scale)
    {
        var p = Components(position, 3, 0f);
        var r = Components(rotation, 4, 0f);
        var s = Components(scale, 3, 1f);
        // CryEngine prefab XML stores quaternion components as w,x,y,z.
        var q = rotation == null ? Quaternion.Identity : new Quaternion(r[1], r[2], r[3], r[0]);
        q = q.LengthSquared() > 0f ? Quaternion.Normalize(q) : Quaternion.Identity;
        return Matrix4x4.CreateScale(s[0], s[1], s[2]) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(p[0], p[1], p[2]);
    }

    private static float[] Components(string? text, int count, float fallback)
    {
        var values = new float[count];
        Array.Fill(values, fallback);
        if (string.IsNullOrWhiteSpace(text))
            return values;
        var parts = text.Split(',');
        for (var i = 0; i < count && i < parts.Length; i++)
        {
            if (float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value))
                values[i] = value;
        }
        return values;
    }

    private static string AssetPath(string relative)
    {
        relative = relative.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        return relative.StartsWith("game/", StringComparison.Ordinal) ? relative : "game/" + relative;
    }
}
