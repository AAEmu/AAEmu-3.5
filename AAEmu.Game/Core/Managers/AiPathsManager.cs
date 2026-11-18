using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.AI;
using AAEmu.Game.Models.Game.AI.v2.Controls;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class AiPathsManager : Singleton<AiPathsManager>, IAiPathsManager
{
    private static string PathFileFolder => Path.Combine(FileManager.AppPath, "Data", "Path");
    private const string PathFileExt = ".path";
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly object _lock = new();

    /// <summary>
    /// Cache for loaded Path
    /// </summary>
    private Dictionary<string, List<AiPathPoint>> PathsCache { get; set; } = [];
    private readonly ConcurrentDictionary<string, ZoneAiPathIndex> _designerByZone = new();

    private static string ZoneCacheKey(string worldName, uint zoneKey) =>
        $"{worldName}:{zoneKey.ToString(CultureInfo.InvariantCulture)}";

    public List<AiPathPoint> LoadAiPathPoints(string aiPathFileName)
    {
        if (!PathFollowRules.AllowsLegacyDiskPath(aiPathFileName))
            return [];

        // If cached, return that
        lock (_lock)
        {
            if (PathsCache.TryGetValue(aiPathFileName, out var res))
                return res;
        }

        // Otherwise, try to load from file
        lock (_lock)
        {
            var res = new List<AiPathPoint>();
            try
            {
                var fullPathFileName = Path.Combine(PathFileFolder, aiPathFileName + PathFileExt);
                if (!File.Exists(fullPathFileName))
                    return res;

                var lines = File.ReadAllLines(fullPathFileName);
                // var lines = FileManager.GetFileContents(fullPathFileName);

                foreach (var line in lines)
                {
                    var columns = line.Split('|');
                    if (columns.Length != 5)
                        continue;
                    if (!float.TryParse(columns[1], out var x))
                        x = 0f;
                    if (!float.TryParse(columns[2], out var y))
                        y = 0f;
                    if (!float.TryParse(columns[3], out var z))
                        z = 0f;
                    var param = columns[4];

                    if (!Enum.TryParse<AiPathPointAction>(columns[0], true, out var action))
                        action = AiPathPointAction.None;

                    var newPoint = new AiPathPoint
                    {
                        Position = new Vector3(x, y, z),
                        Action = action,
                        Param = param
                    };

                    res.Add(newPoint);
                }

                PathsCache.TryAdd(aiPathFileName, res);
            }
            catch (Exception e)
            {
                Logger.Error($"LoadAiPathPoint({aiPathFileName}), Exception: {e.Message}");
                res.Clear();
            }
            return res;
        }
    }

    /// <summary>
    /// Загружает AI-пути из текстовых файлов .path в папке Data/Path.
    /// Источник данных: файлы Data/*.path (формат строки: action|x|y|z|param).
    /// Таблицы compact.sqlite3: не используются.
    /// Таблицы compact.server.table.sqlite3: не используются.
    /// </summary>
    public void Load()
    {
        if (!PathFollowRules.LegacyDiskPathEnabled)
        {
            Logger.Info(
                "Designer patrol paths from level_design/zone/*/editor/npc.xml (legacy Data/Path disabled; AAEMU_LEGACY_DISK_PATH=1 to enable)");
            return;
        }

        var pathFiles = Directory.GetFiles(PathFileFolder, "*" + PathFileExt, SearchOption.TopDirectoryOnly);
        var loaded = 0;
        foreach (var pathFile in pathFiles)
        {
            var shortName = Path.GetFileNameWithoutExtension(pathFile);
            if (LoadAiPathPoints(shortName).Count > 0)
                loaded++;
        }
        Logger.Info($"Loaded {loaded} path files from {PathFileFolder}");
    }

    /// <summary>Parse <c>editor/npc.xml</c> for the zone before spawns need patrol data (ArcheageZones slice 5).</summary>
    public void EnsureDesignerZoneLoaded(string worldName, uint zoneKey)
    {
        var key = ZoneCacheKey(worldName, zoneKey);
        var index = _designerByZone.GetOrAdd(key, _ => ZoneAiPathIndex.Load(worldName, zoneKey));
        if (index.Count > 0)
            Logger.Info("zone {0}: {1} designer paths from editor/npc.xml", zoneKey, index.Count);
    }

    public bool TryLoadDesignerPathPoints(string worldName, uint zoneKey, string pathName, Vector3 spawnLocal, int pointNo,
        out List<AiPathPoint> points, out float speed, out bool loops, out uint resolvedZoneKey,
        IReadOnlyCollection<uint>? searchZones = null)
    {
        points = [];
        speed = 1f;
        loops = true;
        resolvedZoneKey = zoneKey;
        if (string.IsNullOrWhiteSpace(pathName))
            return false;

        var index = DesignerIndex(worldName, zoneKey);
        var path = index.Get(pathName);
        if (path == null && PathFollowRules.TryParseZoneKeyFromPathName(pathName, out var pathZone) && pathZone != zoneKey)
        {
            zoneKey = pathZone;
            index = DesignerIndex(worldName, zoneKey);
            path = index.Get(pathName);
        }

        if (path == null && searchZones is { Count: > 0 })
            path = FindPathInZones(worldName, pathName, searchZones, ref zoneKey, ref index);

        if (path == null)
            return false;

        resolvedZoneKey = zoneKey;
        Logger.Debug("designer path {0} zone {1} ({2} points)", pathName, zoneKey, path.Points.Count);
        speed = path.Speed;
        loops = path.Loops;
        var route = path.LaidAt(spawnLocal, pointNo);
        points = new List<AiPathPoint>(route.Count);
        foreach (var p in route)
        {
            points.Add(new AiPathPoint
            {
                Position = p,
                Action = AiPathPointAction.None,
                Param = string.Empty
            });
        }

        return points.Count > 0;
    }

    private ZoneDesignerPath? FindPathInZones(string worldName, string pathName, IReadOnlyCollection<uint> zoneKeys,
        ref uint resolvedZone, ref ZoneAiPathIndex index)
    {
        foreach (var zk in zoneKeys)
        {
            var candidate = _designerByZone.GetOrAdd(ZoneCacheKey(worldName, zk),
                _ => ZoneAiPathIndex.Load(worldName, zk));
            var path = candidate.Get(pathName);
            if (path == null)
                continue;
            resolvedZone = zk;
            index = candidate;
            return path;
        }

        return null;
    }

    private ZoneAiPathIndex DesignerIndex(string worldName, uint zoneKey)
    {
        var key = ZoneCacheKey(worldName, zoneKey);
        return _designerByZone.GetOrAdd(key, _ => ZoneAiPathIndex.Load(worldName, zoneKey));
    }

    /// <summary>
    /// Removes all cached paths
    /// </summary>
    public void ClearCache()
    {
        lock (_lock)
        {
            PathsCache.Clear();
        }

        _designerByZone.Clear();
    }
}
