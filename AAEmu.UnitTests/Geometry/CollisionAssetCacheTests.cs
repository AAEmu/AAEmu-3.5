using System.Text;

using AAEmu.Geometry;
using AAEmu.Geometry.Abstractions;
using AAEmu.Geometry.Source;

namespace AAEmu.UnitTests.Geometry;

/// <summary>
/// The cache's source selection (plan P1): proxy, render, or proxy with a render fallback. The fixtures are
/// synthesised .cgf files, so the tests need no client data.
/// </summary>
public class CollisionAssetCacheTests
{
    private sealed class MemoryFiles(Dictionary<string, byte[]> files) : IGeometryFileSource
    {
        public byte[]? TryReadBytes(string path) => files.GetValueOrDefault(path);
    }

    [Test]
    public async Task ProxyMode_GivesNoCollisionToAnAssetWithoutOne()
    {
        var cache = new CollisionAssetCache();
        var files = new MemoryFiles(new() { ["empty.cgf"] = CgfFixture.Empty() });

        await Assert.That(cache.Get(files, "empty.cgf")).IsNull();
    }

    [Test]
    public async Task ProxyMode_RejectsAFileThatIsNotACgf()
    {
        var cache = new CollisionAssetCache();
        var files = new MemoryFiles(new() { ["data.bin"] = Encoding.ASCII.GetBytes("not a cgf at all, really") });

        await Assert.That(cache.Get(files, "data.bin")).IsNull();
    }

    [Test]
    public async Task MissingFile_IsRememberedAsNoCollision()
    {
        var cache = new CollisionAssetCache();
        var files = new MemoryFiles(new());
        var reads = cache.Reads;

        await Assert.That(cache.Get(files, "absent.cgf")).IsNull();
        await Assert.That(cache.Get(files, "absent.cgf")).IsNull();
        await Assert.That(cache.Reads).IsEqualTo(reads + 1);
    }

    [Test]
    public async Task BoxProxy_BecomesATwelveTriangleBox()
    {
        var cache = new CollisionAssetCache();
        var files = new MemoryFiles(new() { ["box.cgf"] = CgfFixture.WithBoxProxy() });

        var mesh = cache.Get(files, "box.cgf");

        await Assert.That(mesh).IsNotNull();
        await Assert.That(mesh!.TriangleCount).IsEqualTo(12);
        await Assert.That(mesh.Max.X - mesh.Min.X).IsEqualTo(2f).Within(1e-4f);
    }

    [Test]
    public async Task ProxyThenRender_FallsBackToTheRenderMesh()
    {
        var cache = new CollisionAssetCache();
        var files = new MemoryFiles(new() { ["render.cgf"] = CgfFixture.WithRenderOnly() });

        var mesh = cache.Get(files, "render.cgf");

        await Assert.That(mesh).IsNotNull();
        await Assert.That(mesh!.TriangleCount).IsEqualTo(1);
    }

    [Test]
    public async Task AnAssetNamesItsMaterialAndTheProxyItsSurface()
    {
        // Native reads the surface of a part from the material file the asset names and the sub-material the proxy
        // points at; the mesh has to carry both.
        var cache = new CollisionAssetCache();
        var files = new MemoryFiles(new() { ["game/objects/env/rock.cgf"] = CgfFixture.WithBoxProxyAndMaterial("mat_rock", 1) });

        var mesh = cache.Get(files, "game/objects/env/rock.cgf");

        await Assert.That(mesh).IsNotNull();
        await Assert.That(mesh!.Material).IsEqualTo("game/objects/env/mat_rock.mtl");
        await Assert.That(mesh.MaterialSlot).IsEqualTo(1);
    }

    [Test]
    public async Task RenderMode_ReadsTheRenderMeshOfAnAssetThatHasAProxy()
    {
        var cache = new CollisionAssetCache();
        var files = new MemoryFiles(new() { ["box.cgf"] = CgfFixture.WithBoxProxy() });

        // The fixture has a proxy but no render streams, so render-only mode finds nothing to collide with.
        await Assert.That(cache.Get(files, "box.cgf", GeometryPhysicsSource.Render)).IsNull();
    }

