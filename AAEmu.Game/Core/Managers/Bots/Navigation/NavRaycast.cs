using System.Numerics;

namespace AAEmu.Game.Core.Managers.Bots.Navigation;

/// <summary>
/// Straight-line checks over the mesh, used to shorten a funnelled route. A* picks one corridor among many of about
/// the same cost (in an open field a staircase of triangles costs what the diagonal does), and the funnel can only
/// straighten a route inside its corridor; walking the triangles under a straight leg shows whether a later
/// waypoint can be reached directly. A leg is clear when every link it crosses is one the search would use (not
/// blocked, wide enough, not into a blocked area from outside, not into water deeper than allowed) and it keeps
/// the walker's radius from every obstacle point it passes (the triangles' corners, block corners aside).
/// </summary>
public static class NavRaycast
{
    /// <summary>Most triangles one leg may cross before it is treated as not clear.</summary>
    private const int MaxSteps = 4096;

    /// <summary>
    /// True when a walker of <paramref name="options"/>' radius can go straight from <paramref name="from"/>, on node
    /// <paramref name="node"/> of <paramref name="block"/>, to <paramref name="to"/>, in water no deeper than
    /// <paramref name="maxDepth"/>. A structure waypoint is never the start of a clear leg.
    /// </summary>
    public static bool Clear(Func<int, int, NavBlock> blocks, NavBlock block, int node, Vector2 from, Vector2 to,
        NavOptions options, float maxDepth)
    {
        if (block == null || node < 0 || block.IsWaypoint(node))
            return false;
        var radius = options.ActorRadius;
        var clearance = MathF.Max(0f, radius - 0.01f);
        var seamLinks = new List<NavSeamLink>();
        var reached = -1f;
        for (var step = 0; step < MaxSteps; step++)
        {
            if (block.InsideDistance(node, to.X, to.Y) >= -NavBlock.OnEdgeSlack)
                return true;

            // The leg leaves the triangle through the edge it crosses furthest along.
            var exit = -1;
            var exitAt = reached;
            var exitAlongEdge = 0f;
            for (var edge = 0; edge < 3; edge++)
            {
                var p = NavGeometry.Xy(block.Corner(node, edge));
                var q = NavGeometry.Xy(block.Corner(node, (edge + 1) % 3));
                if (NavGeometry.Crossing(from, to, p, q, out var along, out var alongEdge) && along > exitAt + 1e-6f)
                {
                    exit = edge;
                    exitAt = along;
                    exitAlongEdge = alongEdge;
                }
            }

            if (exit < 0)
                return false;
            reached = exitAt;
            var start = block.Corner(node, exit);
            var end = block.Corner(node, (exit + 1) % 3);
            // Keep clear of the corners of the edge crossed that are obstacle points.
            if ((block.IsObstacle(node, exit) && NavGeometry.ToSegment(NavGeometry.Xy(start), from, to) < clearance) ||
                (block.IsObstacle(node, (exit + 1) % 3) && NavGeometry.ToSegment(NavGeometry.Xy(end), from, to) < clearance))
                return false;

            var fromForbidden = block.IsForbidden(node);
            var next = -1;
            for (var l = block.LinkStart[node]; l < block.LinkStart[node + 1]; l++)
            {
                if (block.LinkEdge[l] != exit)
                    continue;
                var to2 = block.LinkTo[l];
                if (!NavRules.Passable(block.LinkRadius[l], radius) || !NavRules.MayEnter(fromForbidden, block.Forbidden[to2]) ||
                    !NavRules.WaterAllows(MathF.Max(block.LinkDepth[l], block.NodeDepth[to2]), maxDepth))
                    return false;
                next = to2;
                break;
            }

            if (next >= 0)
            {
                node = next;
                continue;
            }

            if (!TryCrossSeam(blocks, ref block, ref node, exit, Vector2.Lerp(NavGeometry.Xy(start), NavGeometry.Xy(end), exitAlongEdge),
                    radius, maxDepth, seamLinks))
                return false;
        }

        return false;
    }

