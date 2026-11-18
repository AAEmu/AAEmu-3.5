using System.Numerics;

namespace AAEmu.Game.Core.Managers.Bots.Navigation;

/// <summary>
/// An edge of a block's walkable area that lies on one of the block's four sides, where the neighbour block takes
/// over (DESIGN 8.3: no link in the file crosses a block). <see cref="Lo"/> and <see cref="Hi"/> are where it starts
/// and ends along the side (world y on the west and east sides, world x on the south and north ones).
/// <see cref="OutRadius"/> and <see cref="InRadius"/> are what this block's own file says about walking out across
/// the edge and in across it (NaN when it says nothing); <see cref="Depth"/> is the water over its middle.
/// </summary>
public readonly record struct NavSeamEdge(int Triangle, byte Edge, float Lo, float Hi, float OutRadius, float InRadius, float Depth);

/// <summary>
/// The walkable graph of one 256 m block of a world's navigation mesh (<c>paths/XXX_YYY</c>), compacted into arrays
/// and immutable once built, so any number of planner threads share it.
/// <para>
/// The client file triangulates the block's square plus a thin fringe of slivers out to obstacle points just past
/// its sides (and, in empty blocks, four large triangles out to the neighbours' far corners). The square's sides
/// are always triangle edges, so the triangles whose centre lies inside the square tile it exactly; only those are
/// kept, and the fringe is read for what it says about crossing each side. The neighbour's own triangles cover the
/// fringe, and <see cref="NavSeamStitcher"/> joins the two at the side.
/// </para>
/// <para>
/// Nodes 0 .. <see cref="TriangleCount"/> - 1 are the triangles (type 2, DESIGN 8.3). When built with structure
/// grids, the nodes after them are the file's waypoint grids (type 4): walkable surfaces of structures such as town
/// gates, bridges and stairs, whose footprints are blocked areas on the triangle mesh, joined to the triangles at
/// their entrances. A grid that crosses a block side is in both blocks' files with the same points, which is how it
/// is followed from one block into the next.
/// </para>
/// Links keep the file's <c>MaxPassRadius</c>, so a blocked link stays blocked. Positions are world coordinates;
/// heights come from the file and are the ground (or structure surface) under each point.
/// </summary>
public sealed class NavBlock
{
    /// <summary>Width of a block, in metres.</summary>
    public const float Size = 256f;

    /// <summary><see cref="LinkEdge"/> of a link to or from a waypoint: it crosses no triangle edge.</summary>
    public const byte NoEdge = 255;

    private const int GridCells = 16;
    private const float CellSize = Size / GridCells;

    /// <summary>A point this far outside a triangle (metres) still counts as on it, for points on shared edges.</summary>
    internal const float OnEdgeSlack = 0.01f;

    /// <summary>Two waypoints of neighbouring blocks this close (metres, each axis) are the same point.</summary>
    internal const float SameWaypoint = 0.1f;

    public int X { get; }
    public int Y { get; }
    public float MinX => X * Size;
    public float MinY => Y * Size;
    public int TriangleCount { get; }

    /// <summary>Triangles plus waypoints.</summary>
    public int NodeCount { get; }

    public int WaypointCount => NodeCount - TriangleCount;
    public int LinkCount => LinkTo.Length;
    public int SeamEdgeCount => Seams.Sum(s => s.Length);

    /// <summary>Rough heap size of this block, for the cache's memory budget.</summary>
    public long EstimatedBytes { get; }

    internal readonly float[] Vx, Vy, Vz;

    /// <summary>Per corner: an obstacle point the walker keeps its radius from (most are; block corners are not).</summary>
    internal readonly bool[] Obstacle;

    internal readonly int[] Corners;
    internal readonly float[] Cx, Cy, Cz;
    internal readonly bool[] Forbidden;
    internal readonly float[] NodeDepth;
    internal readonly int[] LinkStart;
    internal readonly int[] LinkTo;
    internal readonly byte[] LinkEdge;
    internal readonly float[] LinkRadius;
    internal readonly float[] LinkDepth;

