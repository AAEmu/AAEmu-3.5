using System.Collections.Concurrent;
using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.IO;
using AAEmu.Game.Models.CryEngine.Loaders;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Game.World.Xml;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Utils;
using NLog;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Template of a World
/// </summary>
public class WorldTemplate
{
    private static Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// TemplateId for this world
    /// </summary>
    public uint Id { get; set; }

    /// <summary>
    /// World name
    /// </summary>
    public virtual string Name { get; set; }

    /// <summary>
    /// Max height for this world's map data
    /// </summary>
    public float MaxHeight { get; set; }

    /// <summary>
    /// Height Coefficient
    /// </summary>
    public virtual double HeightMaxCoefficient { get; set; }
    /// <summary>
    /// Height of the ocean surface for this world
    /// </summary>
    public float OceanLevel { get; set; } = 100f;
    /// <summary>
    /// World X size in Cells (1024m)
    /// </summary>
    public int CellX { get; set; }
    /// <summary>
    /// World Y size in Cells (1024m)
    /// </summary>
    public int CellY { get; set; }
    /// <summary>
    /// Default spawn location for this world (not used when creating new characters)
    /// </summary>
    public WorldSpawnPosition SpawnPosition { get; set; } = new();

    public WorldCell[,] Cells { get; set; } = new WorldCell[1, 1];
    // <summary>
    // Raw Heightmap data for this world
    // </summary>
    // public virtual ushort[,] HeightMaps { get; set; }

    // <summary>
    // List of what cells have been loaded/processed
    // </summary>
    // public virtual bool[,] LoadedCells { get; set; }

    /// <summary>
    /// Collection of ZoneKeys per Region
    /// </summary>
    public uint[,] ZoneKeyByRegions { get; set; }
    
    /// <summary>
    /// List of levels inside this world (Zone Keys)
    /// </summary>
    public List<uint> ZoneKeys { get; set; } = [];

    /// <summary>
    /// Xml data for this world
    /// </summary>
    public XmlWorld XmlWorld { get; set; } = new();

    /// <summary>
    /// XML Zone data
    /// </summary>
    public ConcurrentDictionary<uint, XmlWorldZone> XmlWorldZones;

    /// <summary>
    /// List of SubZones in this world (zoneId, list)
    /// </summary>
    public Dictionary<uint, List<Area>> SubZones { get; set; } = [];
    /// <summary>
    /// List of housing zones in this world (zoneId, list)
    /// </summary>
    public Dictionary<uint, List<Area>> HousingZones { get; set; } = []; 

    /// <summary>
    /// Handles navmesh data
    /// </summary>
    public AiGeoDataManager GeoData { get; set; }

    /// <summary>
    /// ZoneKey, BaiLoader
    /// </summary>
    public Dictionary<uint, BaseBaiLoader> ZoneBaiLoader { get; init; } = [];
    /// <summary>
    /// (PathX, PathY), BaiLoader
    /// </summary>
    public ConcurrentDictionary<(uint, uint), BaseBaiLoader> PathBaiLoader { get; init; } = new();

    /// <summary>
    /// The coefficient a cell's heights are stored with: the world's one, or the cell's own when the ground is higher
    /// than the world's coefficient can hold (the zone host's rule).
    /// </summary>
    /// <remarks>
    /// The world's coefficient is <c>ushort.MaxValue / (maxTerrainHeight / 4)</c>, which holds 0..1024 m of main_world.
    /// A cell whose ground is higher (the plateau of zone 154, 1100 m) would wrap by 1024 m in a ushort — and the
    /// units standing on it would read 78 m, i.e. under the ground. Such a cell is quantised over its own range.
    /// </remarks>
    /// <param name="maxHeight">The cell's highest sample.</param>
    /// <param name="baseCoefficient">The world's coefficient.</param>
    /// <param name="highTerrain">Whether the high cells are quantised over their own range (on by default).</param>
    public static double CellCoefficient(double maxHeight, double baseCoefficient, bool highTerrain = true)
    {
        if (!highTerrain || maxHeight <= 0f || baseCoefficient <= 0f)
            return baseCoefficient;
        return maxHeight * baseCoefficient > ushort.MaxValue ? ushort.MaxValue / maxHeight : baseCoefficient;
    }

    /// <summary>
    /// Gets heightmap height at target position (not smoothened)
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public float GetRawHeightMapHeight(int x, int y)
    {
        return TryGetRawHeightMapHeight(x, y, out var height) ? height : 0f;
    }

    /// <summary>
    /// Same as <see cref="GetRawHeightMapHeight"/>, but reports whether the position actually has heightmap data.
    /// Height 0 is a legitimate terrain height (sea level areas of main_world), so callers must not read a
    /// returned 0 as "no data".
    /// </summary>
    public bool TryGetRawHeightMapHeight(int x, int y, out float height)
    {
        height = 0f;
        var cellX = x / WorldManager.CELL_SIZE;
        var cellY = y / WorldManager.CELL_SIZE;
        if (cellX < 0 || cellX >= CellX || cellY < 0 || cellY >= CellY)
            return false; // out of bounds
        var cell = Cells[cellX, cellY].VerifyCellLoaded();
        if (cell?.HeightMap == null || !cell.HasHeightMap)
            return false; // no heightmap data for this cell
        var sx = x % WorldManager.CELL_SIZE / 2;
        var sy = y % WorldManager.CELL_SIZE / 2;
        height = cell.GetHeightMapDataInCell(sx, sy);
        return true;
    }

