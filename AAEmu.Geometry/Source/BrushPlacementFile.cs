using System.Numerics;
using System.Text;

using AAEmu.Geometry.Source;

namespace AAEmu.Geometry;

/// <summary>
/// The brushes (static geometry placements, object type 1) of one cell's object file (plan slice 6). Three files share
/// the layout the Game's <c>ObjectsFile</c> reads: <c>u32 assetCount</c>, <c>assetCount × {u32, char[256] path}</c>, an
/// optional prefab list (<c>u32 count</c>, <c>count × 260</c>), then one or more octree nodes
/// (<c>i32, Vec3 min, Vec3 max, i32 blockSize, u8 childMask</c>, the block, then the children in mask order) whose
/// blocks are packed object records.
/// <list type="bullet">
/// <item><c>cells/XXX_YYY/world_server/object_indoor.x2d</c> — the server's indoor brushes (Burnt Castle: 1 077 in 11
/// trees), no prefab list. Only instance worlds ship one in this client.</item>
/// <item><c>cells/XXX_YYY/world_server/object.x2d</c> / <c>client/object.dat</c> — the outdoor objects, with the prefab
/// list (main_world ships only <c>client/</c>; the two are byte-equal where both exist).</item>
/// </list>
/// Records: type 1 brush (132 bytes: bbox at +4, <c>Matrix34</c> at +71 — rows of rotation·scale with the translation
/// last, column-vector convention — asset index at +127), 2 vegetation (68), 4 (100), 5 (124), 8 (196), 9 decal (115),
/// 14 (83), 27 (652), 11 water (<c>0x7B + 12 × (shape + contour points)</c>), 13 road (<c>67 + 12 × points</c>). A
/// record of another type ends its block (none in the zones read so far). Positions are cell-local metres.
/// </summary>
public static class BrushPlacementFile
{
    private const int AssetEntrySize = 260;
    private const int NodeHeaderSize = 33;
    private const int BrushSize = 132;

    /// <summary>One brush: the asset path as the file names it and its placement (object frame → cell-local), row-vector form.</summary>
    public readonly record struct Brush(string AssetPath, Matrix4x4 ToCell);

    public static IReadOnlyList<Brush> Read(byte[] data)
    {
        if (data.Length < 8)
            return [];
        var assetCount = BitConverter.ToInt32(data, 0);
        var afterAssets = 4L + (long)assetCount * AssetEntrySize;
        if (assetCount < 0 || afterAssets > data.Length)
            throw new InvalidDataException("asset list runs past the file");

        var assets = new string[assetCount];
        for (var i = 0; i < assetCount; i++)
        {
            var name = data.AsSpan(8 + i * AssetEntrySize, AssetEntrySize - 4);
            var end = name.IndexOf((byte)0);
            assets[i] = Encoding.ASCII.GetString(end < 0 ? name : name[..end]);
        }

        // With or without the prefab list: take the layout whose node trees end exactly at the end of the file.
        var start = (int)afterAssets;
        if (TryWalk(data, start, assets, out var brushes))
            return brushes;
        if (start + 4 <= data.Length)
        {
            var prefabs = BitConverter.ToInt32(data, start);
            var afterPrefabs = start + 4L + (long)prefabs * AssetEntrySize;
            if (prefabs >= 0 && afterPrefabs <= data.Length && TryWalk(data, (int)afterPrefabs, assets, out brushes))
                return brushes;
        }

        throw new InvalidDataException("object trees do not parse to the end of the file");
    }

    /// <summary>Size of a vis area / portal / occluder record before its shape points (version i32 at +0, object block size at +84, point count at +276).</summary>
    private const int VisAreaFixedSize = 280;

