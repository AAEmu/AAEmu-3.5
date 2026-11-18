using System.Numerics;
using AAEmu.Game.Core.Managers.Bots.Navigation;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Core.Managers.Bots.Navigation;

/// <summary>
/// A synthetic block shaped like the client's files: the 256 m square cut into <see cref="Cells"/> x
/// <see cref="Cells"/> squares of two triangles each (A: corners (i, j), (i+1, j), (i+1, j+1); B: (i, j),
/// (i+1, j+1), (i, j+1) on the corner lattice), links both ways across every shared edge with half the edge as pass
/// radius, and the file's rule that the way into a blocked cell from outside is blocked (-1). Only the corners of
/// blocked cells and blocked edges are obstacle points.
/// </summary>
internal sealed class GridMesh(int blockX, int blockY, int cells)
{
    public int BlockX { get; } = blockX;
    public int BlockY { get; } = blockY;
    public int Cells { get; } = cells;
    public float Cell => NavBlock.Size / Cells;
    public float MinX => BlockX * NavBlock.Size;
    public float MinY => BlockY * NavBlock.Size;

    /// <summary>Ground height everywhere (main_world's sea is at 100 m, so 150 is dry land).</summary>
    public float Height { get; set; } = 150f;

    /// <summary>Height of a lattice corner (i, j) when set, else <see cref="Height"/>.</summary>
    public Func<int, int, float> CornerHeight { get; set; }

    /// <summary>Cells inside a blocked area (node bits 48).</summary>
    public HashSet<(int I, int J)> Blocked { get; } = [];

    /// <summary>Pass radius of an edge between two lattice corners, both ways, instead of half its length.</summary>
    public Dictionary<((int, int), (int, int)), float> EdgeRadius { get; } = [];

    /// <summary>
    /// When set, adds a fringe sliver past the east side next to every boundary cell (as the client files have),
    /// linked both ways with this pass radius.
    /// </summary>
    public float? EastFringeRadius { get; set; }

    /// <summary>Extra raw nodes and links (waypoint grids), added as they are.</summary>
    public List<NavRawNode> ExtraNodes { get; } = [];
    public List<NavRawLink> ExtraLinks { get; } = [];

    public Vector3 Corner(int i, int j) => new(MinX + i * Cell, MinY + j * Cell, CornerHeight?.Invoke(i, j) ?? Height);

    public Vector3 CellCentre(int i, int j) => new(MinX + (i + 0.5f) * Cell, MinY + (j + 0.5f) * Cell, Height);

    /// <summary>File node id of triangle A (0) or B (1) of cell (i, j).</summary>
    public int NodeId(int i, int j, int half) => 1 + 2 * (j * Cells + i) + half;

    /// <summary>Blocks the lattice line between two corners, one edge at a time (both ways).</summary>
    public void BlockLine((int I, int J) from, (int I, int J) to, float radius = -1f)
    {
        var steps = Math.Max(Math.Abs(to.I - from.I), Math.Abs(to.J - from.J));
        var di = Math.Sign(to.I - from.I);
        var dj = Math.Sign(to.J - from.J);
        for (var s = 0; s < steps; s++)
            EdgeRadius[Key((from.I + s * di, from.J + s * dj), (from.I + (s + 1) * di, from.J + (s + 1) * dj))] = radius;
    }

