#nullable enable
using AAEmu.Geometry.Abstractions;
using NLog;

using Geometry = AAEmu.Geometry;

namespace AAEmu.Game.Physics.Static;

/// <summary>
/// Клиентские файлы для геометрического слоя пакета <see cref="IGeometryFileSource"/>:
/// через <see cref="AAEmu.Game.IO.ClientFileManager"/> (AAPak, чтение на месте, без распаковки).
/// </summary>
public sealed class ClientFileCollisionAssetSource : IGeometryFileSource
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public byte[]? TryReadBytes(string path)
    {
        try
        {
            if (!AAEmu.Game.IO.ClientFileManager.FileExists(path))
                return null;
            using var stream = AAEmu.Game.IO.ClientFileManager.GetFileStream(path);
            if (stream == null)
                return null;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "collision asset {0} unreadable", path);
            return null;
        }
    }
}

/// <summary>
/// Чтение клиентских файлов из распакованного дерева <c>game</c> на диске (для тестов и офлайн-разбора).
/// </summary>
public sealed class DirectoryCollisionAssetSource : IGeometryFileSource
{
    private readonly string _root;

    public DirectoryCollisionAssetSource(string root) => _root = root;

    public byte[]? TryReadBytes(string path)
    {
        var relative = path.Replace('/', Path.DirectorySeparatorChar);
        if (relative.StartsWith("game" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            relative = relative["game".Length..].TrimStart(Path.DirectorySeparatorChar);
        var full = Path.Combine(_root, relative);
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
    }
}

/// <summary>
/// Мост логов пакета в NLog: пакет не зависит от логирования, поэтому хост связывает их один раз.
/// </summary>
public static class GeometryLogBridge
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>Направляет логи геометрического пакета в NLog (идемпотентно).</summary>
    public static void Attach()
    {
        Geometry.GeometryLog.Sink ??= (level, message, exception) =>
        {
            switch (level)
            {
                case Geometry.GeometryLogLevel.Debug:
                    Logger.Debug(message);
                    break;
                case Geometry.GeometryLogLevel.Info:
                    Logger.Info(message);
                    break;
                case Geometry.GeometryLogLevel.Warn:
                    Logger.Warn(exception, message);
                    break;
                default:
                    Logger.Error(exception, message);
                    break;
            }
        };
    }
}
