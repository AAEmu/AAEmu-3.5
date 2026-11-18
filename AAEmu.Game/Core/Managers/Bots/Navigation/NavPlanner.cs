using System.Diagnostics;
using System.Numerics;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Core.Managers.Bots.Navigation;

/// <summary>
/// The planner's own settings: the seam tolerance blocks are built with, and how far an off-mesh start or goal is
/// moved onto the mesh (<c>Navigation.StartSnapMeters</c>, <c>GoalSnapMeters</c>).
/// </summary>
public readonly record struct NavPlannerSettings(float SeamTolerance, float StartSnap, float GoalSnap)
{
    public static NavPlannerSettings Default { get; } = new(0.1f, 8f, 8f);
}

/// <summary>Everything one plan found or why it failed, for tests, logs and the bot command.</summary>
public sealed class NavPlanReport
{
    /// <summary>The route; null when the plan failed.</summary>
    public NavPath Path { get; init; }

    /// <summary>Why the plan failed (<see cref="NavReasons"/>); null on success.</summary>
    public string Reason { get; init; }

    /// <summary>The start and goal as planned: moved onto the walkable mesh when they were off it.</summary>
    public Vector3 Start { get; init; }

    /// <inheritdoc cref="Start"/>
    public Vector3 Goal { get; init; }

    public int Expanded { get; init; }
    public int BlocksTouched { get; init; }
    public IReadOnlyList<NavPortal> Portals { get; init; } = [];
    public IReadOnlyList<NavCorridorStep> Corridor { get; init; } = [];
    public TimeSpan Elapsed { get; init; }
}