    /// <summary>Seam edges per side (<see cref="NavRules.West"/> .. <see cref="NavRules.North"/>), sorted by <see cref="NavSeamEdge.Lo"/>.</summary>
    internal readonly NavSeamEdge[][] Seams;

    /// <summary>Waypoints that lie outside the block's square, where the grid goes on in a neighbour block.</summary>
    internal readonly int[] WaypointsOutside;

    /// <summary>Per triangle, the first entry in <see cref="_seamRefs"/>; a triangle has 0 to 3 seam edges.</summary>
    private readonly int[] _seamStart;
    private readonly int[] _seamRefs;
    private readonly int[] _gridStart;
    private readonly int[] _gridTriangles;
    private readonly int[] _waypointGridStart;
    private readonly int[] _waypointGrid;

    private NavBlock(int x, int y, int triangleCount, float[] vx, float[] vy, float[] vz, bool[] obstacle, int[] corners, float[] cx, float[] cy,
        float[] cz, bool[] forbidden, float[] nodeDepth, int[] linkStart, int[] linkTo, byte[] linkEdge, float[] linkRadius,
        float[] linkDepth, NavSeamEdge[][] seams, int[] seamStart, int[] seamRefs, int[] waypointsOutside)
    {
        X = x;
        Y = y;
        TriangleCount = triangleCount;
        NodeCount = cx.Length;
        Vx = vx;
        Vy = vy;
        Vz = vz;
        Obstacle = obstacle;
        Corners = corners;
        Cx = cx;
        Cy = cy;
        Cz = cz;
        Forbidden = forbidden;
        NodeDepth = nodeDepth;
        LinkStart = linkStart;
        LinkTo = linkTo;
        LinkEdge = linkEdge;
        LinkRadius = linkRadius;
        LinkDepth = linkDepth;
        Seams = seams;
        _seamStart = seamStart;
        _seamRefs = seamRefs;
        WaypointsOutside = waypointsOutside;
        (_gridStart, _gridTriangles) = BuildGrid(0, TriangleCount, TriangleBox);
        (_waypointGridStart, _waypointGrid) = BuildGrid(TriangleCount, NodeCount, PointBox);
        EstimatedBytes = Estimate();
    }

