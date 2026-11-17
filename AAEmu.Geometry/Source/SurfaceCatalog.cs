using System.Xml;

using AAEmu.Geometry.Abstractions;

namespace AAEmu.Geometry;

/// <summary>
/// The surface types of the client (<c>libs/materialeffects/surfacetypes.xml</c>): the names in the file's order, each
/// with its id. The native engine registers them in that order, so the index in the file is the id it uses.
/// <para>
/// A part's surface is the one its material names: the material file's sub-material at the proxy's index. The catalog
/// maps that name to the id, so a hull can tell rock from sand from grass — and take the friction of each.
/// </para>
/// </summary>
public sealed class SurfaceCatalog
{
    private readonly Dictionary<string, int> _ids;

    private SurfaceCatalog(Dictionary<string, int> ids, IReadOnlyList<string> names)
    {
        _ids = ids;
        Names = names;
    }

    /// <summary>The surface type names, in the client's order.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>An empty catalog: every surface is the default one (id 0).</summary>
    public static SurfaceCatalog Empty { get; } = new(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), []);

    /// <summary>
    /// Reads the client's surface types (<c>game/libs/materialeffects/surfacetypes.xml</c>); an empty catalog when the
    /// file is missing or unreadable.
    /// </summary>
    public static SurfaceCatalog Load(IGeometryFileSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var bytes = source.TryReadBytes("game/libs/materialeffects/surfacetypes.xml");
        if (bytes == null)
            return Empty;
        try
        {
            var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var names = new List<string>();
            using var reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Name != "SurfaceType")
                    continue;
                var name = reader.GetAttribute("name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                ids.TryAdd(name, names.Count);
                names.Add(name);
            }
            return new SurfaceCatalog(ids, names);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            GeometryLog.Warn("surface types unreadable", ex);
            return Empty;
        }
    }

    /// <summary>The id of a surface type name; 0 (the default) when the catalog does not know it.</summary>
    public int IdOf(string? surfaceType)
        => surfaceType != null && _ids.TryGetValue(surfaceType, out var id) ? id : 0;

    /// <summary>The name of a surface id; null when the id is unknown.</summary>
    public string? NameOf(int id) => id >= 0 && id < Names.Count ? Names[id] : null;
}
