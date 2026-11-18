using AAEmu.Game.Core.Managers.Bots.Navigation;

// ReSharper disable once CheckNamespace
namespace AAEmu.Game.Models.Game;

/// <summary>Path-finding over the client navigation mesh (Configs Navigation section, DESIGN 8.3).</summary>
public class BotNavigationConfig
{
    /// <summary>The settings the planner is built with: the defaults unless the host sets them at start-up.</summary>
    public static BotNavigationConfig Current { get; set; } = new();

    /// <summary>Metres a walking bot needs on each side; narrower links are not used (DESIGN 8.3: the player radius).</summary>
    public float ActorRadiusMeters { get; set; } = 0.55f;

    /// <summary>Deepest water, surface to ground, a route may cross (<see cref="NavOptions.MaxWaterDepth"/>).</summary>
    public float MaxWaterDepthMeters { get; set; } = 5f;

    /// <summary>Most mesh nodes one search may expand before it gives up (<see cref="NavOptions.MaxSearchNodes"/>).</summary>
    public int MaxSearchNodes { get; set; } = 100_000;

    /// <summary>Longest one plan may run, block loading included (<see cref="NavOptions.Deadline"/>).</summary>
    public int DeadlineMilliseconds { get; set; } = 500;

    /// <summary>
    /// How far, in metres, an edge may lie off the line between two 256 m blocks and still be joined to the
    /// neighbour's edges there (DESIGN 8.3: 0.1 m).
    /// </summary>
    public float SeamToleranceMeters { get; set; } = 0.1f;

    /// <summary>A start that is off the mesh is moved to the nearest mesh within this many metres, else the plan fails.</summary>
    public float StartSnapMeters { get; set; } = 8f;

    /// <summary>
    /// A goal off the walkable mesh (outside it, inside a blocked area such as a building's footprint, or in deep
    /// water) is moved to the nearest walkable point within this many metres, else the plan fails. The last waypoint
    /// is then that point, not the goal.
    /// </summary>
    public float GoalSnapMeters { get; set; } = 8f;

    /// <summary>
    /// Also walk the waypoint grids the client keeps on structures (town gates, bridges, stairs: node type 4), joined
    /// to the triangle mesh at their entrances.
    /// </summary>
    public bool UseStructureGrids { get; set; } = true;

    /// <summary>Memory the cache of loaded mesh blocks may use, in megabytes; the least recently used go first.</summary>
    public int CacheMegabytes { get; set; } = 192;

    /// <summary>Background threads that plan paths; plans never run on the bot tick thread.</summary>
    public int PlannerThreads { get; set; } = 2;

    /// <summary>Plans that may wait for a planner thread; a request over this fails at once with a reason.</summary>
    public int QueueCapacity { get; set; } = 512;

    /// <summary>
    /// Folder holding the client's world folders (<c>...\game\worlds</c>) to read mesh blocks from directly. Empty
    /// (the default) reads them through the client sources the server already uses (<c>ClientData.Sources</c>).
    /// </summary>
    public string WorldsFolder { get; set; } = "";

    private static readonly BotNavigationConfig Defaults = new();

    /// <summary>The options a bot on foot plans with, from these settings.</summary>
    public NavOptions Options() => NavOptions.Default with
    {
        ActorRadius = ActorRadiusMeters is >= 0f and <= 10f ? ActorRadiusMeters : Defaults.ActorRadiusMeters,
        MaxWaterDepth = MaxWaterDepthMeters >= 0f ? MaxWaterDepthMeters : Defaults.MaxWaterDepthMeters,
        MaxSearchNodes = MaxSearchNodes > 0 ? MaxSearchNodes : Defaults.MaxSearchNodes,
        Deadline = TimeSpan.FromMilliseconds(DeadlineMilliseconds > 0 ? DeadlineMilliseconds : Defaults.DeadlineMilliseconds),
    };

    /// <summary>The planner's own settings, from these settings.</summary>
    public NavPlannerSettings PlannerSettings() => new(
        SeamToleranceMeters is > 0f and <= 1f ? SeamToleranceMeters : Defaults.SeamToleranceMeters,
        StartSnapMeters >= 0f ? StartSnapMeters : Defaults.StartSnapMeters,
        GoalSnapMeters >= 0f ? GoalSnapMeters : Defaults.GoalSnapMeters);

    public long CacheBytes => (CacheMegabytes > 0 ? CacheMegabytes : Defaults.CacheMegabytes) * 1024L * 1024L;

    public int EffectivePlannerThreads => PlannerThreads is > 0 and <= 16 ? PlannerThreads : Defaults.PlannerThreads;

    public int EffectiveQueueCapacity => QueueCapacity > 0 ? QueueCapacity : Defaults.QueueCapacity;
}