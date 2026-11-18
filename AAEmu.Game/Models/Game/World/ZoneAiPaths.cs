using System.Globalization;
using System.Numerics;
using System.Xml;

using AAEmu.Commons.IO;
using AAEmu.Game.IO;

using NLog;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Designer patrol paths from <c>level_design/zone/{zoneKey}/editor/npc.xml</c> (ArcheageZones <c>ZoneAiPaths</c>).
/// </summary>
public sealed record ZoneDesignerPath(string Name, Vector3 Origin, IReadOnlyList<Vector3> Points, int PathType, float Speed)
{
    public bool Loops => PathType == 0;

    /// <summary>Zone-local waypoints: <c>spawnLocal + Origin + point</c>, Z from spawn.</summary>
    public List<Vector3> LaidAt(Vector3 spawnLocal, int pointNo = 0)
    {
        var start = Math.Clamp(pointNo, 0, Math.Max(0, Points.Count - 1));
        var route = new List<Vector3>(Points.Count - start);
        for (var i = start; i < Points.Count; i++)
            route.Add(new Vector3(
                spawnLocal.X + Origin.X + Points[i].X,
                spawnLocal.Y + Origin.Y + Points[i].Y,
                spawnLocal.Z));
        return route;
    }
}

public sealed class ZoneAiPathIndex
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<string, ZoneDesignerPath> _paths;

    public ZoneAiPathIndex(IEnumerable<ZoneDesignerPath> paths)
    {
        _paths = new Dictionary<string, ZoneDesignerPath>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
            _paths.TryAdd(path.Name, path);
    }

    public static ZoneAiPathIndex Empty { get; } = new([]);

    public int Count => _paths.Count;

    public ZoneDesignerPath? Get(string name) => _paths.GetValueOrDefault(name);

    public static string EditorNpcXmlPath(string worldName, uint zoneKey) =>
        Path.Combine(FileManager.AppPath, "Data", "Worlds", worldName, "level_design", "zone",
            zoneKey.ToString(CultureInfo.InvariantCulture), "editor", "npc.xml");

    public static bool TryReadXml(string worldName, uint zoneKey, out string text, out string source)
    {
        text = string.Empty;
        source = string.Empty;
        var clientRelative = Path.Combine("game", "worlds", worldName, "level_design", "zone",
            zoneKey.ToString(CultureInfo.InvariantCulture), "editor", "npc.xml");
        if (ClientFileManager.Sources.Count > 0 && ClientFileManager.FileExists(clientRelative))
        {
            text = ClientFileManager.GetFileAsString(clientRelative);
            source = clientRelative;
            return !string.IsNullOrWhiteSpace(text);
        }

        var disk = EditorNpcXmlPath(worldName, zoneKey);
        if (!File.Exists(disk))
            return false;
        text = File.ReadAllText(disk);
        source = disk;
        return !string.IsNullOrWhiteSpace(text);
    }

    public static ZoneAiPathIndex Load(string worldName, uint zoneKey, IReadOnlySet<string>? wanted = null)
    {
        if (!TryReadXml(worldName, zoneKey, out var xml, out var source))
            return Empty;

        try
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
            var index = Parse(stream, wanted);
            if (wanted is { Count: > 0 })
                Logger.Debug("zone {0}: loaded {1} designer paths from {2}", zoneKey, index.Count, source);
            return index;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to parse designer paths from {0}", source);
            return Empty;
        }
    }

    public static ZoneAiPathIndex Parse(System.IO.Stream xml, IReadOnlySet<string>? wanted = null)
    {
        var list = new List<ZoneDesignerPath>();
        using var reader = XmlReader.Create(xml, new XmlReaderSettings
        {
            IgnoreWhitespace = true,
            IgnoreComments = true,
            DtdProcessing = DtdProcessing.Ignore
        });
        while (reader.ReadToFollowing("aiPath"))
        {
            var name = reader.GetAttribute("Name") ?? "";
            if (name.Length == 0 || (wanted != null && !wanted.Contains(name)))
                continue;
            var origin = ParseVec(reader.GetAttribute("Pos"));
            var type = int.TryParse(reader.GetAttribute("Rec._PathType"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : 0;
            var speed = float.TryParse(reader.GetAttribute("Rec._Speed"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 1f;
            var points = new List<Vector3>();
            if (!reader.IsEmptyElement)
            {
                var depth = reader.Depth;
                while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.Name == "Point")
                        points.Add(ParseVec(reader.GetAttribute("Pos")));
                }
            }

            if (points.Count > 0)
                list.Add(new ZoneDesignerPath(name, origin, points, type, speed));
        }

        return new ZoneAiPathIndex(list);
    }

    private static Vector3 ParseVec(string? s)
    {
        var parts = (s ?? "").Split(',');
        float F(int i) => i < parts.Length && float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
        return new Vector3(F(0), F(1), F(2));
    }
}