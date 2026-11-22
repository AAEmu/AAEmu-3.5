using System.Numerics;
using System.Text;

namespace AAEmu.Geometry;

/// <summary>
/// Reads the collision proxies of a CryEngine <c>.cgf</c>/<c>.cga</c> (plan slice 6): the physics geometry the native
/// dedicate physicalizes a brush with, never the render mesh unless the asset's physics shares it. Layout read from the
/// shipped files (all 20 392 under <c>game/objects</c> parse), not from a spec or another reader:
/// <list type="bullet">
/// <item>Header <c>CryTek\0\0 | u32 fileType | u32 version (0x744 / 0x745) | u32 tableOffset</c>; table <c>u32 count</c>
/// then <c>count ×</c> <c>{u32 type, u32 version, u32 offset, u32 id}</c> (0x745 adds <c>u32 size</c>). A chunk body may
/// repeat those four dwords first.</item>
/// <item>Mesh <c>0xCCCC0000</c> v0x800: <c>flags, flags2, nVerts, nIndices, nSubsets, subsetsId, vertAnimId,
/// streamId[16], physicsDataId[4], bbox</c>. Physics slot 0 is the default (colliding) proxy; slots 1 (no-collide) and
/// 2 (obstruct, sight only) are left out.</item>
/// <item>Node <c>0xCCCC000B</c>: <c>name[64], objectId, parentId, nChildren, materialId, 4 bytes, float tm[16]</c>
/// (row vectors, translation in the last row in centimetres). A proxy's geometry is in its node's frame, so it goes
/// through the node chain to the object frame. <c>$lod</c> nodes (parked metres off the origin) are skipped.</item>
/// <item>MeshPhysicsData <c>0xCCCC0018</c>: <c>i32 size, flags, tetraSize, tetraId, 2 reserved</c>, then CryPhysics'
/// serialised <c>phys_geometry</c>: 68 bytes (count, Ibody, q, origin, V, refs, surface, …), <c>i32 type</c> at +68,
/// the primitive at +72. Types: 0 box (<c>Matrix33 basis, i32 oriented, center, half sizes</c>), 1 triangle mesh,
/// 4 sphere (<c>center, r</c>), 5 cylinder / 6 capsule (<c>center, axis, r, half height</c>).</item>
/// <item>Triangle mesh: <c>i32 nVerts, nTris, maxValency, flags; u8 hasVtxMap [u16 × nVerts]; u8 sharedVerts</c>.
/// Own vertices (0): <c>nVerts × Vec3</c> then <c>3·nTris × u16</c> indices. Shared (1): the owning mesh chunk's
/// position (stream 0, Vec3) and index (stream 5, u16) streams hold the geometry, and <c>nTris × u16</c> name the
/// render triangle of each physics triangle.</item>
/// </list>
/// Everything returned is in the object frame (metres), ready for a brush's placement matrix.
/// </summary>
public static class CgfPhysicsReader
{
    private const uint ChunkMesh = 0xCCCC0000;
    private const uint ChunkNode = 0xCCCC000B;
    private const uint ChunkDataStream = 0xCCCC0016;
    private const uint ChunkPhysicsData = 0xCCCC0018;
    private const uint ChunkMaterialName = 0xCCCC0014;
    private const int PhysGeometrySurfaceIndex = 52;
    private const int MeshBounds = 28 + 64 + 16;
    private const int StreamPositions = 0;
    private const int StreamIndices = 5;
    private const int GeomBox = 0;
    private const int GeomTriMesh = 1;
    private const int GeomSphere = 4;
    private const int GeomCylinder = 5;
    private const int GeomCapsule = 6;
    private const int MaxChunks = 65536;
    private const float CentimetresPerMetre = 100f;
    /// <summary>Segments of the polygon a cylinder, capsule or sphere is built from (a 0.47 m barrel: 2.6 cm off at worst).</summary>
    private const int RoundSegments = 12;

    /// <summary>What one read found, for the load log, and the material its first default proxy names (its node's material, the proxy's sub-material).</summary>
    public readonly record struct Counts(int Boxes, int Meshes, int Rounds, int SkippedLods, int Unreadable)
    {
        /// <summary>The material the asset's node names; null when the file names none.</summary>
        public string? Material { get; init; }

        /// <summary>The sub-material the default proxy names (its phys_geometry surface index).</summary>
        public int MaterialSlot { get; init; }
    }

