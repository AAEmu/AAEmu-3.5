using System.Numerics;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers.Bots.Navigation;

/// <summary>
/// Finds a walkable path over the client navigation mesh (DESIGN 8.3): A* over the triangle nodes of each 256 m
/// <c>.bai</c> block, stitched across block seams and smoothed. Walking travel asks it for a route and the mover
/// follows the waypoints; the planner itself never moves anything.
/// </summary>
public interface INavPlanner
{
    /// <summary>
    /// Plans a path from <paramref name="from"/> to <paramref name="to"/> in <paramref name="world"/>.
    /// False when there is none within the limits of <paramref name="options"/>; <paramref name="path"/> is then null
    /// and <paramref name="reason"/> says why (no mesh under the start or goal, unreachable, node or time limit hit).
    /// <paramref name="reason"/> is null on success. Can take up to <see cref="NavOptions.Deadline"/> and reads mesh
    /// blocks, so it is never called on the bot tick thread: callers plan on a background task. Safe to call from
    /// several threads at once.
    /// </summary>
    bool TryPlan(WorldTemplate world, Vector3 from, Vector3 to, NavOptions options,
                 out NavPath path, out string reason);
}

/// <summary>
/// A planned route. <see cref="Waypoints"/> are the points to walk through in order: the first is the start and the
/// last the goal, each moved onto the mesh if it was off it. <see cref="ZoneKeysCrossed"/> is every zone key the
/// route passes through, checked per 64 m region (DESIGN 8.5), in the order first entered, the start's key first.
/// <see cref="Length"/> is the distance along the waypoints, in metres. <see cref="OnStructure"/> says, waypoint by
/// waypoint, which stand on a structure's waypoint grid (a bridge deck, stairs, a town gate); a leg between two of them
/// runs on the structure, where the heightmap under it is not the ground walked on. Null when none does.
/// </summary>
public sealed record NavPath(IReadOnlyList<Vector3> Waypoints, IReadOnlyList<uint> ZoneKeysCrossed, float Length,
    IReadOnlyList<bool> OnStructure = null);

/// <summary>
/// Who is walking and how hard to look. <see cref="Default"/> fits a bot on foot; change a value with
/// <c>NavOptions.Default with { ... }</c>.
/// </summary>
public sealed record NavOptions
{
    /// <summary>A bot on foot, with the limits a 1 km plan fits in.</summary>
    public static NavOptions Default { get; } = new();

    /// <summary>
    /// Metres the walker needs on each side; a link narrower than this (<c>MaxPassRadius</c>) is not used, and a
    /// blocked link (<c>MaxPassRadius</c> below 0) never is. 0.55 is the player radius (DESIGN 8.3).
    /// </summary>
    public float ActorRadius { get; init; } = 0.55f;

    /// <summary>
    /// Deepest water, in metres from the surface to the ground under it, the route may cross; the bot swims through
    /// anything shallower (DESIGN 8.3). <see cref="float.PositiveInfinity"/> is no limit.
    /// </summary>
    public float MaxWaterDepth { get; init; } = 5f;

    /// <summary>
    /// Most mesh nodes one search may expand before it gives up as unreachable. This is the limit that normally stops
    /// a hopeless search, and it stops it at the same point every time.
    /// </summary>
    public int MaxSearchNodes { get; init; } = 100_000;

    /// <summary>
    /// Longest one plan may run, from the call. A guard for a slow or busy machine above <see cref="MaxSearchNodes"/>;
    /// a plan it stops fails with a reason that says so.
    /// </summary>
    public TimeSpan Deadline { get; init; } = TimeSpan.FromMilliseconds(500);
}
