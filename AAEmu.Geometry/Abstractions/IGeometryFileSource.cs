namespace AAEmu.Geometry.Abstractions;

/// <summary>
/// Where the geometry readers get client files from. Paths are the pak's own form
/// (<c>game/worlds/&lt;world&gt;/cells/XXX_YYY/client/object.dat</c>, <c>game/objects/…/x.cgf</c>),
/// so an implementation can serve a <c>game_pak</c> archive, an unpacked <c>game</c> folder, or a test fixture
/// without the geometry package knowing about any of them.
/// </summary>
public interface IGeometryFileSource
{
    /// <summary>The file's bytes, or <c>null</c> when the source has no such file.</summary>
    byte[]? TryReadBytes(string path);
}