    [Test]
    public async Task ProxyOnlyMode_IgnoresTheRenderMesh()
    {
        var cache = new CollisionAssetCache();
        var files = new MemoryFiles(new() { ["render.cgf"] = CgfFixture.WithRenderOnly() });

        await Assert.That(cache.Get(files, "render.cgf", GeometryPhysicsSource.Proxy)).IsNull();
    }

    [Test]
    public async Task RenderMode_IgnoresTheProxyOfAnAssetThatHasBoth()
    {
        var cache = new CollisionAssetCache();
        var files = new MemoryFiles(new() { ["both.cgf"] = CgfFixture.WithBoxProxyAndRender() });

        var mesh = cache.Get(files, "both.cgf", GeometryPhysicsSource.Render);

        // The proxy is a 12-triangle box; the render mesh is the single triangle of the fixture.
        await Assert.That(mesh).IsNotNull();
        await Assert.That(mesh!.TriangleCount).IsEqualTo(1);
    }

    [Test]
    public async Task ProxyThenRender_PrefersTheProxyWhenThereIsOne()
    {
        var cache = new CollisionAssetCache();
        var files = new MemoryFiles(new() { ["both.cgf"] = CgfFixture.WithBoxProxyAndRender() });

        var mesh = cache.Get(files, "both.cgf", GeometryPhysicsSource.ProxyThenRender);

        await Assert.That(mesh).IsNotNull();
        await Assert.That(mesh!.TriangleCount).IsEqualTo(12);
    }
}

/// <summary>Minimal .cgf files: a chunk table plus the chunks a reader needs.</summary>
internal static class CgfFixture
{
    private const uint ChunkMesh = 0xCCCC0000;
    private const uint ChunkPhysics = 0xCCCC0018;
    private const uint ChunkStream = 0xCCCC0016;
    private const uint ChunkNode = 0xCCCC000B;
    private const uint ChunkMaterial = 0xCCCC0014;

    /// <summary>A header and an empty chunk table: a cgf with nothing in it.</summary>
    public static byte[] Empty() => Header([]);

    /// <summary>One mesh chunk whose proxy is a 2×2×2 box centred on the origin (no render streams).</summary>
    public static byte[] WithBoxProxy()
    {
        var physics = BoxPhysics();
        var mesh = Mesh(physicsId: 2, positionsId: 0, indicesId: 0);
        return Header([(ChunkMesh, 1, mesh), (ChunkPhysics, 2, physics)]);
    }

    /// <summary>
    /// One mesh chunk whose proxy is a box and whose node names a material: the case the surface types need.
    /// The node chunk names the material chunk at +76, the material's name sits at +8, and the proxy's
    /// phys_geometry carries its surface index at +24 + 52.
    /// </summary>
    public static byte[] WithBoxProxyAndMaterial(string material = "mat_rock", int surfaceIndex = 1)
    {
        var physics = BoxPhysics(surfaceIndex);
        var mesh = Mesh(physicsId: 2, positionsId: 0, indicesId: 0);
        // Node chunk: name[64], object id at +64, the material chunk's id at +76, the transform at +84.
        var node = new byte[148];
        BitConverter.GetBytes(1).CopyTo(node, 64);   // the object id the mesh carries
        BitConverter.GetBytes(6).CopyTo(node, 76);   // the material chunk's id
        BitConverter.GetBytes(1f).CopyTo(node, 84);   // identity transform (row-vector 3x4)
        BitConverter.GetBytes(1f).CopyTo(node, 104);
        BitConverter.GetBytes(1f).CopyTo(node, 124);
        // Material chunk: flags, a reserved word, then the name (128 bytes).
        var mtl = new byte[8 + 128];
        System.Text.Encoding.ASCII.GetBytes(material).CopyTo(mtl, 8);
        return Header([(ChunkMesh, 1, mesh), (ChunkPhysics, 2, physics), (ChunkNode, 5, node), (ChunkMaterial, 6, mtl)]);
    }

