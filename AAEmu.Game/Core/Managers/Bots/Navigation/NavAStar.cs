using System.Diagnostics;
using System.Numerics;

namespace AAEmu.Game.Core.Managers.Bots.Navigation;

/// <summary>A gap a route passes through, with its ends as seen walking through it.</summary>
public readonly record struct NavPortal(Vector3 Left, Vector3 Right);

/// <summary>
/// A node (triangle or structure waypoint) of a found corridor, and the pass radius of the link the route entered it
/// by (NaN for the start).
/// </summary>
public readonly record struct NavCorridorStep(int BlockX, int BlockY, int Node, float EnteredRadius);

/// <summary>What one search found: the corridor of nodes and the portals between them, or why there is none.</summary>
public sealed class NavSearchResult
{
    public bool Found { get; init; }
    public string Reason { get; init; }
    public int Expanded { get; init; }
    public int BlocksTouched { get; init; }
    public IReadOnlyList<NavPortal> Portals { get; init; } = [];
    public IReadOnlyList<NavCorridorStep> Corridor { get; init; } = [];
}

/// <summary>
/// A* over the triangles of the mesh (DESIGN 8.3), across blocks through <see cref="NavSeamStitcher"/>. A triangle is
/// reached at a point on the portal it was entered by (where the straight line on to the goal crosses the portal,
/// else the nearer usable end), costs are flat distances between those points, and the heuristic is the flat
/// distance on to the goal. Ties go to the lower node key, so the same mesh and query always give the same corridor.
/// Links are filtered by <see cref="NavRules"/>: never a blocked link, never one narrower than the walker, never
/// into a blocked area from outside, never into water deeper than allowed. Structure waypoints, when the blocks
/// have them, are points the route passes through exactly.
/// </summary>
public static class NavAStar
{
    private struct Node
    {
        public int Slot;
        public int At;
        public float G;
        public float F;
        public int Parent;
        public Vector2 Entry;
        public NavPortal Portal;
        public float Radius;
        public bool Closed;
    }

    private sealed class PriorityComparer : IComparer<(float F, long Key)>
    {
        public static readonly PriorityComparer Instance = new();

        public int Compare((float F, long Key) x, (float F, long Key) y)
        {
            var byCost = x.F.CompareTo(y.F);
            return byCost != 0 ? byCost : x.Key.CompareTo(y.Key);
        }
    }

    /// <summary>
    /// Searches from <paramref name="start"/> on <paramref name="startNode"/> of <paramref name="startBlock"/>
    /// to <paramref name="goal"/> on <paramref name="goalNode"/> of <paramref name="goalBlock"/>.
    /// <paramref name="blocks"/> hands out neighbour blocks (null where there is none) and may load them, so the
    /// deadline in <paramref name="options"/>, measured on <paramref name="clock"/>, covers loading too.
    /// </summary>
    public static NavSearchResult Search(Func<int, int, NavBlock> blocks, NavBlock startBlock, int startNode, Vector3 start,
        NavBlock goalBlock, int goalNode, Vector3 goal, NavOptions options, Stopwatch clock)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(startBlock);
        ArgumentNullException.ThrowIfNull(goalBlock);
        options ??= NavOptions.Default;
        clock ??= Stopwatch.StartNew();

        var slots = new List<NavBlock>();
        var slotOf = new Dictionary<(int, int), int>();
        var missing = new HashSet<(int, int)>();
        int SlotFor(NavBlock block)
        {
            if (slotOf.TryGetValue((block.X, block.Y), out var slot))
                return slot;
            slotOf.Add((block.X, block.Y), slots.Count);
            slots.Add(block);
            return slots.Count - 1;
        }

        NavBlock Neighbour(NavBlock block, int side)
        {
            var (dx, dy) = NavRules.Step(side);
            return BlockAt(block.X + dx, block.Y + dy);
        }

        NavBlock BlockAt(int x, int y)
        {
            var key = (x, y);
            if (slotOf.TryGetValue(key, out var slot))
                return slots[slot];
            if (missing.Contains(key))
                return null;
            var neighbour = key.Item1 < 0 || key.Item2 < 0 ? null : blocks(key.Item1, key.Item2);
            if (neighbour == null || neighbour.TriangleCount == 0)
            {
                missing.Add(key);
                return null;
            }

            SlotFor(neighbour);
            return neighbour;
        }

