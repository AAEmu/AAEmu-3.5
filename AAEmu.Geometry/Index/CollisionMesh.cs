using System.Numerics;
using System.Runtime.InteropServices;

namespace AAEmu.Geometry;

/// <summary>
/// One asset's collision triangles in its object frame with a bounding-volume hierarchy over them (plan slice 6).
/// Immutable once built and shared by every placement of the asset, in every zone and copy of the process
/// (<see cref="CollisionAssetCache"/>). Arrays of primitives, no per-triangle objects (§4.4): vertices as packed
/// floats, triangles as packed indices reordered so each leaf covers a contiguous run.
/// </summary>
public sealed class CollisionMesh
{
    private const int LeafSize = 4;
    private const float Epsilon = 1e-7f;

    private readonly float[] _vertices;   // x,y,z per vertex
    private readonly int[] _triangles;    // 3 vertex indices per triangle, leaf-ordered
    private readonly float[] _bounds;     // min xyz, max xyz per node
    private readonly int[] _nodes;        // per node: first (triangle, or left child), count (0 = inner node; right child = left + 1)

    private CollisionMesh(float[] vertices, int[] triangles, float[] bounds, int[] nodes)
    {
        _vertices = vertices;
        _triangles = triangles;
        _bounds = bounds;
        _nodes = nodes;
        Min = new Vector3(bounds[0], bounds[1], bounds[2]);
        Max = new Vector3(bounds[3], bounds[4], bounds[5]);
    }

    public int TriangleCount => _triangles.Length / 3;
    public int VertexCount => _vertices.Length / 3;

    /// <summary>The vertices in the object frame.</summary>
    public ReadOnlySpan<Vector3> Vertices => MemoryMarshal.Cast<float, Vector3>(_vertices);

    /// <summary>Three indices into <see cref="Vertices"/> per triangle.</summary>
    public ReadOnlySpan<int> Triangles => _triangles;

    public Vector3 Min { get; }
    public Vector3 Max { get; }
    public long Bytes => 4L * (_vertices.Length + _triangles.Length + _bounds.Length + _nodes.Length) + 4 * 24 + 64;

    /// <summary>The asset's own material (a <see cref="MaterialFile.PathOf"/> path), set once by the asset cache that read it; null when unknown.</summary>
    public string? Material { get; internal set; }

    /// <summary>The sub-material the asset's default proxy names (its phys_geometry surface index).</summary>
    public int MaterialSlot { get; internal set; }

    /// <summary>The surface type the asset's material names (its sub-material at <see cref="MaterialSlot"/>); null when unknown.</summary>
    public string? SurfaceType { get; internal set; }

    /// <summary>The id of <see cref="SurfaceType"/> in the client's surface types; 0 (the default) when unknown.</summary>
    public int Surface { get; internal set; }