    public NavBlockData Data()
    {
        var data = new NavBlockData();
        var n = Cells;
        // Corners of blocked cells and ends of overridden edges are obstacle points; open ground has none.
        var obstacles = EdgeRadius.Keys.SelectMany(k => new[] { k.Item1, k.Item2 }).ToHashSet();
        foreach (var (i, j) in Blocked)
            obstacles.UnionWith([(i, j), (i + 1, j), (i, j + 1), (i + 1, j + 1)]);
        for (var j = 0; j <= n; j++)
            for (var i = 0; i <= n; i++)
                data.Vertices.Add(new NavRawVertex(Corner(i, j), 0f, obstacles.Contains((i, j))));

        int V(int i, int j) => j * (n + 1) + i;
        int[] CornersOf(int i, int j, int half) => half == 0
            ? [V(i, j), V(i + 1, j), V(i + 1, j + 1)]
            : [V(i, j), V(i + 1, j + 1), V(i, j + 1)];
        (int, int) Lattice(int v) => (v % (n + 1), v / (n + 1));

        for (var j = 0; j < n; j++)
        {
            for (var i = 0; i < n; i++)
            {
                for (var half = 0; half < 2; half++)
                {
                    var c = CornersOf(i, j, half);
                    var centre = (data.Vertices[c[0]].Position + data.Vertices[c[1]].Position + data.Vertices[c[2]].Position) / 3f;
                    data.Nodes.Add(new NavRawNode(NodeId(i, j, half), NavRules.TriangleNodeType,
                        Blocked.Contains((i, j)) ? (byte)48 : (byte)0, c[0], c[1], c[2], centre));
                }
            }
        }

        void Link(int i, int j, int half, byte start, byte end, int ti, int tj, int thalf)
        {
            if (ti < 0 || tj < 0 || ti >= n || tj >= n)
                return;
            var c = CornersOf(i, j, half);
            var p = data.Vertices[c[start]].Position;
            var q = data.Vertices[c[end]].Position;
            var radius = EdgeRadius.TryGetValue(Key(Lattice(c[start]), Lattice(c[end])), out var r) ? r : Vector3.Distance(p, q) / 2f;
            if (Blocked.Contains((ti, tj)) && !Blocked.Contains((i, j)))
                radius = -1f;
            data.Links.Add(new NavRawLink(NodeId(i, j, half), NodeId(ti, tj, thalf), radius, (p + q) / 2f, start, end));
        }

        for (var j = 0; j < n; j++)
        {
            for (var i = 0; i < n; i++)
            {
                Link(i, j, 0, 0, 1, i, j - 1, 1); // A bottom
                Link(i, j, 0, 1, 2, i + 1, j, 1); // A right
                Link(i, j, 0, 2, 0, i, j, 1);     // A diagonal
                Link(i, j, 1, 0, 1, i, j, 0);     // B diagonal
                Link(i, j, 1, 1, 2, i, j + 1, 0); // B top
                Link(i, j, 1, 2, 0, i - 1, j, 0); // B left
            }
        }

        if (EastFringeRadius is { } fringe)
        {
            for (var j = 0; j < n; j++)
            {
                var bottom = V(n, j);
                var top = V(n, j + 1);
                var outside = data.Vertices.Count;
                var mid = (Corner(n, j) + Corner(n, j + 1)) / 2f;
                data.Vertices.Add(new NavRawVertex(mid with { X = mid.X + 0.5f }, 0f));
                var id = 100_000 + j;
                data.Nodes.Add(new NavRawNode(id, NavRules.TriangleNodeType, 0, bottom, outside, top,
                    (Corner(n, j) + Corner(n, j + 1) + data.Vertices[outside].Position) / 3f));
                // A of the last column: corner 1 = (n, j), corner 2 = (n, j + 1). The sliver: corner 0 = (n, j), 2 = (n, j + 1).
                data.Links.Add(new NavRawLink(NodeId(n - 1, j, 0), id, fringe, mid, 1, 2));
                data.Links.Add(new NavRawLink(id, NodeId(n - 1, j, 0), fringe, mid, 0, 2));
            }
        }

        data.Nodes.AddRange(ExtraNodes);
        data.Links.AddRange(ExtraLinks);
        return data;
    }

    public NavBlock Block(float seamTolerance = 0.1f, Func<float, float, float, float> water = null, bool grids = false) =>
        NavBlock.Build(BlockX, BlockY, Data(), seamTolerance, water, grids);

    private static ((int, int), (int, int)) Key((int, int) a, (int, int) b) => a.CompareTo(b) <= 0 ? (a, b) : (b, a);
}