    /// <summary>
    /// Builds block (<paramref name="blockX"/>, <paramref name="blockY"/>) from its raw data. Edges within
    /// <paramref name="seamTolerance"/> metres of a side become seam edges. <paramref name="waterDepth"/> gives the
    /// water over the ground at (x, y, ground z); null is dry land everywhere. <paramref name="structureGrids"/> keeps
    /// the waypoint grids of structures too.
    /// </summary>
    public static NavBlock Build(int blockX, int blockY, NavBlockData data, float seamTolerance,
        Func<float, float, float, float> waterDepth = null, bool structureGrids = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        waterDepth ??= static (_, _, _) => 0f;
        var minX = blockX * Size;
        var minY = blockY * Size;

        var vertexCount = data.Vertices.Count;
        var vx = new float[vertexCount];
        var vy = new float[vertexCount];
        var vz = new float[vertexCount];
        var obstacle = new bool[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            var p = data.Vertices[i].Position;
            vx[i] = p.X;
            vy[i] = p.Y;
            vz[i] = p.Z;
            obstacle[i] = data.Vertices[i].Obstacle;
        }

        // Triangles: the ones centred inside the square are kept, the fringe outside is only read for its links.
        var kept = new Dictionary<int, int>();
        var fringe = new Dictionary<int, NavRawNode>();
        var triangles = new List<NavRawNode>();
        var waypoints = new List<NavRawNode>();
        var ids = new HashSet<int>();
        foreach (var node in data.Nodes)
        {
            if (!ids.Add(node.Id))
                continue;
            if (node.Type == NavRules.WaypointNodeType)
            {
                if (structureGrids)
                    waypoints.Add(node);
                continue;
            }

            if (node.Type != NavRules.TriangleNodeType || !ValidCorner(node.A, vertexCount) ||
                !ValidCorner(node.B, vertexCount) || !ValidCorner(node.C, vertexCount))
                continue;
            var centreX = (vx[node.A] + vx[node.B] + vx[node.C]) / 3f;
            var centreY = (vy[node.A] + vy[node.B] + vy[node.C]) / 3f;
            if (centreX >= minX && centreX < minX + Size && centreY >= minY && centreY < minY + Size)
            {
                kept.Add(node.Id, triangles.Count);
                triangles.Add(node);
            }
            else
            {
                fringe.Add(node.Id, node);
            }
        }

        var count = triangles.Count;
        for (var w = 0; w < waypoints.Count; w++)
            kept.Add(waypoints[w].Id, count + w);
        var nodeCount = count + waypoints.Count;
        var corners = new int[count * 3];
        var cx = new float[nodeCount];
        var cy = new float[nodeCount];
        var cz = new float[nodeCount];
        var forbidden = new bool[nodeCount];
        var nodeDepth = new float[nodeCount];
        for (var t = 0; t < count; t++)
        {
            var node = triangles[t];
            corners[t * 3] = node.A;
            corners[t * 3 + 1] = node.B;
            corners[t * 3 + 2] = node.C;
            // The file's node position is inside its triangle and on the ground; a bad one falls back to the centre.
            var a = new Vector2(vx[node.A], vy[node.A]);
            var b = new Vector2(vx[node.B], vy[node.B]);
            var c = new Vector2(vx[node.C], vy[node.C]);
            var position = new Vector2(node.Position.X, node.Position.Y);
            if (NavGeometry.Inside(position, a, b, c) < -OnEdgeSlack)
                position = (a + b + c) / 3f;
            cx[t] = position.X;
            cy[t] = position.Y;
            cz[t] = node.Position.Z;
            forbidden[t] = NavRules.IsForbidden(node.Flags);
        }

        var outside = new List<int>();
        for (var w = 0; w < waypoints.Count; w++)
        {
            var n = count + w;
            var p = waypoints[w].Position;
            cx[n] = p.X;
            cy[n] = p.Y;
            cz[n] = p.Z;
            if (p.X < minX || p.X >= minX + Size || p.Y < minY || p.Y >= minY + Size)
                outside.Add(n);
        }

        for (var n = 0; n < nodeCount; n++)
            nodeDepth[n] = MathF.Max(0f, waterDepth(cx[n], cy[n], cz[n]));

        // Links between kept nodes, in file order per source node.
        var outRadius = Fill(count * 3, float.NaN);
        var inRadius = Fill(count * 3, float.NaN);
        var internalEdge = new bool[count * 3];
        var perNode = new List<int>[nodeCount];
        for (var index = 0; index < data.Links.Count; index++)
        {
            var link = data.Links[index];
            var fromKept = kept.TryGetValue(link.From, out var from);
            var toKept = kept.TryGetValue(link.To, out var to);
            if (fromKept && toKept)
            {
                if (from < count && to < count)
                {
                    var edge = NavRules.EdgeOfCorners(link.StartIndex, link.EndIndex);
                    if (edge < 0)
                        continue;
                    internalEdge[from * 3 + edge] = true;
                    var back = EdgeWithCorners(corners, to, CornerOf(corners, from, edge), CornerOf(corners, from, (edge + 1) % 3));
                    if (back >= 0)
                        internalEdge[to * 3 + back] = true;
                }

                (perNode[from] ??= []).Add(index);
            }
            else if (fromKept && from < count && fringe.ContainsKey(link.To))
            {
                var edge = NavRules.EdgeOfCorners(link.StartIndex, link.EndIndex);
                if (edge >= 0)
                    outRadius[from * 3 + edge] = MinKnown(outRadius[from * 3 + edge], link.MaxPassRadius);
            }
            else if (toKept && to < count && fringe.TryGetValue(link.From, out var outsider))
            {
                var start = CornerOf(outsider, link.StartIndex);
                var end = CornerOf(outsider, link.EndIndex);
                var edge = start < 0 || end < 0 ? -1 : EdgeWithCorners(corners, to, start, end);
                if (edge >= 0)
                    inRadius[to * 3 + edge] = MinKnown(inRadius[to * 3 + edge], link.MaxPassRadius);
            }
        }

        var linkStart = new int[nodeCount + 1];
        for (var n = 0; n < nodeCount; n++)
            linkStart[n + 1] = linkStart[n] + (perNode[n]?.Count ?? 0);
        var linkTotal = linkStart[nodeCount];
        var linkTo = new int[linkTotal];
        var linkEdge = new byte[linkTotal];
        var linkRadius = new float[linkTotal];
        var linkDepth = new float[linkTotal];
        for (var n = 0; n < nodeCount; n++)
        {
            if (perNode[n] == null)
                continue;
            var slot = linkStart[n];
            foreach (var index in perNode[n])
            {
                var link = data.Links[index];
                var to = kept[link.To];
                linkTo[slot] = to;
                linkEdge[slot] = n < count && to < count ? (byte)NavRules.EdgeOfCorners(link.StartIndex, link.EndIndex) : NoEdge;
                linkRadius[slot] = link.MaxPassRadius;
                linkDepth[slot] = MathF.Max(0f, waterDepth(link.EdgeCenter.X, link.EdgeCenter.Y, link.EdgeCenter.Z));
                slot++;
            }
        }

        // Seam edges: triangle edges with no kept triangle across them that lie on a side.
        var sides = new List<NavSeamEdge>[4];
        var seamsOf = new List<int>[count];
        for (var t = 0; t < count; t++)
        {
            for (var edge = 0; edge < 3; edge++)
            {
                if (internalEdge[t * 3 + edge])
                    continue;
                var p = corners[t * 3 + edge];
                var q = corners[t * 3 + (edge + 1) % 3];
                var side = NavRules.SideOf(minX, minY, vx[p], vy[p], vx[q], vy[q], seamTolerance);
                if (side < 0)
                    continue;
                var alongY = side is NavRules.West or NavRules.East;
                var lo = alongY ? MathF.Min(vy[p], vy[q]) : MathF.Min(vx[p], vx[q]);
                var hi = alongY ? MathF.Max(vy[p], vy[q]) : MathF.Max(vx[p], vx[q]);
                if (hi - lo < NavRules.MinSeamOverlap)
                    continue;
                var depth = MathF.Max(0f, waterDepth((vx[p] + vx[q]) / 2f, (vy[p] + vy[q]) / 2f, (vz[p] + vz[q]) / 2f));
                (sides[side] ??= []).Add(new NavSeamEdge(t, (byte)edge, lo, hi, outRadius[t * 3 + edge], inRadius[t * 3 + edge], depth));
            }
        }

        var seams = new NavSeamEdge[4][];
        for (var side = 0; side < 4; side++)
        {
            seams[side] = (sides[side] ?? []).OrderBy(e => e.Lo).ThenBy(e => e.Hi).ThenBy(e => e.Triangle).ToArray();
            for (var i = 0; i < seams[side].Length; i++)
                (seamsOf[seams[side][i].Triangle] ??= []).Add(side << 24 | i);
        }

        var seamStart = new int[count + 1];
        for (var t = 0; t < count; t++)
            seamStart[t + 1] = seamStart[t] + (seamsOf[t]?.Count ?? 0);
        var seamRefs = new int[seamStart[count]];
        for (var t = 0; t < count; t++)
            seamsOf[t]?.CopyTo(seamRefs, seamStart[t]);

        return new NavBlock(blockX, blockY, count, vx, vy, vz, obstacle, corners, cx, cy, cz, forbidden, nodeDepth, linkStart, linkTo,
            linkEdge, linkRadius, linkDepth, seams, seamStart, seamRefs, outside.ToArray());
    }