    /// <summary>
    /// Line linear interpolation
    /// </summary>
    /// <param name="start"></param>
    /// <param name="end"></param>
    /// <param name="target">value 0 to 1</param>
    /// <returns></returns>
    private static float Lerp(float start, float end, float target)
    {
        return start + (end - start) * target;
    }

    /// <summary>
    /// Square linear interpolation
    /// </summary>
    /// <param name="cX0Y0">Bottom-Left</param>
    /// <param name="cX1Y0">Bottom-Right</param>
    /// <param name="cX0Y1">Top-Left</param>
    /// <param name="cX1Y1">Top-Right</param>
    /// <param name="tx">value 0 to 1</param>
    /// <param name="ty">value 0 to 1</param>
    /// <returns></returns>
    private static float Blerp(float cX0Y0, float cX1Y0, float cX0Y1, float cX1Y1, float tx, float ty)
    {
        return Lerp(Lerp(cX0Y0, cX1Y0, tx), Lerp(cX0Y1, cX1Y1, tx), ty);
    }

    /// <summary>
    /// Picks the nearest 4 points of a square that contain target position
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    private static System.Drawing.Rectangle FindNearestSignificantPoints(int x, int y)
    {
        return new System.Drawing.Rectangle(x - x % 2, y - y % 2, 2, 2);
    }

    /// <summary>
    /// Gets height at target position using interpolation
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public float GetHeight(float x, float y)
    {
        return TryGetHeight(x, y, out var height) ? height : 0f;
    }

    /// <summary>
    /// Same as <see cref="GetHeight(float, float)"/>, but reports whether the map had data under the position.
    /// A return value of 0 is a legitimate terrain height, so "no data" must be distinguished from it.
    /// </summary>
    public bool TryGetHeight(float x, float y, out float height)
    {
        height = 0f;

        // Get bordering points
        var border = FindNearestSignificantPoints((int)Math.Floor(x), (int)Math.Floor(y));

        // All four corners come from the same cell grid: if one has no data, the interpolation has none either.
        if (!TryGetRawHeightMapHeight(border.Left, border.Top, out var heightTl) ||
            !TryGetRawHeightMapHeight(border.Right, border.Top, out var heightTr) ||
            !TryGetRawHeightMapHeight(border.Left, border.Bottom, out var heightBl) ||
            !TryGetRawHeightMapHeight(border.Right, border.Bottom, out var heightBr))
            return false;

        var offX = (x - border.Left) / 2;
        var offY = (y - border.Top) / 2;
        height = Blerp(heightTl, heightTr, heightBl, heightBr, offX, offY); // bilinear interpolation
        return true;
    }

    /// <summary>
    /// Checks if target sector offset is within the world's bounds
    /// </summary>
    /// <param name="sectorX"></param>
    /// <param name="sectorY"></param>
    /// <returns></returns>
    public bool ValidRegion(int sectorX, int sectorY)
    {
        return sectorX >= 0 && sectorX < CellX * WorldManager.SECTORS_PER_CELL && sectorY >= 0 && sectorY < CellY * WorldManager.SECTORS_PER_CELL;
    }

    /// <summary>
    /// Gets target cell
    /// </summary>
    /// <param name="cellX"></param>
    /// <param name="cellY"></param>
    /// <returns>Returns the cell, or null if the given index is out of bounds for this world</returns>
    public WorldCell GetCell(int cellX, int cellY)
    {
        if (cellX < 0 || cellX > CellX || cellY < 0 || cellY > CellY)
            return null;
        return Cells[cellX, cellY];
    }

    public void LoadZoneBaiFiles()
    {
        if (!AppConfiguration.Instance.World.GeoDataMode)
            return; // Don't load navmesh if GeoDataMode is disabled

        foreach (var zoneKey in ZoneKeys)
        {
            var worldFolder = Path.Combine("game", "worlds", Name, "zone", zoneKey.ToString());
            var baiFilesList = ClientFileManager.GetFilesInDirectory(worldFolder, "*.bai", false).ToArray();
            if (baiFilesList.Length <= 0)
                continue;

            var zoneBaiLoader = new BaseBaiLoader(this);
            zoneBaiLoader.LoadBaiFilesFromFolder(zoneKey.ToString());
            ZoneBaiLoader.Add(zoneKey, zoneBaiLoader);
        }
    }

    public BaseBaiLoader GetBaiByPos(Vector3 pos)
    {
        if (ZoneBaiLoader.Count > 0)
            return ZoneBaiLoader.Values.First(); // TODO: Pick the actually correct zone

        // First verify if target cell is loaded
        var cellPos = pos.ToCellIndex();
        var cell = Cells[cellPos.Item1, cellPos.Item2];
        cell.VerifyCellLoaded();
        // Return value from the main paths dictionary
        var pathsPos = pos.ToPathsIndex();
        return PathBaiLoader.GetValueOrDefault(((uint)pathsPos.Item1, (uint)pathsPos.Item2));
    }
}
