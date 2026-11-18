using System.Numerics;
using AAEmu.Game.IO;
using AAEmu.Game.Models.CryEngine.Readers;

namespace AAEmu.Game.Core.Managers.Bots.Navigation;

/// <summary>Reads the raw mesh of one block. Called on planner threads only.</summary>
public interface INavBlockSource
{
    /// <summary>
    /// The raw data of block (<paramref name="blockX"/>, <paramref name="blockY"/>) of world
    /// <paramref name="worldName"/>, or null when the world has none there. Throws when the files are unreadable.
    /// </summary>
    NavBlockData Load(string worldName, int blockX, int blockY);
}

/// <summary>
/// Reads blocks through the client sources the server already uses (<c>ClientData.Sources</c>: the game pak or an
/// unpacked game folder), from <c>game/worlds/&lt;world&gt;/paths/XXX_YYY/</c>, with the server's own .bai readers.
/// </summary>
public sealed class ClientNavBlockSource : INavBlockSource
{
    public NavBlockData Load(string worldName, int blockX, int blockY) =>
        NavBlockFiles.Read(blockX, blockY, file => ClientFileManager.GetFileStream(
            Path.Combine("game", "worlds", worldName, "paths", NavBlockFiles.FolderName(blockX, blockY), file)));
}

/// <summary>
/// Reads blocks from an unpacked folder of world folders (<c>&lt;folder&gt;/&lt;world&gt;/paths/XXX_YYY/</c>): the
/// test fixtures, or a loose client copy set in <c>Navigation.WorldsFolder</c>. Files are opened read-only.
/// </summary>
public sealed class FolderNavBlockSource(string worldsFolder) : INavBlockSource
{
    public string WorldsFolder { get; } = worldsFolder ?? "";

    public NavBlockData Load(string worldName, int blockX, int blockY) =>
        NavBlockFiles.Read(blockX, blockY, file =>
        {
            var path = Path.Combine(WorldsFolder, worldName, "paths", NavBlockFiles.FolderName(blockX, blockY), file);
            return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
        });
}

/// <summary>The two files of a block and how they are read.</summary>
public static class NavBlockFiles
{
    public const string NetFile = "netmission0.bai";
    public const string VertexFile = "vertsmission0.bai";

    public static string FolderName(int blockX, int blockY) => $"{blockX:000}_{blockY:000}";

    /// <summary>
    /// Reads a block with <paramref name="open"/> (file name to stream, null when missing): null without a
    /// <see cref="NetFile"/>; points are moved to world coordinates by the block's offset, as the server's loader does.
    /// </summary>
    public static NavBlockData Read(int blockX, int blockY, Func<string, System.IO.Stream> open)
    {
        if (blockX < 0 || blockY < 0)
            return null;
        var offset = new Vector3(blockX * NavBlock.Size, blockY * NavBlock.Size, 0f);

        using var netStream = open(NetFile);
        if (netStream == null)
            return null;
        var net = new NetMissionReader(netStream, 0) { ReaderPointOffset = offset };
        net.ReadFile();

        using var vertexStream = open(VertexFile);
        VertexMissionReader vertices = null;
        if (vertexStream != null)
        {
            vertices = new VertexMissionReader(vertexStream, 0) { ReaderPointOffset = offset };
            vertices.ReadFile();
        }

        return NavBlockData.FromReaders(net, vertices);
    }
}