    /// <summary>A corner of triangle <paramref name="triangle"/>, as a world position (z: the ground there).</summary>
    public Vector3 Corner(int triangle, int corner)
    {
        var v = Corners[triangle * 3 + corner];
        return new Vector3(Vx[v], Vy[v], Vz[v]);
    }

    /// <summary>True when corner <paramref name="corner"/> of a triangle is an obstacle point.</summary>
    public bool IsObstacle(int triangle, int corner) => Obstacle[Corners[triangle * 3 + corner]];

    /// <summary>A node's position: a triangle's file node position (inside it, on the ground), or a waypoint.</summary>
    public Vector3 Centre(int node) => new(Cx[node], Cy[node], Cz[node]);

    public bool IsWaypoint(int node) => node >= TriangleCount;

    public bool IsForbidden(int node) => Forbidden[node];

    /// <summary>Water over the ground at a node's position, in metres.</summary>
    public float DepthOf(int node) => NodeDepth[node];

    /// <summary>The links out of a node: target node, the source edge crossed (<see cref="NoEdge"/> for waypoints), its pass radius.</summary>
    public IEnumerable<(int To, int Edge, float Radius)> LinksOf(int node)
    {
        for (var l = LinkStart[node]; l < LinkStart[node + 1]; l++)
            yield return (LinkTo[l], LinkEdge[l], LinkRadius[l]);
    }

