using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Services;

/// <summary>
/// Loads <c>npc_spawners.g</c> like ArcheageZones <c>SpawnerSystem</c>: queue at zone/world load, drain
/// <see cref="ZoneNpcSpawnRules.SpawnsPerTick"/> per 100 ms — not player-radius <see cref="NpcSpawner.Update"/>.
/// </summary>
public sealed class ZoneNpcSpawnOrchestrator
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly WorldInstance _world;
    private readonly List<NpcSpawner> _placements = [];
    private readonly HashSet<uint> _placementSids = [];
    private readonly Queue<QueuedSpawn> _queue = new();
    private readonly Lock _queueLock = new();
    private bool _populatedAtLoad;
    private int _scheduleTicks;

    public ZoneNpcSpawnOrchestrator(WorldInstance world) => _world = world;

    public int QueuedCount
    {
        get
        {
            lock (_queueLock)
                return _queue.Count;
        }
    }

    public int PlacementCount { get; private set; }

    public bool OwnsPlacement(uint placementSid) => _placementSids.Contains(placementSid);

    /// <summary>Register one placement from <c>ClientNpcSpawnerLoader</c> (no legacy <c>AddNpcSpawner</c>).</summary>
    public void RegisterPlacement(NpcSpawner spawner)
    {
        if (spawner.Template == null || spawner.SpawnableNpcs is not { Count: > 0 })
            return;

        spawner.ParentWorld ??= _world;
        spawner.ZonePopulationManaged = true;
        _placements.Add(spawner);
        _placementSids.Add(spawner.SpawnerId);
        PlacementCount = _placements.Count;
    }

    /// <summary>After all placements registered: queue every slot (native post-ZoneLoaded pass).</summary>
    public void PopulateAtLoad()
    {
        if (_populatedAtLoad)
            return;
        _populatedAtLoad = true;

        var queued = 0;
        lock (_queueLock)
        {
            foreach (var spawner in _placements)
            {
                var template = spawner.Template;
                if (template is { ActivationState: false })
                {
                    Logger.Trace("Zone spawn: skip sid={0} type={1} (activation_state=false)", spawner.SpawnerId, template.Id);
                    continue;
                }

                if (!spawner.IsSpawningScheduleEnabled())
                    continue;

                var max = (int)Math.Max(1, template?.MaxPopulation ?? 1);
                for (var slot = 0; slot < max; slot++)
                {
                    _queue.Enqueue(new QueuedSpawn(spawner.SpawnerId, slot));
                    queued++;
                }
            }
        }

        Logger.Info(
            "Zone npc spawn for {0}: {1} placements, {2} slots queued (ArcheageZones-style, legacy radius off)",
            _world, PlacementCount, queued);
    }

    /// <summary>
    /// Death respawn: <see cref="SpawnManager"/> calls this instead of legacy <see cref="NpcSpawner.Update"/>.
    /// </summary>
    /// <remarks>
    /// One call returns one slot — the one the unit vacated (zone host <c>SpawnPopulation.FreeSlot</c> → <c>Respawning</c>
    /// → <c>RespawnDue</c>). It is not a deficit recomputed from the queue: slots already announced by
    /// <see cref="PopulateAtLoad"/> are that placement's other slots, and a death is exactly what makes room for this one.
    /// </remarks>
    public void EnqueueRespawn(NpcSpawner spawner, TimeSpan? delay = null)
    {
        if (spawner.Template == null)
            return;

        var max = (int)Math.Max(1, spawner.Template.MaxPopulation);
        if (GetLiveCount(spawner) >= max)
            return;

        EnqueueSlot(spawner, NotBefore(delay));
    }

    /// <summary>
    /// ArcheageZones <c>MinPopulationRespawns</c>: a death that leaves the placement under <c>min_population</c>
    /// re-announces the freed slot at once instead of after <c>spawn_delay</c> — the zone host sets
    /// <c>slot.RespawnAtMs = nowMs</c> and counts only slots holding units, not announcements still waiting.
    /// </summary>
    public void EnqueueMinPopulationRespawn(NpcSpawner spawner)
    {
        if (!ZoneNpcSpawnRules.MinPopulationEnabled || spawner.Template is not { } template)
            return;

        var min = (int)Math.Max(1, template.MinPopulation);
        if (GetLiveCount(spawner) >= min)
            return;

        EnqueueSlot(spawner, notBeforeUtc: default);
    }

    public void Tick()
    {
        if (!_populatedAtLoad)
            PopulateAtLoad();

        if (++_scheduleTicks >= ZoneNpcSpawnRules.ScheduleCheckIntervalTicks)
        {
            _scheduleTicks = 0;
            TickSchedules();
        }

        // The pass is bounded by the slots queued when it starts, like the zone host's announcer (`while (budget > 0 &&
        // TryDequeue(...))`): a slot put back — a respawn whose time has not come, a failed spawn — is looked at once per
        // tick. Without that bound a future respawn re-enqueued itself and the loop never ended.
        var budget = ZoneNpcSpawnRules.SpawnsPerTick;
        var scan = QueuedCount;
        while (budget > 0 && scan-- > 0)
        {
            QueuedSpawn item;
            lock (_queueLock)
            {
                if (!_queue.TryDequeue(out item))
                    break;
            }

            if (item.NotBeforeUtc > DateTime.UtcNow)
            {
                lock (_queueLock)
                    _queue.Enqueue(item);
                continue;
            }

            var spawner = _placements.Find(s => s.SpawnerId == item.PlacementSid);
            if (spawner?.Template == null)
                continue;

            var max = (int)Math.Max(1, spawner.Template.MaxPopulation);
            if (GetLiveCount(spawner) >= max)
                continue;

            if (!spawner.IsSpawningScheduleEnabled())
            {
                lock (_queueLock)
                    _queue.Enqueue(item);
                continue;
            }

            budget--;
            try
            {
                var before = GetLiveCount(spawner);
                spawner.DoSpawn();
                if (GetLiveCount(spawner) <= before)
                {
                    lock (_queueLock)
                        _queue.Enqueue(item);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Zone spawn failed sid={0} slot={1}", item.PlacementSid, item.SlotIndex);
                lock (_queueLock)
                    _queue.Enqueue(item);
            }
        }
    }

    private void TickSchedules()
    {
        foreach (var spawner in _placements)
        {
            spawner.TickZonePopulationSchedule();

            var template = spawner.Template;
            if (template == null || template.ActivationState == false)
                continue;

            if (!spawner.IsSpawningScheduleEnabled())
                continue;

            EnqueueDeficitFor(spawner);
        }
    }

    private void EnqueueDeficitFor(NpcSpawner spawner, DateTime notBeforeUtc = default)
    {
        var max = (int)Math.Max(1, spawner.Template!.MaxPopulation);
        var live = GetLiveCount(spawner);
        var pending = CountPendingSlots(spawner.SpawnerId);
        var deficit = max - live - pending;
        if (deficit <= 0)
            return;

        lock (_queueLock)
        {
            for (var slot = live + pending; slot < live + pending + deficit; slot++)
                _queue.Enqueue(new QueuedSpawn(spawner.SpawnerId, slot, notBeforeUtc));
        }
    }

    /// <summary>Queues the one slot a unit left behind (its index is for the log only — the queue is not slot-typed).</summary>
    private void EnqueueSlot(NpcSpawner spawner, DateTime notBeforeUtc)
    {
        var pending = CountPendingSlots(spawner.SpawnerId);
        lock (_queueLock)
            _queue.Enqueue(new QueuedSpawn(spawner.SpawnerId, GetLiveCount(spawner) + pending, notBeforeUtc));
    }

    private static DateTime NotBefore(TimeSpan? delay) =>
        delay is { TotalSeconds: > 0 } d ? DateTime.UtcNow.Add(d) : default;

    private int CountPendingSlots(uint placementSid)
    {
        lock (_queueLock)
        {
            var count = 0;
            foreach (var item in _queue)
            {
                if (item.PlacementSid == placementSid)
                    count++;
            }

            return count;
        }
    }

    private static int GetLiveCount(NpcSpawner spawner)
    {
        if (!spawner.SpawnedNpcs.TryGetValue(spawner.SpawnerId, out var list) || list.Count == 0)
            return 0;

        var live = 0;
        foreach (var npc in list)
        {
            if (npc is { IsDead: false })
                live++;
        }

        return live;
    }

    private readonly record struct QueuedSpawn(uint PlacementSid, int SlotIndex, DateTime NotBeforeUtc = default);
}