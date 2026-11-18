using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Core.Managers.Bots.Navigation;

public enum NavPlanStatus
{
    Queued,
    Planning,
    Done,
    Failed,
    Cancelled,
}

/// <summary>
/// One requested plan. The mover polls <see cref="IsDone"/> on its own step (or awaits <see cref="Completion"/>) and
/// then reads <see cref="Path"/> or <see cref="Reason"/>; nothing here blocks the caller.
/// </summary>
public sealed class NavPlanTicket
{
    private readonly TaskCompletionSource<NavPlanTicket> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _status = (int)NavPlanStatus.Queued;

    internal NavPlanTicket(uint botId, WorldTemplate world, Vector3 from, Vector3 to, NavOptions options)
    {
        BotId = botId;
        World = world;
        From = from;
        To = to;
        Options = options ?? NavOptions.Default;
    }

    public uint BotId { get; }
    public WorldTemplate World { get; }
    public Vector3 From { get; }
    public Vector3 To { get; }
    public NavOptions Options { get; }

    public NavPlanStatus Status => (NavPlanStatus)Volatile.Read(ref _status);

    public bool IsDone => Status is NavPlanStatus.Done or NavPlanStatus.Failed or NavPlanStatus.Cancelled;

    /// <summary>The route once <see cref="Status"/> is <see cref="NavPlanStatus.Done"/>; null otherwise.</summary>
    public NavPath Path { get; private set; }

    /// <summary>Why there is no route, once done without one.</summary>
    public string Reason { get; private set; }

    /// <summary>How long the plan itself ran (not the wait in the queue).</summary>
    public TimeSpan PlanTime { get; private set; }

    public Task<NavPlanTicket> Completion => _completion.Task;

    /// <summary>
    /// Drops the request: a queued one never runs, a running one finishes but is reported cancelled. Does nothing
    /// once done.
    /// </summary>
    public void Cancel()
    {
        if (Interlocked.CompareExchange(ref _status, (int)NavPlanStatus.Cancelled, (int)NavPlanStatus.Queued) == (int)NavPlanStatus.Queued ||
            Interlocked.CompareExchange(ref _status, (int)NavPlanStatus.Cancelled, (int)NavPlanStatus.Planning) == (int)NavPlanStatus.Planning)
        {
            Reason = NavReasons.Cancelled;
            _completion.TrySetResult(this);
        }
    }

    internal bool TryStart() =>
        Interlocked.CompareExchange(ref _status, (int)NavPlanStatus.Planning, (int)NavPlanStatus.Queued) == (int)NavPlanStatus.Queued;

    internal void Finish(NavPath path, string reason, TimeSpan planTime)
    {
        if (Status != NavPlanStatus.Planning)
            return;
        // The results are written before the status flips, so a poller that sees Done also sees the path.
        PlanTime = planTime;
        Path = path;
        Reason = path != null ? null : reason;
        var outcome = path != null ? NavPlanStatus.Done : NavPlanStatus.Failed;
        if (Interlocked.CompareExchange(ref _status, (int)outcome, (int)NavPlanStatus.Planning) == (int)NavPlanStatus.Planning)
            _completion.TrySetResult(this);
    }

    internal void Refuse(string reason)
    {
        Reason = reason;
        Volatile.Write(ref _status, (int)NavPlanStatus.Failed);
        _completion.TrySetResult(this);
    }
}