/// <summary>
/// Plans walking routes over the client navigation mesh (DESIGN 8.3): finds the nodes under the start and the goal,
/// runs <see cref="NavAStar"/> across blocks from <see cref="NavCache"/>, smooths the corridor with
/// <see cref="NavFunnel"/>, drops the waypoints a straight leg can skip (<see cref="NavRaycast"/>), adds points where the
/// ground under a long leg rises or falls away from it (<see cref="NavRaycast.FollowHeights"/>) and lists the zone
/// keys on the way (<see cref="NavZoneKeys"/>).
/// <para>
/// A start off the mesh is moved to the nearest mesh within <see cref="NavPlannerSettings.StartSnap"/>; a start in a
/// blocked area or deep water may leave it. A goal on ground the walker may not use (outside the mesh, inside a
/// blocked area such as a building's footprint, or in water deeper than allowed) is moved to the nearest walkable
/// point within <see cref="NavPlannerSettings.GoalSnap"/>; the last waypoint is then that point, and the caller
/// compares it with its stop distance.
/// </para>
/// Planning reads blocks and can take up to the deadline, so it never runs on the bot tick thread: the bots use
/// <see cref="NavPlanQueue"/>, and a thread marked with <see cref="MarkTickThread"/> is refused at once.
/// </summary>
public sealed class NavPlanner : INavPlanner
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    [ThreadStatic]
    private static bool _isTickThread;

    private static int _tickThreadWarned;

    private readonly NavCache _cache;

    public NavPlanner(NavCache cache, NavPlannerSettings? settings = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        Settings = settings ?? NavPlannerSettings.Default;
    }

    private static readonly Lazy<NavPlanner> Live =
        new(() => new NavPlanner(NavCache.Instance, AAEmu.Game.Models.Game.BotNavigationConfig.Current.PlannerSettings()));

    /// <summary>The live server's planner, over <see cref="NavCache.Instance"/>.</summary>
    public static NavPlanner Instance => Live.Value;

    public NavPlannerSettings Settings { get; }

    /// <summary>The blocks this planner reads.</summary>
    public NavCache Cache => _cache;

    /// <summary>
    /// Marks the calling thread as the bot tick thread: a plan asked for on it afterwards fails at once with
    /// <see cref="NavReasons.TickThread"/> (and a warning, once) instead of stalling every bot.
    /// </summary>
    public static void MarkTickThread() => _isTickThread = true;

    /// <summary>Undoes <see cref="MarkTickThread"/> for the calling thread.</summary>
    public static void UnmarkTickThread() => _isTickThread = false;

    public bool TryPlan(WorldTemplate world, Vector3 from, Vector3 to, NavOptions options, out NavPath path, out string reason)
    {
        var report = Plan(world, from, to, options);
        path = report.Path;
        reason = report.Reason;
        return path != null;
    }

    /// <summary><see cref="TryPlan"/>, with everything the search found.</summary>
    public NavPlanReport Plan(WorldTemplate world, Vector3 from, Vector3 to, NavOptions options = null)
    {
        var clock = Stopwatch.StartNew();
        if (_isTickThread)
        {
            if (Interlocked.Exchange(ref _tickThreadWarned, 1) == 0)
                Logger.Warn("A path plan was asked for on the bot tick thread and refused; plans go through NavPlanQueue");
            return Failed(NavReasons.TickThread, from, to, clock);
        }

        if (world == null)
            return Failed(NavReasons.NoWorld, from, to, clock);

        // Blocks are fetched once per plan, so a block evicted meanwhile stays the same for this whole search.
        var held = new Dictionary<(int, int), NavBlock>();
        NavBlock Block(int x, int y)
        {
            if (!held.TryGetValue((x, y), out var block))
            {
                block = _cache.Get(world, x, y);
                held.Add((x, y), block);
            }

            return block;
        }

        return Plan(Block, from, to, options, Settings, world, clock);
    }

    /// <summary>
    /// <see cref="Plan(WorldTemplate, Vector3, Vector3, NavOptions)"/> over blocks handed out by
    /// <paramref name="blocks"/> (null where the world has none), for a caller that holds its own blocks instead of a
    /// <see cref="NavCache"/> and may have no <see cref="WorldTemplate"/> (the headless zone host plans over the
    /// blocks of one zone; without a world the route lists no zone keys). <paramref name="blocks"/> must hand out the
    /// same block object for the same (x, y) for the whole call.
    /// </summary>
    public static NavPlanReport Plan(Func<int, int, NavBlock> blocks, Vector3 from, Vector3 to, NavOptions options,
        NavPlannerSettings settings, WorldTemplate world = null, Stopwatch clock = null)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        clock ??= Stopwatch.StartNew();
        options ??= NavOptions.Default;
        if (!TryLocate(blocks, from, settings.StartSnap, null, out var start))
            return Failed(NavReasons.NoStartMesh, from, to, clock);

        var maxDepth = NavRules.EffectiveMaxDepth(options.MaxWaterDepth, start.Block.DepthOf(start.Node));
        bool Walkable(NavBlock block, int node) =>
            (block == start.Block && node == start.Node) ||
            (!block.IsForbidden(node) && NavRules.WaterAllows(block.DepthOf(node), maxDepth));
        if (!TryLocate(blocks, to, settings.GoalSnap, Walkable, out var goal))
        {
            var reason = TryLocate(blocks, to, settings.GoalSnap, null, out _)
                ? NavReasons.GoalNotWalkable(settings.GoalSnap)
                : NavReasons.NoGoalMesh;
            return Failed(reason, start.Point, to, clock);
        }

        if (clock.Elapsed > options.Deadline)
            return Failed(NavReasons.Deadline(options.Deadline), start.Point, goal.Point, clock);

        NavSearchResult search;
        List<Vector3> waypoints;
        IReadOnlySet<Vector3> anchors = new HashSet<Vector3>();
        if (start.Block == goal.Block && start.Node == goal.Node)
        {
            search = new NavSearchResult
            {
                Found = true, Expanded = 1, BlocksTouched = 1,
                Corridor = [new NavCorridorStep(start.Block.X, start.Block.Y, start.Node, float.NaN)],
            };
            waypoints = [start.Point, goal.Point];
        }
        else
        {
            search = NavAStar.Search(blocks, start.Block, start.Node, start.Point, goal.Block, goal.Node, goal.Point,
                options, clock);
            if (!search.Found)
            {
                Logger.Debug("Nav plan {0} -> {1} failed: {2} ({3} nodes, {4} blocks, {5:0.0} ms)", from, to, search.Reason,
                    search.Expanded, search.BlocksTouched, clock.Elapsed.TotalMilliseconds);
                return new NavPlanReport
                {
                    Reason = search.Reason, Start = start.Point, Goal = goal.Point, Expanded = search.Expanded,
                    BlocksTouched = search.BlocksTouched, Elapsed = clock.Elapsed,
                };
            }

            waypoints = NavFunnel.StringPull(start.Point, goal.Point, search.Portals, options.ActorRadius);
            anchors = search.Portals.Where(p => p.Left == p.Right).Select(p => p.Left).ToHashSet();
            waypoints = NavRaycast.Shortcut(blocks, start.Block, start.Node, waypoints, options, maxDepth, anchors);
            // A long straight leg over a hill or a dip gets points at the mesh's own height, so the walker following
            // it stays on the ground.
            waypoints = NavRaycast.FollowHeights(blocks, start.Block, start.Node, waypoints, anchors);
        }

        var length = 0f;
        for (var i = 1; i < waypoints.Count; i++)
            length += Vector3.Distance(waypoints[i - 1], waypoints[i]);
        var onStructure = OnStructure(waypoints, anchors, start.Block.IsWaypoint(start.Node), goal.Block.IsWaypoint(goal.Node));
        var path = new NavPath(waypoints, NavZoneKeys.Along(world, waypoints), length, onStructure);
        return new NavPlanReport
        {
            Path = path, Start = start.Point, Goal = goal.Point, Expanded = search.Expanded,
            BlocksTouched = search.BlocksTouched, Portals = search.Portals, Corridor = search.Corridor, Elapsed = clock.Elapsed,
        };
    }

    private readonly record struct Located(NavBlock Block, int Node, Vector3 Point);

    /// <summary>
    /// Which waypoints stand on a structure's grid (<see cref="NavPath.OnStructure"/>): the structure waypoints the
    /// route passes through, and the start and the goal when they were found on one. Null when none does.
    /// </summary>
    private static IReadOnlyList<bool> OnStructure(IReadOnlyList<Vector3> waypoints, IReadOnlySet<Vector3> anchors,
        bool startOnStructure, bool goalOnStructure)
    {
        var flags = new bool[waypoints.Count];
        var any = false;
        for (var i = 0; i < flags.Length; i++)
        {
            flags[i] = anchors.Contains(waypoints[i]) || (i == 0 && startOnStructure) || (i == flags.Length - 1 && goalOnStructure);
            any |= flags[i];
        }

        return any ? flags : null;
    }

    /// <summary>A point within this many flat metres of a structure waypoint, at about its height, stands on the structure.</summary>
    private const float StructureReach = 2f;

    /// <summary>Most height difference, in metres, between a point and a structure waypoint it may stand on.</summary>
    private const float StructureRise = 2.5f;

    /// <summary>
    /// The node under <paramref name="point"/> when <paramref name="accept"/> takes it: a structure waypoint when the
    /// point stands on a structure (closer in height to it than to the ground, or over a blocked footprint), else the
    /// triangle. Otherwise the nearest node it takes within <paramref name="snap"/> metres in this block or a
    /// neighbour, and the point of it closest to <paramref name="point"/> (height from the mesh). A point found where
    /// it is keeps its own height.
    /// </summary>
    private static bool TryLocate(Func<int, int, NavBlock> blocks, Vector3 point, float snap, Func<NavBlock, int, bool> accept,
        out Located located)
    {
        located = default;
        var blockX = (int)MathF.Floor(point.X / NavBlock.Size);
        var blockY = (int)MathF.Floor(point.Y / NavBlock.Size);
        var home = blockX >= 0 && blockY >= 0 ? blocks(blockX, blockY) : null;
        if (home != null)
        {
            var under = home.Locate(point.X, point.Y);
            var onStructure = home.NearestWaypoint(point.X, point.Y, point.Z, StructureReach, StructureRise, out _);
            if (onStructure >= 0 && (accept == null || accept(home, onStructure)) &&
                (under < 0 || home.IsForbidden(under) ||
                 MathF.Abs(point.Z - home.Cz[onStructure]) < MathF.Abs(point.Z - home.HeightAt(under, point.X, point.Y))))
            {
                located = new Located(home, onStructure, point);
                return true;
            }

            if (under >= 0 && (accept == null || accept(home, under)))
            {
                located = new Located(home, under, point);
                return true;
            }
        }

        NavBlock bestBlock = null;
        var bestNode = -1;
        var bestPoint = Vector2.Zero;
        var bestDistance = float.PositiveInfinity;
        if (home != null)
        {
            var waypoint = home.NearestWaypoint(point.X, point.Y, point.Z, snap, StructureRise, out var distance);
            if (waypoint >= 0 && (accept == null || accept(home, waypoint)))
            {
                bestBlock = home;
                bestNode = waypoint;
                bestPoint = new Vector2(home.Cx[waypoint], home.Cy[waypoint]);
                bestDistance = distance;
            }
        }

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var x = blockX + dx;
                var y = blockY + dy;
                if (x < 0 || y < 0 || !Reaches(point, x, y, snap))
                    continue;
                var block = dx == 0 && dy == 0 ? home : blocks(x, y);
                if (block == null || block.TriangleCount == 0)
                    continue;
                var triangle = block.Nearest(point.X, point.Y, snap, accept == null ? null : t => accept(block, t),
                    out var nearest, out var distance);
                // Ties keep the block met first in this fixed order.
                if (triangle < 0 || distance >= bestDistance)
                    continue;
                bestBlock = block;
                bestNode = triangle;
                bestPoint = nearest;
                bestDistance = distance;
            }
        }

        if (bestNode < 0)
            return false;
        located = new Located(bestBlock, bestNode, bestBlock.IsWaypoint(bestNode)
            ? bestBlock.Centre(bestNode)
            : new Vector3(bestPoint.X, bestPoint.Y, bestBlock.HeightAt(bestNode, bestPoint.X, bestPoint.Y)));
        return true;
    }

    /// <summary>True when block (x, y) has ground within <paramref name="distance"/> flat metres of <paramref name="point"/>.</summary>
    private static bool Reaches(Vector3 point, int x, int y, float distance)
    {
        var nearestX = Math.Clamp(point.X, x * NavBlock.Size, (x + 1) * NavBlock.Size);
        var nearestY = Math.Clamp(point.Y, y * NavBlock.Size, (y + 1) * NavBlock.Size);
        return Vector2.Distance(new Vector2(point.X, point.Y), new Vector2(nearestX, nearestY)) <= distance;
    }

    private static NavPlanReport Failed(string reason, Vector3 from, Vector3 to, Stopwatch clock)
    {
        Logger.Debug("Nav plan {0} -> {1} failed: {2}", from, to, reason);
        return new NavPlanReport { Reason = reason, Start = from, Goal = to, Elapsed = clock.Elapsed };
    }
}
