using System.Numerics;
using System.Text;

namespace AAEmu.Geometry.Source;

/// <summary>
/// The render geometry of a CryEngine <c>.cgf</c>/<c>.cga</c>: every mesh chunk's position and index streams,
/// with the same node chain and <c>$lod</c> rules <see cref="CgfPhysicsReader"/> uses.
/// <para>
/// This is the reference the collision proxies are checked against (plan P1): the proxy is what the native
/// dedicate physicalises, the render mesh is what the player sees. Where an asset has no proxy, the render
/// mesh is the only geometry left, so <see cref="StaticCollisionOptions.PhysicsSource"/> can fall back to it.
/// </para>
/// </summary>
public static class CgfRenderReader
{
    private const uint ChunkMesh = 0xCCCC0000;
    private const uint ChunkNode = 0xCCCC000B;
    private const uint ChunkDataStream = 0xCCCC0016;
    private const int StreamPositions = 0;
    private const int StreamIndices = 5;
    private const int MaxChunks = 65536;
    private const float CentimetresPerMetre = 100f;

    /// <summary>What one read found, for the load log.</summary>
    public readonly record struct Counts(int Meshes, int SkippedLods, int Unreadable, long Triangles);

    /// <summary>
    /// The merged render geometry of <paramref name="data"/> in the object frame, or null when the file is no
    /// cgf or has no readable mesh.
    /// </summary>
    public static CollisionMesh? Read(ReadOnlySpan<byte> data, out Counts counts)
    {
        counts = default;
        if (data.Length < 20 || !data[..6].SequenceEqual("CryTek"u8))
            return null;

        var version = U32(data, 12);
        var tableOffset = I32(data, 16);
        if (tableOffset <= 0 || tableOffset + 4 > data.Length)
            return null;
        var count = I32(data, tableOffset);
        var stride = version == 0x745 ? 20 : 16;
        if (count <= 0 || count > MaxChunks || tableOffset + 4 + (long)count * stride > data.Length)
            return null;

        var chunks = new Dictionary<int, (uint Type, int Body)>(count);
        for (var i = 0; i < count; i++)
        {
            var e = tableOffset + 4 + i * stride;
            var type = U32(data, e);
            var offset = I32(data, e + 8);
            var id = I32(data, e + 12);
            if (offset <= 0 || offset + 16 > data.Length)
                continue;
            var body = U32(data, offset) == type && I32(data, offset + 8) == offset && I32(data, offset + 12) == id ? offset + 16 : offset;
            chunks.TryAdd(id, (type, body));
        }

        var nodesByObject = new Dictionary<int, int>();
        foreach (var (id, (type, body)) in chunks)
            if (type == ChunkNode && body + 64 + 16 + 4 + 64 <= data.Length)
                nodesByObject.TryAdd(I32(data, body + 64), id);

        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        int meshes = 0, lods = 0, unreadable = 0;
        foreach (var (meshId, (type, body)) in chunks)
        {
            if (type != ChunkMesh || body + 28 + 64 > data.Length)
                continue;

            var toObject = Matrix4x4.Identity;
            var skip = false;
            if (nodesByObject.TryGetValue(meshId, out var nodeId))
            {
                if (IsLod(data, chunks, nodeId))
                {
                    lods++;
                    continue;
                }

                toObject = NodeChain(data, chunks, nodeId);
            }

            var first = vertices.Count;
            var firstTriangle = triangles.Count;
            try
            {
                var (positions, positionCount, positionSize) = Stream(data, body, StreamPositions, chunks);
                var (indices, indexCount, indexSize) = Stream(data, body, StreamIndices, chunks);
                if (positionSize != 12 || indexSize != 2)
                    throw new InvalidDataException("unsupported render stream layout");
                for (var i = 0; i < positionCount; i++)
                    vertices.Add(V3(data, positions + 12 * i));
                for (var i = 0; i < indexCount; i++)
                {
                    var index = U16(data, indices + 2 * i);
                    if (index >= positionCount)
                        throw new InvalidDataException("index past the positions");
                    triangles.Add(first + index);
                }

                meshes++;
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException)
            {
                unreadable++;
                vertices.RemoveRange(first, vertices.Count - first);
                triangles.RemoveRange(firstTriangle, triangles.Count - firstTriangle);
                skip = true;
            }

            if (!skip && !toObject.IsIdentity)
            {
                for (var i = first; i < vertices.Count; i++)
                    vertices[i] = Vector3.Transform(vertices[i], toObject);
            }
        }

        counts = new Counts(meshes, lods, unreadable, triangles.Count / 3);
        return triangles.Count == 0 ? null : CollisionMesh.Build(vertices, triangles);
    }