    /// <summary>The seam edges of a node, as (side, index into <see cref="Seams"/>[side]); none for a waypoint.</summary>
    internal ReadOnlySpan<int> SeamRefsOf(int node) => node >= TriangleCount
        ? ReadOnlySpan<int>.Empty
        : _seamRefs.AsSpan(_seamStart[node], _seamStart[node + 1] - _seamStart[node]);

    /// <summary>How far (x, y) is inside a triangle, in metres; negative outside.</summary>
    public float InsideDistance(int triangle, float x, float y)
    {
        var (a, b, c) = Flat(triangle);
        return NavGeometry.Inside(new Vector2(x, y), a, b, c);
    }

    /// <summary>
    /// The triangle (x, y) lies on, allowing <see cref="OnEdgeSlack"/> on shared edges (the one it is deepest inside
    /// wins, then the lowest number); -1 when it lies on none of this block's.
    /// </summary>
    public int Locate(float x, float y)
    {
        if (!TryCell(x, y, out var cell))
            return -1;
        // A cell lists its triangles in ascending order, so on a tie the first (lowest) one stays.
        var best = -1;
        var bestInside = float.NegativeInfinity;
        for (var i = _gridStart[cell]; i < _gridStart[cell + 1]; i++)
        {
            var t = _gridTriangles[i];
            var inside = InsideDistance(t, x, y);
            if (inside < -OnEdgeSlack || inside <= bestInside)
                continue;
            best = t;
            bestInside = inside;
        }

        return best;
    }

    /// <summary>
    /// The triangle nearest (x, y) within <paramref name="maxDistance"/> metres that <paramref name="accept"/> takes,
    /// and the point of it closest to (x, y) (nudged a centimetre inside, so it locates back to the same triangle).
    /// Ties go to the lowest triangle number. -1 when there is none.
    /// </summary>
    public int Nearest(float x, float y, float maxDistance, Func<int, bool> accept, out Vector2 point, out float distance)
    {
        point = default;
        distance = float.PositiveInfinity;
        var best = -1;
        if (!Overlaps(x, y, maxDistance))
            return -1;

        var seen = new HashSet<int>();
        var p = new Vector2(x, y);
        foreach (var cell in Cells(x, y, maxDistance))
        {
            for (var i = _gridStart[cell]; i < _gridStart[cell + 1]; i++)
            {
                var t = _gridTriangles[i];
                if (!seen.Add(t) || (accept != null && !accept(t)))
                    continue;
                var (a, b, c) = Flat(t);
                var closest = NavGeometry.ClosestOnTriangle(p, a, b, c);
                var d = Vector2.Distance(p, closest);
                if (d > maxDistance || d > distance || (d == distance && t >= best))
                    continue;
                best = t;
                distance = d;
                point = closest;
            }
        }

        if (best >= 0 && distance > 0f)
            point = Nudge(best, point);
        return best;
    }