        var actor = options.ActorRadius;
        var goalFlat = NavGeometry.Xy(goal);
        var maxDepth = NavRules.EffectiveMaxDepth(options.MaxWaterDepth, startBlock.DepthOf(startNode));
        var goalSlot = SlotFor(goalBlock);
        var nodes = new Node[256];
        var count = 0;
        var index = new Dictionary<long, int>();
        var open = new PriorityQueue<int, (float F, long Key)>(PriorityComparer.Instance);
        var seamLinks = new List<NavSeamLink>();

        var startSlot = SlotFor(startBlock);
        var startFlat = NavGeometry.Xy(start);
        AddNode(startSlot, startNode, 0f, Vector2.Distance(startFlat, goalFlat), -1, startFlat, default, float.NaN);

        var expanded = 0;
        while (open.TryDequeue(out var current, out var priority))
        {
            if (nodes[current].Closed || priority.F > nodes[current].F)
                continue;
            nodes[current].Closed = true;
            expanded++;

            if (nodes[current].Slot == goalSlot && nodes[current].At == goalNode)
                return Found(current);
            if (expanded > options.MaxSearchNodes)
                return Failed(NavReasons.NodeLimit(options.MaxSearchNodes));
            if ((expanded & 63) == 0 && clock.Elapsed > options.Deadline)
                return Failed(NavReasons.Deadline(options.Deadline));

            var block = slots[nodes[current].Slot];
            var at = nodes[current].At;
            var fromForbidden = block.Forbidden[at];
            for (var l = block.LinkStart[at]; l < block.LinkStart[at + 1]; l++)
            {
                var to = block.LinkTo[l];
                if (!NavRules.Passable(block.LinkRadius[l], actor) || !NavRules.MayEnter(fromForbidden, block.Forbidden[to]))
                    continue;
                if (!NavRules.WaterAllows(MathF.Max(block.LinkDepth[l], block.NodeDepth[to]), maxDepth))
                    continue;
                var edge = block.LinkEdge[l];
                NavPortal portal;
                if (edge == NavBlock.NoEdge)
                {
                    // On or off a structure's grid: the route passes through the waypoint itself.
                    var point = block.Centre(block.IsWaypoint(to) ? to : at);
                    portal = new NavPortal(point, point);
                }
                else
                {
                    portal = Orient(block.Corner(at, edge), block.Corner(at, (edge + 1) % 3),
                        block.Corner(at, (edge + 2) % 3));
                }

                Relax(current, nodes[current].Slot, to, portal, block.LinkRadius[l]);
            }

            if (block.IsWaypoint(at))
            {
                // A grid running on into the next block: that block's file has the same point.
                var here = block.Centre(at);
                var x = (int)MathF.Floor(here.X / NavBlock.Size);
                var y = (int)MathF.Floor(here.Y / NavBlock.Size);
                if (x == block.X && y == block.Y)
                    continue;
                var next = x < 0 || y < 0 ? null : BlockAt(x, y);
                var same = next?.SameWaypointAs(here) ?? -1;
                if (same >= 0)
                    Relax(current, SlotFor(next), same, new NavPortal(here, here), NavRules.SameWaypointRadius);
                continue;
            }

            foreach (var seam in block.SeamRefsOf(at))
            {
                var side = seam >> 24;
                var edge = block.Seams[side][seam & 0xFFFFFF];
                var neighbour = Neighbour(block, side);
                if (neighbour == null)
                    continue;
                if (clock.Elapsed > options.Deadline)
                    return Failed(NavReasons.Deadline(options.Deadline));
                seamLinks.Clear();
                NavSeamStitcher.Links(block, side, edge, neighbour, seamLinks);
                var back = block.Corner(at, (edge.Edge + 2) % 3);
                var neighbourSlot = SlotFor(neighbour);
                foreach (var link in seamLinks)
                {
                    if (!NavRules.Passable(link.Radius, actor) ||
                        !NavRules.WaterAllows(MathF.Max(link.Depth, neighbour.NodeDepth[link.Triangle]), maxDepth))
                        continue;
                    Relax(current, neighbourSlot, link.Triangle, Orient(link.Start, link.End, back), link.Radius);
                }
            }
        }

