namespace AAEmu.Game.Core.Managers.Bots.Navigation;

/// <summary>Why a plan failed, as <see cref="INavPlanner.TryPlan"/> reports it. Callers may compare with these.</summary>
public static class NavReasons
{
    public const string TickThread = "planning was asked for on the bot tick thread";
    public const string NoWorld = "no world to plan in";
    public const string NoStartMesh = "no navigation mesh under the start";
    public const string NoGoalMesh = "no navigation mesh under the goal";
    public const string Unreachable = "unreachable: no walkable route joins the start and the goal";
    public const string QueueFull = "the path planner is busy (queue full)";
    public const string Cancelled = "cancelled";

    /// <summary>The goal is on the mesh but only on ground the walker may not use (blocked area or deep water), with none usable near.</summary>
    public static string GoalNotWalkable(float snapMeters) =>
        $"the goal is in a blocked area or deep water, with no walkable ground within {snapMeters:0.#} m";

    public static string NodeLimit(int maxNodes) => $"search limit reached ({maxNodes} nodes)";

    /// <summary>How every <see cref="Deadline"/> reason starts.</summary>
    public const string DeadlinePrefix = "deadline reached";

    public static string Deadline(TimeSpan deadline) => $"{DeadlinePrefix} ({deadline.TotalMilliseconds:0} ms)";

    public static string Crashed(Exception e) => $"the planner failed: {e.GetType().Name}: {e.Message}";
}