    /// <summary>
    /// The brushes of a cell's <c>client/visareas.dat</c>: the indoor objects (caves, halls, house interiors) that
    /// <c>object.dat</c> leaves out. Layout: the asset list (as in <see cref="Read"/>), a second 260-byte list (materials),
    /// a 20-byte header (<c>u8 version 4, 3 pad, i32 chunk size from the header to the end of the file, i32 vis areas,
    /// i32 portals, i32 occluders</c>), then per area a 280-byte record (<c>i32 version, 2 boxes, char[32] name, i32 object
    /// block size, …, i32 point count</c>), its shape points (12 bytes each) and its object block: octree nodes as in
    /// <c>object.dat</c>, whose asset indices name the first list. Where an instance world ships both, the concatenated
    /// object blocks are <c>world_server/object_indoor.x2d</c>'s trees byte for byte (backup_w_mirror_kingdom 000_000).
    /// main_world ships only this file, so its underground halls (zone 186's halls 40–75 m under the terrain) had no floor.
    /// </summary>
    public static IReadOnlyList<Brush> ReadVisAreas(byte[] data)
    {
        if (data.Length < 8)
            return [];
        var assetCount = BitConverter.ToInt32(data, 0);
        var p = 4L + (long)assetCount * AssetEntrySize;
        if (assetCount < 0 || p + 4 > data.Length)
            throw new InvalidDataException("asset list runs past the file");
        var assets = new string[assetCount];
        for (var i = 0; i < assetCount; i++)
        {
            var name = data.AsSpan(8 + i * AssetEntrySize, AssetEntrySize - 4);
            var nul = name.IndexOf((byte)0);
            assets[i] = Encoding.ASCII.GetString(nul < 0 ? name : name[..nul]);
        }

        var materials = BitConverter.ToInt32(data, (int)p);
        p += 4L + (long)materials * AssetEntrySize;
        if (materials < 0 || p + 20 > data.Length)
            throw new InvalidDataException("material list runs past the file");
        var header = (int)p;
        var end = header + (long)BitConverter.ToInt32(data, header + 4);
        var areas = (long)BitConverter.ToInt32(data, header + 8) + BitConverter.ToInt32(data, header + 12) + BitConverter.ToInt32(data, header + 16);
        if (end != data.Length || areas < 0)
            throw new InvalidDataException("vis area chunk does not end at the end of the file");
        p = header + 20;
        var brushes = new List<Brush>();
        for (var i = 0; i < areas; i++)
        {
            if (p + VisAreaFixedSize > end)
                throw new InvalidDataException("vis area record runs past the file");
            var version = BitConverter.ToInt32(data, (int)p);
            var block = BitConverter.ToInt32(data, (int)p + 84);
            var points = BitConverter.ToInt32(data, (int)p + 276);
            p += VisAreaFixedSize + 12L * points;
            if (block < 0 || points < 0 || p + block > end)
                throw new InvalidDataException($"vis area {i} (version {version}) objects run past the file");
            if (block > 0 && !TryWalk(data, (int)p, (int)(p + block), assets, brushes))
                throw new InvalidDataException($"vis area {i} (version {version}) object trees do not parse to the end of their block");
            p += block;
            if (version >= 4)
                p += 4;
        }

        if (p != end)
            throw new InvalidDataException("vis areas do not end at the end of the file");
        return brushes;
    }

    /// <summary>
    /// The water volumes (object type 11) of an object file laid out as <see cref="Read"/> reads it: rivers, lakes and
    /// ponds over the terrain, with their contour, surface (the higher end of the volume) and depth. Positions as the
    /// file holds them (cell-local metres for main_world's cells).
    /// </summary>
    public static IReadOnlyList<WaterVolumeRecord> ReadWater(byte[] data)
    {
        if (data.Length < 8)
            return [];
        var assetCount = BitConverter.ToInt32(data, 0);
        var afterAssets = 4L + (long)assetCount * AssetEntrySize;
        if (assetCount < 0 || afterAssets > data.Length)
            throw new InvalidDataException("asset list runs past the file");
        var assets = new string[assetCount];
        Array.Fill(assets, string.Empty);
        var start = (int)afterAssets;
        var water = new List<WaterVolumeRecord>();
        if (TryWalk(data, start, data.Length, assets, [], water))
            return water;
        water.Clear();
        if (start + 4 <= data.Length)
        {
            var prefabs = BitConverter.ToInt32(data, start);
            var afterPrefabs = start + 4L + (long)prefabs * AssetEntrySize;
            if (prefabs >= 0 && afterPrefabs <= data.Length && TryWalk(data, (int)afterPrefabs, data.Length, assets, [], water))
                return water;
        }

        throw new InvalidDataException("object trees do not parse to the end of the file");
    }