    /// <summary>
    /// The waypoint nearest (x, y) within <paramref name="maxFlat"/> flat metres whose height is within
    /// <paramref name="maxRise"/> of <paramref name="z"/> (ties: lowest number), and its flat distance; -1 for none.
    /// </summary>
    public int NearestWaypoint(float x, float y, float z, float maxFlat, float maxRise, out float distance)
    {
        distance = float.PositiveInfinity;
        var best = -1;
        if (WaypointCount == 0 || !Overlaps(x, y, maxFlat))
            return -1;
        var p = new Vector2(x, y);
        foreach (var cell in Cells(x, y, maxFlat))
        {
            for (var i = _waypointGridStart[cell]; i < _waypointGridStart[cell + 1]; i++)
            {
                var n = _waypointGrid[i];
                if (MathF.Abs(Cz[n] - z) > maxRise)
                    continue;
                var d = Vector2.Distance(p, new Vector2(Cx[n], Cy[n]));
                if (d > maxFlat || d > distance || (d == distance && n >= best))
                    continue;
                best = n;
                distance = d;
            }
        }

        return best;
    }

    /// <summary>This block's copy of a waypoint another block's file also has (within <see cref="SameWaypoint"/>); -1 for none.</summary>
    public int SameWaypointAs(Vector3 point)
    {
        var n = NearestWaypoint(point.X, point.Y, point.Z, SameWaypoint, SameWaypoint, out _);
        return n >= 0 && MathF.Abs(Cx[n] - point.X) <= SameWaypoint && MathF.Abs(Cy[n] - point.Y) <= SameWaypoint ? n : -1;
    }

    /// <summary>Ground height at (x, y) on a triangle, from its corners.</summary>
    public float HeightAt(int triangle, float x, float y) =>
        NavGeometry.HeightOn(new Vector2(x, y), Corner(triangle, 0), Corner(triangle, 1), Corner(triangle, 2));

    private Vector2 Nudge(int triangle, Vector2 point)
    {
        var centre = new Vector2(Cx[triangle], Cy[triangle]);
        var toCentre = centre - point;
        var length = toCentre.Length();
        return length <= 0.01f ? centre : point + toCentre / length * 0.01f;
    }

    private (Vector2 A, Vector2 B, Vector2 C) Flat(int triangle)
    {
        var a = Corners[triangle * 3];
        var b = Corners[triangle * 3 + 1];
        var c = Corners[triangle * 3 + 2];
        return (new Vector2(Vx[a], Vy[a]), new Vector2(Vx[b], Vy[b]), new Vector2(Vx[c], Vy[c]));
    }

    private bool Overlaps(float x, float y, float distance) =>
        x + distance >= MinX && x - distance <= MinX + Size && y + distance >= MinY && y - distance <= MinY + Size;

    private IEnumerable<int> Cells(float x, float y, float distance)
    {
        var x0 = CellIndex(x - distance - MinX);
        var x1 = CellIndex(x + distance - MinX);
        var y0 = CellIndex(y - distance - MinY);
        var y1 = CellIndex(y + distance - MinY);
        for (var gy = y0; gy <= y1; gy++)
            for (var gx = x0; gx <= x1; gx++)
                yield return gy * GridCells + gx;
    }

    private bool TryCell(float x, float y, out int cell)
    {
        cell = -1;
        var localX = x - MinX;
        var localY = y - MinY;
        if (localX < -OnEdgeSlack || localX > Size + OnEdgeSlack || localY < -OnEdgeSlack || localY > Size + OnEdgeSlack)
            return false;
        cell = CellIndex(localY) * GridCells + CellIndex(localX);
        return true;
    }

    private static int CellIndex(float local) => Math.Clamp((int)MathF.Floor(local / CellSize), 0, GridCells - 1);