    private static (int Data, int Count, int ElementSize) Stream(ReadOnlySpan<byte> data, int meshBody, int streamType, Dictionary<int, (uint Type, int Body)> chunks)
    {
        var id = I32(data, meshBody + 28 + 4 * streamType);
        if (id == 0 || !chunks.TryGetValue(id, out var chunk) || chunk.Type != ChunkDataStream)
            throw new InvalidDataException("render stream missing");
        var b = chunk.Body;
        if (b + 24 > data.Length || I32(data, b + 4) != streamType)
            throw new InvalidDataException("render stream type mismatch");
        var count = I32(data, b + 8);
        var elementSize = I32(data, b + 12);
        if (count < 0 || b + 24 + (long)count * elementSize > data.Length)
            throw new InvalidDataException("render stream out of range");
        return (b + 24, count, elementSize);
    }

    private static bool IsLod(ReadOnlySpan<byte> data, Dictionary<int, (uint Type, int Body)> chunks, int nodeId)
    {
        for (var guard = 0; guard < 64 && chunks.TryGetValue(nodeId, out var node) && node.Type == ChunkNode; guard++)
        {
            var name = data.Slice(node.Body, 64);
            var length = name.IndexOf((byte)0);
            var text = Encoding.ASCII.GetString(length < 0 ? name : name[..length]);
            if (text.StartsWith("$lod", StringComparison.OrdinalIgnoreCase))
                return true;
            nodeId = I32(data, node.Body + 68);
        }

        return false;
    }

    private static Matrix4x4 NodeChain(ReadOnlySpan<byte> data, Dictionary<int, (uint Type, int Body)> chunks, int nodeId)
    {
        var result = Matrix4x4.Identity;
        for (var guard = 0; guard < 64 && chunks.TryGetValue(nodeId, out var node) && node.Type == ChunkNode; guard++)
        {
            var t = node.Body + 64 + 20;
            var m = new Matrix4x4(
                F32(data, t), F32(data, t + 4), F32(data, t + 8), 0f,
                F32(data, t + 16), F32(data, t + 20), F32(data, t + 24), 0f,
                F32(data, t + 32), F32(data, t + 36), F32(data, t + 40), 0f,
                F32(data, t + 48) / CentimetresPerMetre, F32(data, t + 52) / CentimetresPerMetre, F32(data, t + 56) / CentimetresPerMetre, 1f);
            if (!float.IsFinite(m.M11 + m.M22 + m.M33 + m.M41 + m.M42 + m.M43))
                return Matrix4x4.Identity;
            result *= m;
            nodeId = I32(data, node.Body + 68);
        }

        return result;
    }

    private static Vector3 V3(ReadOnlySpan<byte> data, int offset) => new(F32(data, offset), F32(data, offset + 4), F32(data, offset + 8));

    private static uint U32(ReadOnlySpan<byte> data, int offset) => BitConverter.ToUInt32(data[offset..]);
    private static int I32(ReadOnlySpan<byte> data, int offset) => BitConverter.ToInt32(data[offset..]);
    private static ushort U16(ReadOnlySpan<byte> data, int offset) => BitConverter.ToUInt16(data[offset..]);
    private static float F32(ReadOnlySpan<byte> data, int offset) => BitConverter.ToSingle(data[offset..]);
}