    private static bool TryWalk(byte[] data, int offset, string[] assets, out List<Brush> brushes)
    {
        brushes = [];
        return TryWalk(data, offset, data.Length, assets, brushes);
    }

    private static bool TryWalk(byte[] data, int offset, int end, string[] assets, List<Brush> brushes, List<WaterVolumeRecord>? water = null)
    {
        try
        {
            while (offset + NodeHeaderSize <= end)
            {
                var next = Node(data, offset, assets, brushes, 0, water);
                if (next <= offset)
                    return false;
                offset = next;
            }

            return offset == end;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static int Node(byte[] data, int offset, string[] assets, List<Brush> brushes, int depth, List<WaterVolumeRecord>? water = null)
    {
        if (depth > 64 || offset + NodeHeaderSize > data.Length)
            throw new InvalidDataException("bad node");
        var size = BitConverter.ToInt32(data, offset + 28);
        var mask = data[offset + 32];
        var p = offset + NodeHeaderSize;
        var end = p + (long)size;
        if (size < 0 || end > data.Length)
            throw new InvalidDataException("node block runs past the file");
        while (p + 4 <= end)
        {
            var type = BitConverter.ToInt32(data, p);
            var length = RecordSize(data, p, type, (int)end);
            if (length <= 0)
                break;
            if (type == 11 && water != null)
            {
                // Its own copy: the reader walks the array with LINQ from the start, so a whole-file array costs O(offset).
                var volume = new WaterVolumeRecord();
                if (volume.ReadData(data.AsSpan(p, length).ToArray(), 0) > 0)
                    water.Add(volume);
            }

            if (type == 1)
            {
                var index = BitConverter.ToInt32(data, p + 127);
                if (index >= 0 && index < assets.Length)
                    brushes.Add(new Brush(assets[index], Placement(data, p + 71)));
            }

            p += length;
        }

        p = (int)end;
        for (var i = 0; i < 8; i++)
        {
            if ((mask & (1 << i)) != 0)
                p = Node(data, p, assets, brushes, depth + 1, water);
        }

        return p;
    }

    private static int RecordSize(byte[] data, int p, int type, int end)
    {
        var size = type switch
        {
            1 => BrushSize,
            2 => 68,
            4 => 100,
            5 => 124,
            8 => 196,
            9 => 115,
            14 => 83,
            27 => 652,
            11 when p + 0x7B <= end => 0x7B + 12 * (Math.Max(0, BitConverter.ToInt32(data, p + 0x6B)) + Math.Max(0, BitConverter.ToInt32(data, p + 0x77))),
            13 when p + 67 <= end => 67 + 12 * data[p + 43],
            _ => 0
        };
        return p + size <= end ? size : 0;
    }

    /// <summary>The record's 3×4 (column-vector rows <c>R|T</c>) as a row-vector <see cref="Matrix4x4"/>.</summary>
    private static Matrix4x4 Placement(byte[] data, int o)
    {
        float F(int i) => BitConverter.ToSingle(data, o + 4 * i);
        return new Matrix4x4(
            F(0), F(4), F(8), 0f,
            F(1), F(5), F(9), 0f,
            F(2), F(6), F(10), 0f,
            F(3), F(7), F(11), 1f);
    }
}