    private (float MinX, float MaxX, float MinY, float MaxY) TriangleBox(int t)
    {
        var a = Corners[t * 3];
        var b = Corners[t * 3 + 1];
        var c = Corners[t * 3 + 2];
        return (MathF.Min(Vx[a], MathF.Min(Vx[b], Vx[c])), MathF.Max(Vx[a], MathF.Max(Vx[b], Vx[c])),
            MathF.Min(Vy[a], MathF.Min(Vy[b], Vy[c])), MathF.Max(Vy[a], MathF.Max(Vy[b], Vy[c])));
    }

    private (float MinX, float MaxX, float MinY, float MaxY) PointBox(int n) => (Cx[n], Cx[n], Cy[n], Cy[n]);

    /// <summary>A 16 x 16 grid over the square listing the nodes from..to whose box touches each cell, in ascending order.</summary>
    private (int[] Start, int[] Nodes) BuildGrid(int from, int to, Func<int, (float MinX, float MaxX, float MinY, float MaxY)> box)
    {
        var cells = GridCells * GridCells;
        var start = new int[cells + 1];
        var ranges = new (int X0, int X1, int Y0, int Y1)[to - from];
        for (var n = from; n < to; n++)
        {
            var (x0, x1, y0, y1) = box(n);
            var range = (CellIndex(x0 - MinX - OnEdgeSlack), CellIndex(x1 - MinX + OnEdgeSlack),
                CellIndex(y0 - MinY - OnEdgeSlack), CellIndex(y1 - MinY + OnEdgeSlack));
            ranges[n - from] = range;
            for (var gy = range.Item3; gy <= range.Item4; gy++)
                for (var gx = range.Item1; gx <= range.Item2; gx++)
                    start[gy * GridCells + gx + 1]++;
        }

        for (var i = 0; i < cells; i++)
            start[i + 1] += start[i];
        var fill = (int[])start.Clone();
        var nodes = new int[start[cells]];
        for (var n = from; n < to; n++)
        {
            var (x0, x1, y0, y1) = ranges[n - from];
            for (var gy = y0; gy <= y1; gy++)
                for (var gx = x0; gx <= x1; gx++)
                    nodes[fill[gy * GridCells + gx]++] = n;
        }

        return (start, nodes);
    }

    private long Estimate()
    {
        const int arrayOverhead = 24;
        long bytes = 256;
        bytes += 3L * (Vx.Length * sizeof(float) + arrayOverhead) + Obstacle.Length + arrayOverhead;
        bytes += Corners.Length * sizeof(int) + arrayOverhead;
        bytes += 4L * (Cx.Length * sizeof(float) + arrayOverhead) + Forbidden.Length + arrayOverhead;
        bytes += LinkStart.Length * sizeof(int) + LinkTo.Length * (sizeof(int) + 1 + 2 * sizeof(float)) + 4 * arrayOverhead;
        bytes += Seams.Sum(s => (long)s.Length * 28 + arrayOverhead);
        bytes += (WaypointsOutside.Length + _seamStart.Length + _seamRefs.Length + _gridStart.Length + _gridTriangles.Length +
                  _waypointGridStart.Length + _waypointGrid.Length) * sizeof(int) + 7 * arrayOverhead;
        return bytes;
    }

    private static bool ValidCorner(int index, int vertexCount) => index >= 0 && index < vertexCount;

    private static int CornerOf(int[] corners, int triangle, int corner) => corners[triangle * 3 + corner];

    private static int CornerOf(NavRawNode node, int corner) => corner switch
    {
        0 => node.A,
        1 => node.B,
        2 => node.C,
        _ => -1,
    };

    private static int EdgeWithCorners(int[] corners, int triangle, int a, int b)
    {
        for (var edge = 0; edge < 3; edge++)
        {
            var p = corners[triangle * 3 + edge];
            var q = corners[triangle * 3 + (edge + 1) % 3];
            if ((p == a && q == b) || (p == b && q == a))
                return edge;
        }

        return -1;
    }

    private static float MinKnown(float current, float value) => float.IsNaN(current) ? value : MathF.Min(current, value);

    private static float[] Fill(int length, float value)
    {
        var array = new float[length];
        Array.Fill(array, value);
        return array;
    }
}