    /// <summary>
    /// Builds the hierarchy over <paramref name="triangles"/> (3 indices each into <paramref name="vertices"/>). Degenerate
    /// triangles are dropped and only the vertices the rest use are kept, renumbered in leaf order: a CryPhysics trimesh
    /// carries its welded duplicates (the cave gate: 9 520 vertices, 1 244 used).
    /// </summary>
    public static CollisionMesh Build(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> triangles)
    {
        var kept = new List<int>(triangles.Count / 3);
        for (var t = 0; t < triangles.Count / 3; t++)
        {
            var a = vertices[triangles[3 * t]];
            if (Vector3.Cross(vertices[triangles[3 * t + 1]] - a, vertices[triangles[3 * t + 2]] - a).LengthSquared() > 1e-14f)
                kept.Add(t);
        }

        var triangleCount = kept.Count;
        var order = new int[triangleCount];
        var centroids = new Vector3[triangleCount];
        var boxes = new (Vector3 Min, Vector3 Max)[triangleCount];
        for (var i = 0; i < triangleCount; i++)
        {
            order[i] = i;
            var t = kept[i];
            var a = vertices[triangles[3 * t]];
            var b = vertices[triangles[3 * t + 1]];
            var c = vertices[triangles[3 * t + 2]];
            boxes[i] = (Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c)));
            centroids[i] = (a + b + c) / 3f;
        }

        var bounds = new List<float>(Math.Max(12, triangleCount / 2 * 6));
        var nodes = new List<int>(Math.Max(4, triangleCount / 2 * 2));
        Allocate(bounds, nodes, 1);
        BuildInto(0, order, 0, triangleCount, boxes, centroids, bounds, nodes);

        var remap = new Dictionary<int, int>();
        var packed = new List<float>();
        var reordered = new int[triangleCount * 3];
        for (var i = 0; i < triangleCount; i++)
        {
            var t = kept[order[i]];
            for (var c = 0; c < 3; c++)
            {
                var source = triangles[3 * t + c];
                if (!remap.TryGetValue(source, out var index))
                {
                    index = remap.Count;
                    remap[source] = index;
                    packed.AddRange([vertices[source].X, vertices[source].Y, vertices[source].Z]);
                }

                reordered[3 * i + c] = index;
            }
        }

        return new CollisionMesh([.. packed], reordered, [.. bounds], [.. nodes]);
    }

    private static int Allocate(List<float> bounds, List<int> nodes, int count)
    {
        var first = nodes.Count / 2;
        for (var i = 0; i < count; i++)
        {
            bounds.AddRange([0f, 0f, 0f, 0f, 0f, 0f]);
            nodes.AddRange([0, 0]);
        }

        return first;
    }

    /// <summary>Top-down median split on the longest centroid axis into the already allocated <paramref name="slot"/>; an inner node's children are allocated as a pair (right = left + 1).</summary>
    internal static void BuildInto(int slot, int[] order, int start, int count, (Vector3 Min, Vector3 Max)[] boxes, Vector3[] centroids, List<float> bounds, List<int> nodes)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var cmin = new Vector3(float.MaxValue);
        var cmax = new Vector3(float.MinValue);
        for (var i = start; i < start + count; i++)
        {
            min = Vector3.Min(min, boxes[order[i]].Min);
            max = Vector3.Max(max, boxes[order[i]].Max);
            cmin = Vector3.Min(cmin, centroids[order[i]]);
            cmax = Vector3.Max(cmax, centroids[order[i]]);
        }

        if (count == 0)
            min = max = Vector3.Zero;
        bounds[6 * slot] = min.X;
        bounds[6 * slot + 1] = min.Y;
        bounds[6 * slot + 2] = min.Z;
        bounds[6 * slot + 3] = max.X;
        bounds[6 * slot + 4] = max.Y;
        bounds[6 * slot + 5] = max.Z;
        if (count <= LeafSize)
        {
            nodes[2 * slot] = start;
            nodes[2 * slot + 1] = count;
            return;
        }

        var extent = cmax - cmin;
        var axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
        Array.Sort(order, start, count, Comparer<int>.Create((a, b) => Axis(centroids[a], axis).CompareTo(Axis(centroids[b], axis))));
        var half = count / 2;
        var left = Allocate(bounds, nodes, 2);
        nodes[2 * slot] = left;
        nodes[2 * slot + 1] = 0;
        BuildInto(left, order, start, half, boxes, centroids, bounds, nodes);
        BuildInto(left + 1, order, start + half, count - half, boxes, centroids, bounds, nodes);
    }

    private static float Axis(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    /// <summary>
    /// Nearest hit of the ray <paramref name="origin"/> + t·<paramref name="direction"/> with 0 ≤ t &lt; <paramref name="maxT"/>
    /// (both sides of every triangle count). On a hit lowers <paramref name="maxT"/> to it and gives the triangle's normal
    /// (unnormalised, either orientation). <paramref name="minUpNormal"/> &gt; 0 skips triangles whose normal, taken to
    /// the world by <paramref name="toWorld"/>, has |n.z| / |n| below it (walls, for floor queries).
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, ref float maxT, out Vector3 normal, float minUpNormal = 0f, Matrix4x4? toWorld = null)
    {
        var normalToWorld = Matrix4x4.Identity;
        if (minUpNormal > 0f && toWorld is { } m && Matrix4x4.Invert(m, out var inverse))
            normalToWorld = Matrix4x4.Transpose(inverse);
        normal = default;
        if (_nodes.Length == 0)
            return false;
        var inv = new Vector3(1f / direction.X, 1f / direction.Y, 1f / direction.Z);
        Span<int> stack = stackalloc int[64];
        var top = 0;
        stack[top++] = 0;
        var hit = false;
        while (top > 0)
        {
            var node = stack[--top];
            if (!SlabHit(_bounds, node, origin, inv, maxT))
                continue;
            var first = _nodes[2 * node];
            var count = _nodes[2 * node + 1];
            if (count == 0)
            {
                if (top + 2 > stack.Length)
                    continue;
                stack[top++] = first;
                stack[top++] = first + 1;
                continue;
            }

            for (var t = first; t < first + count; t++)
            {
                if (IntersectTriangle(t, origin, direction, maxT, out var at, out var n))
                {
                    if (minUpNormal > 0f)
                    {
                        var w = Vector3.TransformNormal(n, normalToWorld);
                        var length = w.Length();
                        if (length <= 0f || MathF.Abs(w.Z) < minUpNormal * length)
                            continue;
                    }

                    maxT = at;
                    normal = n;
                    hit = true;
                }
            }
        }

        return hit;
    }

    private bool IntersectTriangle(int t, Vector3 o, Vector3 d, float maxT, out float at, out Vector3 normal)
    {
        at = 0f;
        var a = Vertex(_triangles[3 * t]);
        var e1 = Vertex(_triangles[3 * t + 1]) - a;
        var e2 = Vertex(_triangles[3 * t + 2]) - a;
        normal = Vector3.Cross(e1, e2);
        var p = Vector3.Cross(d, e2);
        var det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < Epsilon)
            return false;
        var invDet = 1f / det;
        var s = o - a;
        var u = Vector3.Dot(s, p) * invDet;
        if (u < 0f || u > 1f)
            return false;
        var q = Vector3.Cross(s, e1);
        var v = Vector3.Dot(d, q) * invDet;
        if (v < 0f || u + v > 1f)
            return false;
        at = Vector3.Dot(e2, q) * invDet;
        return at >= 0f && at < maxT;
    }

    private Vector3 Vertex(int i) => new(_vertices[3 * i], _vertices[3 * i + 1], _vertices[3 * i + 2]);

    internal static bool SlabHit(float[] bounds, int node, Vector3 o, Vector3 inv, float maxT)
    {
        var b = 6 * node;
        var t1 = (bounds[b] - o.X) * inv.X;
        var t2 = (bounds[b + 3] - o.X) * inv.X;
        var tmin = MathF.Min(t1, t2);
        var tmax = MathF.Max(t1, t2);
        t1 = (bounds[b + 1] - o.Y) * inv.Y;
        t2 = (bounds[b + 4] - o.Y) * inv.Y;
        tmin = MathF.Max(tmin, MathF.Min(t1, t2));
        tmax = MathF.Min(tmax, MathF.Max(t1, t2));
        t1 = (bounds[b + 2] - o.Z) * inv.Z;
        t2 = (bounds[b + 5] - o.Z) * inv.Z;
        tmin = MathF.Max(tmin, MathF.Min(t1, t2));
        tmax = MathF.Min(tmax, MathF.Max(t1, t2));
        // NaN from 0 × ∞ (a ray in a slab's plane) compares false, which keeps the node: conservative.
        return !(tmax < MathF.Max(tmin, 0f)) && !(tmin > maxT);
    }
}
