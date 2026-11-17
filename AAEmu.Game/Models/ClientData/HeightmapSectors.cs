namespace AAEmu.Game.Models.ClientData;

/// <summary>
/// The raw samples of a <c>heightmap.dat</c>'s sector nodes, read as the CryEngine terrain stores them.
/// <para>
/// A 64 m sector keeps 33 × 33 samples 2 m apart, or fewer where the terrain is smooth: 17 × 17 (4 m),
/// 9 × 9 (8 m), 5 × 5, 2 × 2. The shared reader (<see cref="NodeCell"/>) "up-scales" such a sector from its first
/// 2 × 2 samples only (its <c>FindNearestSignificantPoints</c> divides the source index by the node size, which is
/// always 0) and then indexes the 33 × 33 result with the old node size, so every low-resolution sector reads as
/// a blend of its corner patch: the heights there are off by up to 6 m on the sea beds, and the units standing
/// there sink under the ground.
/// </para>
/// <para>
/// Native's terrain interpolates the sector's own samples. Ported from ArcheageZones (10.x,
/// <c>Core/Data/HeightmapSectors.cs</c>); the samples come from the already parsed heightmap, so the file is not
/// read twice.
/// </para>
/// </summary>
public static class HeightmapSectors
{
    /// <summary>The sector's samples per side in a full sector.</summary>
    public const int SectorResolution = 32;

    /// <summary>
    /// One sector node: its sample count per side and its heights (row-major by x, as <see cref="NodeCell"/> indexes them).
    /// </summary>
    public sealed record Sector(int Size, float[] Heights)
    {
        /// <summary>
        /// The height at unit (<paramref name="unitX"/>, <paramref name="unitY"/>) of the sector's 32 × 32 two-metre grid:
        /// the sample itself in a full sector, else bilinear between the sector's own samples.
        /// </summary>
        public float At(int unitX, int unitY, int resolution = SectorResolution)
        {
            if (Size <= 1)
                return Heights.Length > 0 ? Heights[0] : 0f;
            if (Size > resolution)
                return Heights[unitX * Size + unitY];
            var span = Size - 1;
            var fx = unitX * span / (float)resolution;
            var fy = unitY * span / (float)resolution;
            var x0 = (int)fx;
            var y0 = (int)fy;
            var x1 = Math.Min(x0 + 1, span);
            var y1 = Math.Min(y0 + 1, span);
            var tx = fx - x0;
            var ty = fy - y0;
            var a = Heights[x0 * Size + y0];
            var b = Heights[x1 * Size + y0];
            var c = Heights[x0 * Size + y1];
            var d = Heights[x1 * Size + y1];
            return (a + (b - a) * tx) * (1f - ty) + (c + (d - c) * tx) * ty;
        }
    }

    /// <summary>
    /// The sector nodes of a parsed heightmap, ordered as the shared reader orders them (by the box's minimum X, then Y),
    /// with the node's own samples (before the up-scale).
    /// </summary>
    public static IReadOnlyList<Sector> Read(Hmap hmap)
    {
        ArgumentNullException.ThrowIfNull(hmap);
        var list = new List<(float MinX, float MinY, Sector Sector)>();
        foreach (var node in hmap.Nodes)
        {
            var raw = node.RawSamples;
            if (raw == null || raw.Length == 0)
                continue;
            var heights = new float[raw.Length];
            for (var i = 0; i < raw.Length; i++)
                heights[i] = node.RawDataToHeight(raw[i]);
            list.Add((node.BoxHeightmap.Min.X, node.BoxHeightmap.Min.Y, new Sector(node.nSize, heights)));
        }

        return list.OrderBy(s => s.MinX).ThenBy(s => s.MinY).Select(s => s.Sector).ToList();
    }
}