    /// <summary>
    /// The default collision proxies of <paramref name="data"/> merged into one triangle list (object frame), or null
    /// when the file is no cgf or has none (decor such as hanging weapons: native gives it no collision either).
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
            // A body may repeat its table entry; skip it when it does.
            var body = U32(data, offset) == type && I32(data, offset + 8) == offset && I32(data, offset + 12) == id ? offset + 16 : offset;
            chunks.TryAdd(id, (type, body));
        }

        // Nodes by the object (mesh) they carry, with their parent chain.
        var nodesByObject = new Dictionary<int, int>();
        foreach (var (id, (type, body)) in chunks)
        {
            if (type == ChunkNode && body + 64 + 16 + 4 + 64 <= data.Length)
                nodesByObject.TryAdd(I32(data, body + 64), id);
        }

        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        int boxes = 0, meshes = 0, rounds = 0, lods = 0, unreadable = 0;
        (string? Name, int Slot)? material = null;
        foreach (var (meshId, (type, body)) in chunks)
        {
            if (type != ChunkMesh || body + 28 + 64 + 16 > data.Length)
                continue;
            var physicsId = I32(data, body + 28 + 64); // slot 0: the default (colliding) proxy
            if (physicsId == 0)
                continue;
            if (!chunks.TryGetValue(physicsId, out var physics) || physics.Type != ChunkPhysicsData)
            {
                unreadable++;
                continue;
            }

            var toObject = Matrix4x4.Identity;
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
                switch (ReadGeometry(data, physics.Body, body, chunks, vertices, triangles))
                {
                    case GeomBox: boxes++; break;
                    case GeomTriMesh: meshes++; break;
                    case GeomSphere or GeomCylinder or GeomCapsule: rounds++; break;
                    default: unreadable++; break;
                }
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException)
            {
                unreadable++;
                vertices.RemoveRange(first, vertices.Count - first);
                triangles.RemoveRange(firstTriangle, triangles.Count - firstTriangle);
                continue;
            }

            if (!toObject.IsIdentity)
            {
                for (var i = first; i < vertices.Count; i++)
                    vertices[i] = Vector3.Transform(vertices[i], toObject);
            }

            material ??= (nodesByObject.TryGetValue(meshId, out var owner) ? MaterialName(data, chunks, owner) : null,
                I32(data, physics.Body + 24 + PhysGeometrySurfaceIndex));
        }

        counts = new Counts(boxes, meshes, rounds, lods, unreadable) { Material = material?.Name, MaterialSlot = material?.Slot ?? 0 };
        return triangles.Count == 0 ? null : CollisionMesh.Build(vertices, triangles);
    }

    /// <summary>
    /// The box of the asset's meshes, <c>$lod</c> nodes left out, in their own frames: the statobj box whose half
    /// diagonal is the radius native's vegetation size test takes. Null when the file is no cgf or has no mesh.
    /// </summary>
    public static (Vector3 Min, Vector3 Max)? RenderBounds(ReadOnlySpan<byte> data)
        => ReadRenderBounds(data, objectFrame: false) is { } box ? (box.Min, box.Max) : null;

    /// <summary>Render bounds of occupied meshes in the asset frame, including their node chains.</summary>
    public static GeometryBounds? ObjectRenderBounds(ReadOnlySpan<byte> data) => ReadRenderBounds(data, objectFrame: true);

    private static GeometryBounds? ReadRenderBounds(ReadOnlySpan<byte> data, bool objectFrame)
    {
        if (data.Length < 20 || !data[..6].SequenceEqual("CryTek"u8))
            return null;
        var version = U32(data, 12);
        var tableOffset = I32(data, 16);
        if (tableOffset <= 0 || tableOffset > data.Length - 4)
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
            if (offset <= 0 || offset > data.Length - 16)
                continue;
            var body = U32(data, offset) == type && I32(data, offset + 8) == offset && I32(data, offset + 12) == id ? offset + 16 : offset;
            if (type == ChunkNode && body > data.Length - (objectFrame ? 148 : 72))
                continue;
            chunks.TryAdd(id, (type, body));
        }

        var nodesByObject = new Dictionary<int, int>();
        foreach (var (id, (type, body)) in chunks)
        {
            if (type == ChunkNode && body + 68 <= data.Length)
                nodesByObject.TryAdd(I32(data, body + 64), id);
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var (meshId, (type, body)) in chunks)
        {
            if (type != ChunkMesh || body > data.Length - MeshBounds - 24)
                continue;
            if (objectFrame && I32(data, body + 8) <= 0)
                continue;
            var box = new GeometryBounds(V3(data, body + MeshBounds), V3(data, body + MeshBounds + 12));
            if (nodesByObject.TryGetValue(meshId, out var nodeId))
            {
                if (IsLod(data, chunks, nodeId))
                    continue;
                if (objectFrame)
                    box = box.Transform(NodeChain(data, chunks, nodeId));
            }
            min = Vector3.Min(min, box.Min);
            max = Vector3.Max(max, box.Max);
        }

        return min.X <= max.X ? new GeometryBounds(min, max) : null;
    }

    /// <summary>The name in the material chunk a node names; null when it names none this reader knows.</summary>
    private static string? MaterialName(ReadOnlySpan<byte> data, Dictionary<int, (uint Type, int Body)> chunks, int nodeId)
    {
        if (!chunks.TryGetValue(nodeId, out var node) || node.Type != ChunkNode || node.Body + 80 > data.Length)
            return null;
        if (!chunks.TryGetValue(I32(data, node.Body + 76), out var chunk) || chunk.Type != ChunkMaterialName || chunk.Body + 8 + 128 > data.Length)
            return null;
        // Version 0x800: flags, a reserved word, then the name.
        var name = data.Slice(chunk.Body + 8, 128);
        var length = name.IndexOf((byte)0);
        var text = Encoding.ASCII.GetString(length < 0 ? name : name[..length]).Trim();
        return text.Length > 0 ? text : null;
    }

    /// <summary>Appends one serialised phys_geometry; returns its type (-1 unknown).</summary>
    private static int ReadGeometry(ReadOnlySpan<byte> data, int physicsBody, int meshBody, Dictionary<int, (uint Type, int Body)> chunks, List<Vector3> vertices, List<int> triangles)
    {
        var size = I32(data, physicsBody);
        var g = physicsBody + 24;
        if (size < 72 || g + size > data.Length)
            throw new InvalidDataException("physics chunk out of range");
        var end = g + size;
        var type = I32(data, g + 68);
        var p = g + 72;
        switch (type)
        {
            case GeomBox:
            {
                Need(p + 64, end);
                var basis = new[] { V3(data, p), V3(data, p + 12), V3(data, p + 24) };
                var oriented = I32(data, p + 36) != 0;
                var center = V3(data, p + 40);
                var half = V3(data, p + 52);
                if (!oriented)
                    basis = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
                AddBox(center, basis[0] * half.X, basis[1] * half.Y, basis[2] * half.Z, vertices, triangles);
                return type;
            }
            case GeomSphere:
            {
                Need(p + 16, end);
                AddSphere(V3(data, p), F32(data, p + 12), vertices, triangles);
                return type;
            }
            case GeomCylinder or GeomCapsule:
            {
                Need(p + 32, end);
                var center = V3(data, p);
                var axis = V3(data, p + 12);
                var r = F32(data, p + 24);
                var hh = F32(data, p + 28);
                // A capsule's caps are approximated by a flat end at the true extremity (the top a unit stands on).
                AddCylinder(center, axis, r, type == GeomCapsule ? hh + r : hh, vertices, triangles);
                return type;
            }
            case GeomTriMesh:
            {
                Need(p + 18, end);
                var nVerts = I32(data, p);
                var nTris = I32(data, p + 4);
                if (nVerts < 0 || nTris < 0)
                    throw new InvalidDataException("negative counts");
                var q = p + 16;
                if (data[q++] != 0)
                    q += 2 * nVerts; // vertex weld map: not needed for queries
                Need(q + 1, end);
                var shared = data[q++] != 0;
                if (!shared)
                {
                    Need(q + 12 * nVerts + 6 * nTris, end);
                    var first = vertices.Count;
                    for (var i = 0; i < nVerts; i++)
                        vertices.Add(V3(data, q + 12 * i));
                    q += 12 * nVerts;
                    for (var i = 0; i < 3 * nTris; i++)
                    {
                        var index = U16(data, q + 2 * i);
                        if (index >= nVerts)
                            throw new InvalidDataException("index past the vertices");
                        triangles.Add(first + index);
                    }

                    return type;
                }

                // Shared with the render mesh: the physics triangle i is render triangle foreign[i].
                Need(q + 2 * nTris, end);
                var (positions, positionCount, positionSize) = Stream(data, meshBody, StreamPositions, chunks);
                var (indices, indexCount, indexSize) = Stream(data, meshBody, StreamIndices, chunks);
                if (positionSize != 12 || indexSize != 2)
                    throw new InvalidDataException("unsupported render stream layout");
                var baseVertex = vertices.Count;
                var remap = new Dictionary<int, int>();
                for (var i = 0; i < nTris; i++)
                {
                    var renderTriangle = U16(data, q + 2 * i);
                    if (3 * renderTriangle + 2 >= indexCount)
                        throw new InvalidDataException("foreign triangle past the render indices");
                    for (var c = 0; c < 3; c++)
                    {
                        var v = U16(data, indices + 2 * (3 * renderTriangle + c));
                        if (v >= positionCount)
                            throw new InvalidDataException("render index past the positions");
                        if (!remap.TryGetValue(v, out var local))
                        {
                            local = vertices.Count - baseVertex;
                            remap[v] = local;
                            vertices.Add(V3(data, positions + 12 * v));
                        }

                        triangles.Add(baseVertex + local);
                    }
                }

                return type;
            }
            default:
                return -1;
        }
    }

    /// <summary>A mesh chunk's data stream: (data offset, element count, element size).</summary>
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

    /// <summary>Node frame → object frame: the row-vector tms of the node and its parents, translations in cm.</summary>
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

    private static void AddBox(Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, List<Vector3> vertices, List<int> triangles)
    {
        var b = vertices.Count;
        for (var i = 0; i < 8; i++)
            vertices.Add(c + ((i & 1) != 0 ? ax : -ax) + ((i & 2) != 0 ? ay : -ay) + ((i & 4) != 0 ? az : -az));
        ReadOnlySpan<int> faces = [0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5];
        foreach (var f in faces)
            triangles.Add(b + f);
    }

    private static void AddCylinder(Vector3 center, Vector3 axis, float r, float hh, List<Vector3> vertices, List<int> triangles)
    {
        if (axis.LengthSquared() < 1e-12f)
            axis = Vector3.UnitZ;
        axis = Vector3.Normalize(axis);
        var u = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX));
        var w = Vector3.Cross(axis, u);
        var b = vertices.Count;
        vertices.Add(center - axis * hh); // b: bottom centre
        vertices.Add(center + axis * hh); // b+1: top centre
        for (var i = 0; i < RoundSegments; i++)
        {
            var a = 2 * MathF.PI * i / RoundSegments;
            var rim = (u * MathF.Cos(a) + w * MathF.Sin(a)) * r;
            vertices.Add(center - axis * hh + rim); // b+2+2i
            vertices.Add(center + axis * hh + rim); // b+3+2i
        }

        for (var i = 0; i < RoundSegments; i++)
        {
            var j = (i + 1) % RoundSegments;
            int lo0 = b + 2 + 2 * i, hi0 = lo0 + 1, lo1 = b + 2 + 2 * j, hi1 = lo1 + 1;
            triangles.AddRange([lo0, lo1, hi1, lo0, hi1, hi0, b, lo1, lo0, b + 1, hi0, hi1]);
        }
    }

    private static void AddSphere(Vector3 center, float r, List<Vector3> vertices, List<int> triangles)
    {
        const int rings = RoundSegments / 2;
        var b = vertices.Count;
        for (var ring = 0; ring <= rings; ring++)
        {
            var phi = MathF.PI * ring / rings;
            for (var i = 0; i < RoundSegments; i++)
            {
                var a = 2 * MathF.PI * i / RoundSegments;
                vertices.Add(center + new Vector3(MathF.Sin(phi) * MathF.Cos(a), MathF.Sin(phi) * MathF.Sin(a), MathF.Cos(phi)) * r);
            }
        }

        for (var ring = 0; ring < rings; ring++)
        {
            for (var i = 0; i < RoundSegments; i++)
            {
                var j = (i + 1) % RoundSegments;
                int a0 = b + ring * RoundSegments + i, a1 = b + ring * RoundSegments + j;
                int b0 = a0 + RoundSegments, b1 = a1 + RoundSegments;
                triangles.AddRange([a0, b0, b1, a0, b1, a1]);
            }
        }
    }

    private static void Need(long end, int limit)
    {
        if (end > limit)
            throw new InvalidDataException("geometry runs past its chunk");
    }

    private static int I32(ReadOnlySpan<byte> d, int o) => BitConverter.ToInt32(d.Slice(o, 4));
    private static uint U32(ReadOnlySpan<byte> d, int o) => BitConverter.ToUInt32(d.Slice(o, 4));
    private static ushort U16(ReadOnlySpan<byte> d, int o) => BitConverter.ToUInt16(d.Slice(o, 2));
    private static float F32(ReadOnlySpan<byte> d, int o) => BitConverter.ToSingle(d.Slice(o, 4));
    private static Vector3 V3(ReadOnlySpan<byte> d, int o) => new(F32(d, o), F32(d, o + 4), F32(d, o + 8));
}