/// <summary>Block data handed out by coordinates, counting loads; a missing key is a block the world does not have.</summary>
internal sealed class FakeBlockSource : INavBlockSource
{
    private readonly Dictionary<(int, int), Func<NavBlockData>> _blocks = [];
    private int _loads;

    public int Loads => Volatile.Read(ref _loads);

    /// <summary>Called before each load (tests use it to slow loading down or to throw).</summary>
    public Action<int, int> OnLoad { get; set; }

    public FakeBlockSource Add(GridMesh mesh)
    {
        _blocks[(mesh.BlockX, mesh.BlockY)] = mesh.Data;
        return this;
    }

    public FakeBlockSource Add(int x, int y, Func<NavBlockData> data)
    {
        _blocks[(x, y)] = data;
        return this;
    }

    public NavBlockData Load(string worldName, int blockX, int blockY)
    {
        Interlocked.Increment(ref _loads);
        OnLoad?.Invoke(blockX, blockY);
        return _blocks.TryGetValue((blockX, blockY), out var data) ? data() : null;
    }
}

internal static class NavTestWorlds
{
    /// <summary>A world whose sea is at 100 m, as main_world's is.</summary>
    public static WorldTemplate World(string name = "synthetic") => new() { Name = name, OceanLevel = 100f };

    /// <summary>
    /// True when every point of every leg, sampled every half metre, lies on a triangle that is not in a blocked area
    /// (within <paramref name="slack"/> metres), or within a metre of a structure waypoint. The first
    /// <paramref name="skipStart"/> metres are not checked (a start inside a blocked area walks out of it).
    /// </summary>
    public static bool StaysOnWalkable(Func<int, int, NavBlock> blocks, IReadOnlyList<Vector3> waypoints, float slack = 0.1f,
        float skipStart = 0f)
    {
        var walked = 0f;
        for (var w = 1; w < waypoints.Count; w++)
        {
            var from = waypoints[w - 1];
            var to = waypoints[w];
            var length = NavGeometry.Flat(from, to);
            var steps = Math.Max(1, (int)(length / 0.5f));
            for (var s = 0; s <= steps; s++)
            {
                var p = Vector3.Lerp(from, to, s / (float)steps);
                if (walked + length * s / steps < skipStart)
                    continue;
                var block = blocks((int)MathF.Floor(p.X / NavBlock.Size), (int)MathF.Floor(p.Y / NavBlock.Size));
                if (block == null)
                    return false;
                if (block.NearestWaypoint(p.X, p.Y, p.Z, 1f, 100f, out _) >= 0)
                    continue;
                var t = block.Nearest(p.X, p.Y, slack, n => !block.IsForbidden(n), out _, out _);
                if (t < 0)
                    return false;
            }

            walked += length;
        }

        return true;
    }

    /// <summary>
    /// True when every point of every leg lies on one of <paramref name="corridor"/>'s nodes (within
    /// <paramref name="slack"/> metres), sampled every half metre; structure waypoints count as a 1 m disc.
    /// </summary>
    public static bool StaysInside(IReadOnlyList<Vector3> waypoints, IReadOnlyList<(NavBlock Block, int Node)> corridor, float slack = 0.1f)
    {
        for (var w = 1; w < waypoints.Count; w++)
        {
            var from = waypoints[w - 1];
            var to = waypoints[w];
            var steps = Math.Max(1, (int)(NavGeometry.Flat(from, to) / 0.5f));
            for (var s = 0; s <= steps; s++)
            {
                var p = Vector3.Lerp(from, to, s / (float)steps);
                if (!corridor.Any(c => c.Block.IsWaypoint(c.Node)
                        ? NavGeometry.Flat(c.Block.Centre(c.Node), p) <= 1f
                        : c.Block.InsideDistance(c.Node, p.X, p.Y) >= -slack))
                    return false;
            }
        }

        return true;
    }
}
