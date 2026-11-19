using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Services;

/// <summary>
/// Loads <c>npc_spawners.g</c> like ArcheageZones <c>SpawnerSystem</c>: a zone's placements are queued when the zone
/// becomes active, then drained <see cref="ZoneNpcSpawnRules.SpawnsPerTick"/> per 100 ms — not player-radius
/// <see cref="NpcSpawner.Update"/>. The zone host also instantiates <c>npc_spawners.g</c> per loaded zone, not for the
/// whole world at boot: 25 406 placements / 37 702 slots spawned up front cost gigabytes and a multi-second AI tick.
/// </summary>
public sealed class ZoneNpcSpawnOrchestrator
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly WorldInstance _world;
    private readonly List<NpcSpawner> _placements = [];
    private readonly Dictionary<uint, NpcSpawner> _bySid = [];
    private readonly Dictionary<uint, List<NpcSpawner>> _byZone = [];
    private readonly HashSet<uint> _populatedZones = [];
    private readonly Queue<QueuedSpawn> _queue = new();
    private readonly Dictionary<uint, int> _pendingBySid = [];
    private readonly Lock _queueLock = new();
    private readonly Dictionary<uint, int> _stalledAttempts = [];
    private readonly Dictionary<uint, DateTime> _stallWarnUtc = [];
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

    /// <summary>Zones whose placements were already announced (each zone is populated once).</summary>
    public int PopulatedZoneCount
    {
        get
        {
            lock (_queueLock)
                return _populatedZones.Count;
        }
    }

    public bool OwnsPlacement(uint placementSid) => _bySid.ContainsKey(placementSid);

    public bool IsZonePopulated(uint zoneKey)
    {
        lock (_queueLock)
            return _populatedZones.Contains(zoneKey);
    }

    /// <summary>Register one placement from <c>ClientNpcSpawnerLoader</c> (no legacy <c>AddNpcSpawner</c>).</summary>
    public void RegisterPlacement(NpcSpawner spawner)
    {
        if (spawner.Template == null || spawner.SpawnableNpcs is not { Count: > 0 })
            return;

        spawner.ParentWorld ??= _world;
        spawner.ZonePopulationManaged = true;
        _placements.Add(spawner);
        _bySid[spawner.SpawnerId] = spawner;

        var zoneKey = spawner.Position?.ZoneId ?? 0;
        if (!_byZone.TryGetValue(zoneKey, out var zonePlacements))
        {
            zonePlacements = [];
            _byZone[zoneKey] = zonePlacements;
        }

        zonePlacements.Add(spawner);
        PlacementCount = _placements.Count;
    }

    /// <summary>Queue every registered placement (whole-world load). Runtime uses per-zone <see cref="PopulateZone"/>.</summary>
    public void PopulateAtLoad()
    {
        if (_populatedAtLoad)
            return;
        _populatedAtLoad = true;

        var queued = 0;
        foreach (var spawner in _placements)
            queued += EnqueuePlacement(spawner);
        lock (_queueLock)
        {
            foreach (var zoneKey in _byZone.Keys)
                _populatedZones.Add(zoneKey);
        }

        Logger.Info(
            "Zone npc spawn for {0}: {1} placements, {2} slots queued (whole world, ArcheageZones-style, legacy radius off)",
            _world, PlacementCount, queued);
    }

    /// <summary>
    /// Queue the placements of one zone, once — the equivalent of the zone host loading <c>npc_spawners.g</c> when a
    /// player enters the zone (<c>SpawnerSystem</c> → <c>SpawnPopulation</c> per placement).
    /// </summary>
    public bool PopulateZone(uint zoneKey)
    {
        lock (_queueLock)
        {
            if (!_populatedZones.Add(zoneKey))
                return false;
        }

        if (!_byZone.TryGetValue(zoneKey, out var placements))
        {
            Logger.Trace("Zone npc spawn: zone {0} has no npc_spawners.g placements", zoneKey);
            return false;
        }

        var queued = 0;
        foreach (var spawner in placements)
            queued += EnqueuePlacement(spawner);

        Logger.Info("Zone npc spawn: zone {0} activated — {1} placements, {2} slots queued", zoneKey, placements.Count,
            queued);
        return true;
    }

    private int EnqueuePlacement(NpcSpawner spawner)
    {
        var template = spawner.Template;
        if (template is { ActivationState: false })
        {
            Logger.Trace("Zone spawn: skip sid={0} type={1} (activation_state=false)", spawner.SpawnerId, template.Id);
            return 0;
        }

        if (!spawner.IsSpawningScheduleEnabled())
            return 0;

        var max = (int)Math.Max(1, template?.MaxPopulation ?? 1);
        for (var slot = 0; slot < max; slot++)
            QueuePush(new QueuedSpawn(spawner.SpawnerId, slot));
        return max;
    }

    /// <summary>
    /// Death respawn: <see cref="SpawnManager"/> calls this instead of legacy <see cref="NpcSpawner.Update"/>.
    /// </summary>
    /// <remarks>
    /// One call returns one slot — the one the unit vacated (zone host <c>SpawnPopulation.FreeSlot</c> → <c>Respawning</c>
    /// → <c>RespawnDue</c>). It is not a deficit recomputed from the queue: slots already announced by
    /// <see cref="PopulateZone"/> are that placement's other slots, and a death is exactly what makes room for this one.
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
        if (++_scheduleTicks >= ZoneNpcSpawnRules.ScheduleCheckIntervalTicks)
        {
            _scheduleTicks = 0;
            TickSchedules();
        }

        // The pass is bounded by the slots queued when it starts, like the zone host's announcer (`while (budget > 0 &&
        // TryDequeue(...))`): a slot put back — a respawn whose time has not come, a failed spawn — is looked at once per
        // tick. Without that bound a future respawn re-enqueued itself and the loop never ended. A slot that stops
        // making progress is additionally parked for <see cref="ZoneNpcSpawnRules.StallBackoffMs"/> instead of being
        // re-announced every tick (see <see cref="RequeueStalled"/>).
        var budget = ZoneNpcSpawnRules.SpawnsPerTick;
        var scan = QueuedCount;
        while (budget > 0 && scan-- > 0)
        {
            if (!QueuePop(out var item))
                break;

            if (item.NotBeforeUtc > DateTime.UtcNow)
            {
                QueuePush(item);
                continue;
            }

            if (!_bySid.TryGetValue(item.PlacementSid, out var spawner) || spawner.Template == null)
            {
                _stalledAttempts.Remove(item.PlacementSid);
                continue;
            }

            var max = (int)Math.Max(1, spawner.Template.MaxPopulation);
            if (GetLiveCount(spawner) >= max)
            {
                // Полное размещение: объявление просто снимается, как у зоны слот, на который нет места.
                _stalledAttempts.Remove(spawner.SpawnerId);
                continue;
            }

            if (!spawner.IsSpawningScheduleEnabled())
            {
                QueuePush(item);
                continue;
            }

            budget--;
            try
            {
                var before = GetLiveCount(spawner);
                spawner.DoSpawn();
                if (GetLiveCount(spawner) <= before)
                    RequeueStalled(spawner, item);
                else
                    _stalledAttempts.Remove(spawner.SpawnerId);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Zone spawn failed sid={0} slot={1}", item.PlacementSid, item.SlotIndex);
                RequeueStalled(spawner, item);
            }
        }
    }

    /// <summary>
    /// Возвращает слот, не давший юнита. Несколько бесплодных объявлений подряд — это норма (размещение полно,
    /// ждёт respawn), но слот, который не двигается десятки тиков, не должен объявляться 10 раз в секунду:
    /// он уходит на паузу, как respawn-таймер зоны, и один раз за интервал объясняет себя в логе.
    /// </summary>
    private void RequeueStalled(NpcSpawner spawner, QueuedSpawn item)
    {
        var sid = spawner.SpawnerId;
        var attempts = _stalledAttempts.TryGetValue(sid, out var seen) ? seen + 1 : 1;
        _stalledAttempts[sid] = attempts;

        var notBefore = item.NotBeforeUtc;
        if (attempts >= ZoneNpcSpawnRules.StallAttemptsBeforeBackoff)
        {
            var now = DateTime.UtcNow;
            var delayedUntil = now.AddMilliseconds(ZoneNpcSpawnRules.StallBackoffMs);
            if (delayedUntil > notBefore)
                notBefore = delayedUntil;

            if (!_stallWarnUtc.TryGetValue(sid, out var lastWarn) ||
                now - lastWarn >= TimeSpan.FromSeconds(ZoneNpcSpawnRules.StallWarnIntervalSeconds))
            {
                _stallWarnUtc[sid] = now;
                var max = (int)Math.Max(1, spawner.Template?.MaxPopulation ?? 1);
                Logger.Warn(
                    "Zone spawn stalled sid={0} type={1}: live={2} tracked={3} max={4}, attempts={5}; retry in {6} ms",
                    sid, spawner.Template?.Id ?? 0, GetLiveCount(spawner), TrackedCount(spawner), max, attempts,
                    ZoneNpcSpawnRules.StallBackoffMs);
            }
        }

        QueuePush(item with { NotBeforeUtc = notBefore });
    }

    private void TickSchedules()
    {
        bool zonePopulated;
        foreach (var spawner in _placements)
        {
            lock (_queueLock)
                zonePopulated = _populatedZones.Contains(spawner.Position?.ZoneId ?? 0);
            if (!zonePopulated)
                continue;

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

        for (var slot = live + pending; slot < live + pending + deficit; slot++)
            QueuePush(new QueuedSpawn(spawner.SpawnerId, slot, notBeforeUtc));
    }

    /// <summary>Queues the one slot a unit left behind (its index is for the log only — the queue is not slot-typed).</summary>
    private void EnqueueSlot(NpcSpawner spawner, DateTime notBeforeUtc)
    {
        var pending = CountPendingSlots(spawner.SpawnerId);
        QueuePush(new QueuedSpawn(spawner.SpawnerId, GetLiveCount(spawner) + pending, notBeforeUtc));
    }

    private static DateTime NotBefore(TimeSpan? delay) =>
        delay is { TotalSeconds: > 0 } d ? DateTime.UtcNow.Add(d) : default;

    private void QueuePush(QueuedSpawn item)
    {
        lock (_queueLock)
        {
            _queue.Enqueue(item);
            _pendingBySid[item.PlacementSid] = _pendingBySid.GetValueOrDefault(item.PlacementSid) + 1;
        }
    }

    private bool QueuePop(out QueuedSpawn item)
    {
        lock (_queueLock)
        {
            if (!_queue.TryDequeue(out item))
                return false;

            if (_pendingBySid.TryGetValue(item.PlacementSid, out var pending))
            {
                if (pending <= 1)
                    _pendingBySid.Remove(item.PlacementSid);
                else
                    _pendingBySid[item.PlacementSid] = pending - 1;
            }

            return true;
        }
    }

    /// <summary>
    /// Сколько объявлений размещения ждёт в очереди. Счётчик ведётся при постановке/снятии: раньше здесь
    /// перебиралась вся очередь (до 37 702 слотов) на каждое размещение, а проверка расписаний вызывает это
    /// для всех 25 406 размещений раз в секунду — квадратичная работа, из-за которой тик вставал на секунды.
    /// </summary>
    private int CountPendingSlots(uint placementSid)
    {
        lock (_queueLock)
            return _pendingBySid.GetValueOrDefault(placementSid);
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

    /// <summary>Сколько NPC размещение вообще держит в списке (живые и трупы) — только для диагностики.</summary>
    private static int TrackedCount(NpcSpawner spawner) =>
        spawner.SpawnedNpcs.TryGetValue(spawner.SpawnerId, out var list) ? list.Count : 0;

    private readonly record struct QueuedSpawn(uint PlacementSid, int SlotIndex, DateTime NotBeforeUtc = default);
}