/// <summary>
/// Runs path plans on their own background threads (<c>Navigation.PlannerThreads</c>), so the bot tick thread only
/// asks and later polls (WORKPLAN rule 6, DESIGN 8.3). Requests over <c>Navigation.QueueCapacity</c> fail at once
/// with <see cref="NavReasons.QueueFull"/>. Each plan runs in the requesting bot's journal scope; one stopped by the
/// deadline is planned once more, with the blocks the first try loaded now held.
/// </summary>
public sealed class NavPlanQueue : IDisposable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly INavPlanner _planner;
    private readonly BlockingCollection<NavPlanTicket> _queue;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Thread> _threads = [];

    /// <param name="planner">The planner the threads run.</param>
    /// <param name="threads">Planner threads; 0 starts none, and <see cref="RunPending"/> plans on the caller (tests).</param>
    /// <param name="capacity">Most requests waiting at once.</param>
    public NavPlanQueue(INavPlanner planner, int threads, int capacity)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _queue = new BlockingCollection<NavPlanTicket>(new ConcurrentQueue<NavPlanTicket>(), Math.Max(1, capacity));
        for (var i = 0; i < threads; i++)
        {
            var thread = new Thread(Work)
            {
                IsBackground = true,
                Name = $"BotNavPlanner{i + 1}",
                Priority = ThreadPriority.BelowNormal,
            };
            _threads.Add(thread);
            thread.Start();
        }
    }

    private static readonly Lazy<NavPlanQueue> Live = new(() =>
    {
        var config = AAEmu.Game.Models.Game.BotNavigationConfig.Current;
        return new NavPlanQueue(NavPlanner.Instance, config.EffectivePlannerThreads, config.EffectiveQueueCapacity);
    });

    /// <summary>The live server's queue over <see cref="NavPlanner.Instance"/>; its threads start on first use.</summary>
    public static NavPlanQueue Instance => Live.Value;

    /// <summary>Requests waiting for a planner thread.</summary>
    public int Waiting => _queue.Count;

    /// <summary>
    /// Queues a plan for <paramref name="botId"/> from <paramref name="from"/> to <paramref name="to"/>; returns at once.
    /// <paramref name="options"/> null is <see cref="NavOptions.Default"/>.
    /// </summary>
    public NavPlanTicket Request(uint botId, WorldTemplate world, Vector3 from, Vector3 to, NavOptions options = null)
    {
        var ticket = new NavPlanTicket(botId, world, from, to, options);
        bool queued;
        try
        {
            queued = !_stop.IsCancellationRequested && _queue.TryAdd(ticket);
        }
        catch (InvalidOperationException)
        {
            queued = false;
        }

        if (!queued)
            ticket.Refuse(NavReasons.QueueFull);
        return ticket;
    }

    /// <summary>Plans every waiting request on the calling thread; how many ran. For a queue made with no threads.</summary>
    public int RunPending()
    {
        var ran = 0;
        while (_queue.TryTake(out var ticket))
        {
            Run(ticket);
            ran++;
        }

        return ran;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _queue.CompleteAdding();
        foreach (var thread in _threads)
            thread.Join(TimeSpan.FromSeconds(2));
        while (_queue.TryTake(out var ticket))
            ticket.Cancel();
        _stop.Dispose();
        _queue.Dispose();
    }

    private void Work()
    {
        try
        {
            foreach (var ticket in _queue.GetConsumingEnumerable(_stop.Token))
                Run(ticket);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (ObjectDisposedException)
        {
            // Shutting down.
        }
    }

    private void Run(NavPlanTicket ticket)
    {
        if (!ticket.TryStart())
            return;
        var started = Stopwatch.GetTimestamp();
        // (the Live build opens a per-bot log scope here)
        {
            NavPath path = null;
            string reason;
            try
            {
                // A first trip into an area loads its blocks inside the deadline; they are held afterwards, so a plan
                // the deadline stopped is tried once more.
                if (!_planner.TryPlan(ticket.World, ticket.From, ticket.To, ticket.Options, out path, out reason) &&
                    reason != null && reason.StartsWith(NavReasons.DeadlinePrefix, StringComparison.Ordinal) &&
                    ticket.Status == NavPlanStatus.Planning)
                    _planner.TryPlan(ticket.World, ticket.From, ticket.To, ticket.Options, out path, out reason);
            }
            catch (Exception e)
            {
                reason = NavReasons.Crashed(e);
                Logger.Error(e, "Path plan for bot {0} from {1} to {2} failed", ticket.BotId, ticket.From, ticket.To);
            }

            ticket.Finish(path, reason, Stopwatch.GetElapsedTime(started));
        }
    }
}