    /// <summary>One mesh chunk with both a box proxy and render streams: the case the mode switch decides.</summary>
    public static byte[] WithBoxProxyAndRender()
    {
        var physics = BoxPhysics();
        var positions = FloatStream(0, [0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f]);
        var indices = UShortStream(5, [0, 1, 2]);
        var mesh = Mesh(physicsId: 2, positionsId: 4, indicesId: 5);
        return Header([(ChunkMesh, 1, mesh), (ChunkPhysics, 2, physics), (ChunkStream, 4, positions), (ChunkStream, 5, indices)]);
    }

    /// <summary>One mesh chunk with render streams and no proxy: the asset the fallback exists for.</summary>
    public static byte[] WithRenderOnly()
    {
        var positions = FloatStream(0, [0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f]);
        var indices = UShortStream(5, [0, 1, 2]);
        // Stream ids must not collide with the mesh chunk's own id: a repeated id is dropped by the chunk map.
        var mesh = Mesh(physicsId: 0, positionsId: 4, indicesId: 5);
        return Header([(ChunkMesh, 3, mesh), (ChunkStream, 4, positions), (ChunkStream, 5, indices)]);
    }

    private static byte[] Header(List<(uint Type, int Id, byte[] Body)> chunks)
    {
        const int tableOffset = 20;
        const int stride = 16;
        var total = tableOffset + 4 + chunks.Count * stride;
        foreach (var chunk in chunks)
            total += chunk.Body.Length;

        var data = new byte[total];
        "CryTek"u8.CopyTo(data);
        Write(data, 8, 1u);                      // file type
        Write(data, 12, 0x744u);                 // version
        Write(data, 16, (uint)tableOffset);
        Write(data, tableOffset, (uint)chunks.Count);

        var offset = tableOffset + 4 + chunks.Count * stride;
        for (var i = 0; i < chunks.Count; i++)
        {
            var e = tableOffset + 4 + i * stride;
            Write(data, e, chunks[i].Type);
            Write(data, e + 4, 1u);              // version
            Write(data, e + 8, (uint)offset);
            Write(data, e + 12, (uint)chunks[i].Id);
            chunks[i].Body.CopyTo(data, offset);
            offset += chunks[i].Body.Length;
        }

        return data;
    }

    /// <summary>
    /// Mesh chunk as the readers index it: stream ids at <c>+28 + 4 * stream</c> (so stream 0 is at +28), and slot 0
    /// of the physics ids at <c>+28 + 64</c>. The header is 28 bytes, hence the ids start there.
    /// </summary>
    private static byte[] Mesh(int physicsId, int positionsId, int indicesId)
    {
        // 28-byte header, 16 stream ids (stream 5 at +28+20), then 4 physics ids (slot 0 at +28+64).
        var body = new byte[28 + 64 + 16];
        Write(body, 28 + 4 * 0, (uint)positionsId);
        Write(body, 28 + 4 * 5, (uint)indicesId);
        Write(body, 28 + 64, (uint)physicsId);
        return body;
    }

    private static byte[] FloatStream(int type, float[] values)
    {
        var body = new byte[24 + values.Length * 4];
        Write(body, 4, (uint)type);
        Write(body, 8, (uint)(values.Length / 3));
        Write(body, 12, 12);
        Buffer.BlockCopy(values, 0, body, 24, values.Length * 4);
        return body;
    }

    private static byte[] UShortStream(int type, ushort[] values)
    {
        var body = new byte[24 + values.Length * 2];
        Write(body, 4, (uint)type);
        Write(body, 8, (uint)values.Length);
        Write(body, 12, 2);
        for (var i = 0; i < values.Length; i++)
            BitConverter.GetBytes(values[i]).CopyTo(body, 24 + 2 * i);
        return body;
    }

