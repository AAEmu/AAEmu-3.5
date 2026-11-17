using System.Xml.Linq;

namespace AAEmu.Geometry.Source;

/// <summary>
/// The surface type names of a CryEngine <c>.mtl</c>: the material's own and, for a multi-material, each
/// sub-material's in order. Native reads the same file to know what a surface is (rock, sand, wood) and what
/// friction and bounce it gives a hull that touches it.
/// </summary>
/// <param name="SurfaceType">The material's own surface type; null when the file names none.</param>
/// <param name="SubMaterials">The sub-materials' surface types, in the file's order.</param>
public sealed record MaterialFile(string? SurfaceType, IReadOnlyList<string?> SubMaterials)
{
    /// <summary>
    /// A material reference as the data names it, as a path under the data root: lower case, '/' separators,
    /// <c>.mtl</c> added; a bare name lies in <paramref name="folder"/> (the folder of the asset naming it).
    /// </summary>
    /// <remarks>
    /// The path keeps the <c>game/</c> prefix the client's data uses, so it can be read straight from the pak
    /// (<c>game/objects/…/x.mtl</c>). ArcheageZones keeps the same helper but strips that prefix, because its data
    /// root is the extracted <c>game</c> folder.
    /// </remarks>
    public static string? PathOf(string? reference, string? folder = null)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;
        var path = reference.Trim().Replace('\\', '/').ToLowerInvariant();
        if (!path.Contains('/') && !string.IsNullOrEmpty(folder))
            path = folder.Replace('\\', '/').TrimEnd('/').ToLowerInvariant() + "/" + path;
        return path.EndsWith(".mtl", StringComparison.Ordinal) ? path : path + ".mtl";
    }

    /// <summary>Reads a material file's bytes (an XML document) into its surface types.</summary>
    public static MaterialFile Read(byte[] data)
    {
        var document = XDocument.Parse(System.Text.Encoding.UTF8.GetString(data));
        var root = document.Root;
        var subs = root?.Element("SubMaterials")?.Elements("Material").Select(m => SurfaceOf(m)).ToList() ?? [];
        return new MaterialFile(root == null ? null : SurfaceOf(root), subs);
    }

    private static string? SurfaceOf(XElement material) => (string?)material.Attribute("SurfaceType") is { Length: > 0 } name ? name : null;
}
