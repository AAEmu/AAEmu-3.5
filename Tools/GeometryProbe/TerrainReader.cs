using AAEmu.Game.Models.ClientData;

namespace AAEmu.GeometryProbe;

/// <summary>
/// Terrain heights straight from a cell's <c>terrain/heightmap.dat</c>, using the server's own
/// <see cref="Hmap"/>/<see cref="NodeCell"/> readers — the probe has no game world, so it cannot call
/// <c>WorldTemplate.GetHeight</c> (that lazily loads cells and needs the app configuration).
/// <para>
/// Mirrors <c>WorldCell.LoadCellHeightMapFromClientData</c>: nodes sorted by box min, then
/// <c>GetHeight(unitX, unitY)</c> per 32×32 sector, so the numbers are the ones the server sees.
/// </para>
/// </summary>
internal sealed class TerrainReader
{
    public const int CellSize = 1024;
    private const int SectorsPerCell = 16;
    private const int SamplesPerSector = 32;
    public const int Resolution = SectorsPerCell * SamplesPerSector;

    private readonly Dictionary<(int CellX, int CellY), float[]?> _cells = new();
    private readonly Func<string, byte[]?> _read;

    public TerrainReader(Func<string, byte[]?> read) => _read = read;

    /// <summary>Height in metres at a world position, or null when the cell ships no heightmap.</summary>
    public float? Height(float x, float y)
    {
        var cellX = (int)MathF.Floor(x / CellSize);
        var cellY = (int)MathF.Floor(y / CellSize);
        if (cellX < 0 || cellY < 0)
            return null;
        var heights = Cell(cellX, cellY);
        if (heights == null)
            return null;
        var sx = (int)(x - cellX * CellSize) / 2;
        var sy = (int)(y - cellY * CellSize) / 2;
        if (sx < 0 || sy < 0 || sx >= Resolution || sy >= Resolution)
            return null;
        return heights[sy * Resolution + sx];
    }

    private float[]? Cell(int cellX, int cellY)
    {
        if (_cells.TryGetValue((cellX, cellY), out var cached))
            return cached;

        var path = $"game/worlds/main_world/cells/{cellX:000}_{cellY:000}/client/terrain/heightmap.dat";
        var data = _read(path);
        var heights = data == null ? null : Read(data);
        _cells[(cellX, cellY)] = heights;
        return heights;
    }

    private static float[]? Read(byte[] data)
    {
        try
        {
            var hmap = new Hmap();
            using var stream = new MemoryStream(data);
            using var reader = new BinaryReader(stream);
            if (hmap.Read(reader, false) < 0)
                return null;

            var nodes = hmap.Nodes
                .OrderBy(node => node.BoxHeightmap.Min.X)
                .ThenBy(node => node.BoxHeightmap.Min.Y)
                .Where(node => node.pHMData.Length > 0)
                .ToList();
            if (nodes.Count == 0)
                return null;

            var result = new float[Resolution * Resolution];
            for (ushort sectorX = 0; sectorX < SectorsPerCell; sectorX++)
            for (ushort sectorY = 0; sectorY < SectorsPerCell; sectorY++)
            {
                var index = sectorX * SectorsPerCell + sectorY;
                if (index >= nodes.Count)
                    continue;
                var node = nodes[index];
                for (ushort unitX = 0; unitX < SamplesPerSector; unitX++)
                for (ushort unitY = 0; unitY < SamplesPerSector; unitY++)
                {
                    var x = sectorX * SamplesPerSector + unitX;
                    var y = sectorY * SamplesPerSector + unitY;
                    result[y * Resolution + x] = node.GetHeight(unitX, unitY);
                }
            }

            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