    /// <summary>Physics chunk: 24-byte chunk header, then phys_geometry with type 0 (box) at +68.</summary>
    private static byte[] BoxPhysics(int surfaceIndex = 0)
    {
        const int size = 72 + 64;
        var body = new byte[24 + size];
        Write(body, 0, (uint)size);
        const int g = 24;
        Write(body, g + 68, 0);                              // GeomBox
        Write(body, g + 52, surfaceIndex);                   // the proxy's surface index (phys_geometry + 52)
        const int p = g + 72;
        Write(body, p + 0, 1f); Write(body, p + 16, 1f); Write(body, p + 32, 1f); // basis: unit axes
        Write(body, p + 36, 0);                              // not oriented
        Write(body, p + 40, 0f); Write(body, p + 44, 0f); Write(body, p + 48, 0f); // centre
        Write(body, p + 52, 1f); Write(body, p + 56, 1f); Write(body, p + 60, 1f); // half sizes
        return body;
    }

    private static void Write(byte[] data, int offset, uint value) => BitConverter.GetBytes(value).CopyTo(data, offset);
    private static void Write(byte[] data, int offset, int value) => BitConverter.GetBytes(value).CopyTo(data, offset);
    private static void Write(byte[] data, int offset, float value) => BitConverter.GetBytes(value).CopyTo(data, offset);
}

/// <summary>
/// Minimal <c>object.dat</c> files: the asset list, an empty prefab list, then one octree leaf holding the brushes.
/// Layout as <c>BrushPlacementFile</c> reads it: <c>u32 assetCount</c>, <c>260 B</c> per asset, then the node tree.
/// </summary>
internal static class ObjectDat
{
    /// <summary>One brush per position, each naming <paramref name="asset"/> and scaled 1:1.</summary>
    public static byte[] WithBoxBrushes(string asset, IReadOnlyList<System.Numerics.Vector3> positions)
    {
        var assets = new byte[4 + 260];
        BitConverter.GetBytes(1).CopyTo(assets, 0);
        var name = System.Text.Encoding.ASCII.GetBytes(asset);
        name.CopyTo(assets, 8);

        // Node header: i32, min (3 floats), max (3 floats), i32 blockSize, u8 childMask = 33 bytes.
        var block = new byte[positions.Count * 132];
        for (var i = 0; i < positions.Count; i++)
        {
            var p = i * 132;
            BitConverter.GetBytes(1).CopyTo(block, p);                      // type 1: brush
            var pos = positions[i];
            BitConverter.GetBytes(pos.X).CopyTo(block, p + 4);              // start pos
            BitConverter.GetBytes(pos.Y).CopyTo(block, p + 8);
            BitConverter.GetBytes(pos.Z).CopyTo(block, p + 12);

            // Matrix34 at +71, rows of rotation·scale with the translation last (column-vector convention).
            // Identity rotation, 1:1 scale, translation in the last row.
            for (var row = 0; row < 3; row++)
                for (var col = 0; col < 3; col++)
                    BitConverter.GetBytes(row == col ? 1f : 0f).CopyTo(block, p + 71 + 4 * (row * 4 + col));
            BitConverter.GetBytes(pos.X).CopyTo(block, p + 71 + 4 * 3);
            BitConverter.GetBytes(pos.Y).CopyTo(block, p + 71 + 4 * 7);
            BitConverter.GetBytes(pos.Z).CopyTo(block, p + 71 + 4 * 11);

            BitConverter.GetBytes(0).CopyTo(block, p + 127);                // asset index 0
        }

        var node = new byte[33 + block.Length];
        BitConverter.GetBytes(1).CopyTo(node, 0);                           // node type
        BitConverter.GetBytes(block.Length).CopyTo(node, 28);               // block size
        node[32] = 0;                                                       // no children
        block.CopyTo(node, 33);
        // The reader's block ends at node start + 33 + size, so blockSize must cover exactly the records.
        if (node.Length != 33 + block.Length)
            throw new InvalidOperationException("node layout");

        var data = new byte[assets.Length + node.Length];
        assets.CopyTo(data, 0);
        node.CopyTo(data, assets.Length);
        return data;
    }
}