    /// <summary>
    /// Drops waypoints a walker can skip: from each kept waypoint, the furthest following one reachable by a clear
    /// leg (stopping at the first that is not) is the next kept. Points in <paramref name="anchors"/> (structure
    /// waypoints, whose heights matter) are always kept and never skipped over. The first point lies on node
    /// <paramref name="startNode"/> of <paramref name="startBlock"/>.
    /// </summary>
    public static List<Vector3> Shortcut(Func<int, int, NavBlock> blocks, NavBlock startBlock, int startNode,
        IReadOnlyList<Vector3> waypoints, NavOptions options, float maxDepth, IReadOnlySet<Vector3> anchors)
    {
        var kept = new List<Vector3> { waypoints[0] };
        var at = 0;
        while (at < waypoints.Count - 1)
        {
            var next = at + 1;
            if (!anchors.Contains(waypoints[at]))
            {
                var (block, node) = at == 0 ? (startBlock, startNode) : Under(blocks, waypoints[at]);
                for (var k = at + 2; k < waypoints.Count; k++)
                {
                    if (anchors.Contains(waypoints[k - 1]) ||
                        !Clear(blocks, block, node, NavGeometry.Xy(waypoints[at]), NavGeometry.Xy(waypoints[k]), options, maxDepth))
                        break;
                    next = k;
                }
            }

            kept.Add(waypoints[next]);
            at = next;
        }

        return kept;
    }

    /// <summary>
    /// Most metres the mesh under a leg may rise or fall away from the straight line between the leg's ends before a
    /// waypoint is added there (<see cref="FollowHeights"/>): well inside the 3 m a walker treats as on the ground.
    /// </summary>
    public const float HeightTolerance = 1.5f;

    /// <summary>
    /// Adds waypoints where the mesh under a leg rises or falls more than <paramref name="tolerance"/> metres away from
    /// the straight line between the leg's ends (a hill or a dip crossed in one straight leg), at the mesh's own height
    /// there, so a walker following the waypoints stays on the ground: over a hill rather than through it, down into a
    /// dip rather than floating over it, and along a bridge deck where the mesh is the deck. The added points lie on
    /// the legs, so the route on the map is unchanged. A leg that starts or ends at a structure waypoint (in
    /// <paramref name="anchors"/>) keeps its heights. The first point lies on node <paramref name="startNode"/> of
    /// <paramref name="startBlock"/>.
    /// </summary>
    public static List<Vector3> FollowHeights(Func<int, int, NavBlock> blocks, NavBlock startBlock, int startNode,
        IReadOnlyList<Vector3> waypoints, IReadOnlySet<Vector3> anchors, float tolerance = HeightTolerance)
    {
        var result = new List<Vector3>(waypoints.Count) { waypoints[0] };
        var block = startBlock;
        var node = startNode;
        var samples = new List<(float T, float Z)>();
        for (var i = 1; i < waypoints.Count; i++)
        {
            var from = waypoints[i - 1];
            var to = waypoints[i];
            if (anchors.Contains(from) || anchors.Contains(to))
            {
                result.Add(to);
                block = null;
                continue;
            }

            if (block == null || node < 0)
                (block, node) = Under(blocks, from);
            if (block == null || node < 0 || block.IsWaypoint(node))
            {
                result.Add(to);
                block = null;
                continue;
            }

            samples.Clear();
            var reached = HeightsAlong(blocks, ref block, ref node, from, to, samples);
            AddWhereOff(from, to, samples, 0, samples.Count, 0f, from.Z, 1f, to.Z, tolerance, result);
            result.Add(to);
            if (!reached)
                block = null;
        }

        return result;
    }

    /// <summary>
    /// The mesh's height where a straight leg crosses each triangle edge, as (fraction of the leg, height), following
    /// the triangles' own links from <paramref name="node"/> (and seams into the next block), so a leg over a bridge
    /// stays on the bridge. True when it got to the leg's end; false when it stopped short (at a structure grid, a
    /// blocked seam, or an edge with no triangle beyond), with the heights it found so far.
    /// </summary>
    private static bool HeightsAlong(Func<int, int, NavBlock> blocks, ref NavBlock block, ref int node, Vector3 from, Vector3 to,
        List<(float T, float Z)> samples)
    {
        var start2 = NavGeometry.Xy(from);
        var end2 = NavGeometry.Xy(to);
        var seamLinks = new List<NavSeamLink>();
        var reached = -1f;
        for (var step = 0; step < MaxSteps; step++)
        {
            if (block.InsideDistance(node, to.X, to.Y) >= -NavBlock.OnEdgeSlack)
                return true;

            var exit = -1;
            var exitAt = reached;
            var exitAlongEdge = 0f;
            for (var edge = 0; edge < 3; edge++)
            {
                var p = NavGeometry.Xy(block.Corner(node, edge));
                var q = NavGeometry.Xy(block.Corner(node, (edge + 1) % 3));
                if (NavGeometry.Crossing(start2, end2, p, q, out var along, out var alongEdge) && along > exitAt + 1e-6f)
                {
                    exit = edge;
                    exitAt = along;
                    exitAlongEdge = alongEdge;
                }
            }

            if (exit < 0)
                return false;
            reached = exitAt;
            var a = block.Corner(node, exit);
            var b = block.Corner(node, (exit + 1) % 3);
            samples.Add((exitAt, a.Z + (b.Z - a.Z) * exitAlongEdge));

            var next = -1;
            for (var l = block.LinkStart[node]; l < block.LinkStart[node + 1]; l++)
            {
                if (block.LinkEdge[l] != exit)
                    continue;
                next = block.LinkTo[l];
                break;
            }

            if (next >= 0)
            {
                if (block.IsWaypoint(next))
                    return false;
                node = next;
                continue;
            }

            if (!TryCrossSeam(blocks, ref block, ref node, exit, Vector2.Lerp(NavGeometry.Xy(a), NavGeometry.Xy(b), exitAlongEdge),
                    0f, float.PositiveInfinity, seamLinks))
                return false;
        }

        return false;
    }