        return Failed(NavReasons.Unreachable);

        void AddNode(int slot, int node, float g, float h, int parent, Vector2 entry, NavPortal portal, float radius)
        {
            if (count == nodes.Length)
                Array.Resize(ref nodes, nodes.Length * 2);
            var key = Key(slot, node);
            nodes[count] = new Node
            {
                Slot = slot, At = node, G = g, F = g + h, Parent = parent, Entry = entry, Portal = portal,
                Radius = radius,
            };
            index[key] = count;
            open.Enqueue(count, (g + h, key));
            count++;
        }

        void Relax(int parent, int slot, int node, NavPortal portal, float radius)
        {
            var key = Key(slot, node);
            var known = index.TryGetValue(key, out var existing);
            if (known && nodes[existing].Closed)
                return;
            var entry = EntryPoint(nodes[parent].Entry, goalFlat, portal, actor);
            var g = nodes[parent].G + Vector2.Distance(nodes[parent].Entry, entry);
            var h = Vector2.Distance(entry, goalFlat);
            if (!known)
            {
                AddNode(slot, node, g, h, parent, entry, portal, radius);
                return;
            }

            if (g >= nodes[existing].G)
                return;
            ref var better = ref nodes[existing];
            better.G = g;
            better.F = g + h;
            better.Parent = parent;
            better.Entry = entry;
            better.Portal = portal;
            better.Radius = radius;
            open.Enqueue(existing, (better.F, key));
        }

        NavSearchResult Found(int last)
        {
            var chain = new List<int>();
            for (var at = last; at >= 0; at = nodes[at].Parent)
                chain.Add(at);
            chain.Reverse();
            var portals = new List<NavPortal>(chain.Count - 1);
            var corridor = new List<NavCorridorStep>(chain.Count);
            foreach (var at in chain)
            {
                var block = slots[nodes[at].Slot];
                corridor.Add(new NavCorridorStep(block.X, block.Y, nodes[at].At, nodes[at].Radius));
                if (nodes[at].Parent >= 0)
                    portals.Add(nodes[at].Portal);
            }

            return new NavSearchResult
            {
                Found = true, Expanded = expanded, BlocksTouched = slots.Count, Portals = portals, Corridor = corridor,
            };
        }

        NavSearchResult Failed(string reason) =>
            new() { Found = false, Reason = reason, Expanded = expanded, BlocksTouched = slots.Count };
    }

    /// <summary>
    /// Portal p-q of a triangle whose third corner is <paramref name="back"/>, with its ends named as seen from inside
    /// that triangle looking out through it.
    /// </summary>
    public static NavPortal Orient(Vector3 p, Vector3 q, Vector3 back) =>
        NavGeometry.Cross(NavGeometry.Xy(back), NavGeometry.Xy(p), NavGeometry.Xy(q)) > 0f
            ? new NavPortal(q, p)
            : new NavPortal(p, q);

    /// <summary>
    /// Where a route from <paramref name="from"/> heading for <paramref name="goal"/> crosses a portal: where the
    /// straight line crosses it, else the end that makes the shorter detour. The ends are first pulled in by the
    /// walker's radius (at most to the middle), so the point keeps clear of the corners.
    /// </summary>
    public static Vector2 EntryPoint(Vector2 from, Vector2 goal, NavPortal portal, float actorRadius)
    {
        var (shrunkLeft, shrunkRight) = NavFunnel.Shrink(portal.Left, portal.Right, actorRadius);
        var left = NavGeometry.Xy(shrunkLeft);
        var right = NavGeometry.Xy(shrunkRight);
        if (NavGeometry.Crossing(from, goal, left, right, out var along))
            return Vector2.Lerp(left, right, along);
        var viaLeft = Vector2.Distance(from, left) + Vector2.Distance(left, goal);
        var viaRight = Vector2.Distance(from, right) + Vector2.Distance(right, goal);
        return viaRight < viaLeft ? right : left;
    }

    private static long Key(int slot, int node) => (long)slot << 32 | (uint)node;
}