    /// <summary>
    /// Douglas-Peucker on the heights: the sample furthest off the straight line from (<paramref name="t0"/>,
    /// <paramref name="z0"/>) to (<paramref name="t1"/>, <paramref name="z1"/>), when further than the tolerance,
    /// becomes a waypoint, and each side is looked at again.
    /// </summary>
    private static void AddWhereOff(Vector3 from, Vector3 to, List<(float T, float Z)> samples, int lo, int hi,
        float t0, float z0, float t1, float z1, float tolerance, List<Vector3> output)
    {
        var worst = -1;
        var worstOff = tolerance;
        for (var k = lo; k < hi; k++)
        {
            var (t, z) = samples[k];
            if (t <= t0 || t >= t1)
                continue;
            var off = MathF.Abs(z - (z0 + (z1 - z0) * (t - t0) / (t1 - t0)));
            if (off <= worstOff)
                continue;
            worst = k;
            worstOff = off;
        }

        if (worst < 0)
            return;

        var (tw, zw) = samples[worst];
        AddWhereOff(from, to, samples, lo, worst, t0, z0, tw, zw, tolerance, output);
        var at = Vector3.Lerp(from, to, tw);
        output.Add(at with { Z = zw });
        AddWhereOff(from, to, samples, worst + 1, hi, tw, zw, t1, z1, tolerance, output);
    }

    /// <summary>The triangle under a waypoint, in the block that holds it; (null, -1) when there is none.</summary>
    private static (NavBlock Block, int Node) Under(Func<int, int, NavBlock> blocks, Vector3 point)
    {
        var x = (int)MathF.Floor(point.X / NavBlock.Size);
        var y = (int)MathF.Floor(point.Y / NavBlock.Size);
        var block = x < 0 || y < 0 ? null : blocks(x, y);
        return block == null ? (null, -1) : (block, block.Locate(point.X, point.Y));
    }

    /// <summary>Follows the leg across a block side at <paramref name="crossing"/>, when a usable seam link covers it.</summary>
    private static bool TryCrossSeam(Func<int, int, NavBlock> blocks, ref NavBlock block, ref int node, int exit, Vector2 crossing,
        float radius, float maxDepth, List<NavSeamLink> seamLinks)
    {
        foreach (var seam in block.SeamRefsOf(node))
        {
            var side = seam >> 24;
            var edge = block.Seams[side][seam & 0xFFFFFF];
            if (edge.Edge != exit)
                continue;
            var (dx, dy) = NavRules.Step(side);
            var neighbour = block.X + dx < 0 || block.Y + dy < 0 ? null : blocks(block.X + dx, block.Y + dy);
            if (neighbour == null)
                return false;
            seamLinks.Clear();
            NavSeamStitcher.Links(block, side, edge, neighbour, seamLinks);
            var along = side is NavRules.West or NavRules.East ? crossing.Y : crossing.X;
            foreach (var link in seamLinks)
            {
                var lo = side is NavRules.West or NavRules.East ? link.Start.Y : link.Start.X;
                var hi = side is NavRules.West or NavRules.East ? link.End.Y : link.End.X;
                if (along < lo || along > hi)
                    continue;
                if (!NavRules.Passable(link.Radius, radius) ||
                    !NavRules.WaterAllows(MathF.Max(link.Depth, neighbour.NodeDepth[link.Triangle]), maxDepth))
                    return false;
                block = neighbour;
                node = link.Triangle;
                return true;
            }

            return false;
        }

        return false;
    }
}
