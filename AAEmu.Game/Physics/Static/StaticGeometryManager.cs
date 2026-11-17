#nullable enable
using AAEmu.Geometry.Abstractions;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;

using Geometry = AAEmu.Geometry;

using NLog;

namespace AAEmu.Game.Physics.Static;

/// <summary>
/// Кэш статической геометрии мира: для каждой зоны — BVH по кистям её ячеек.
/// <para>
/// Зона строится <b>в фоне</b>: первый запрос отдаёт пустой индекс (значит, высота берётся из рельефа — старое
/// поведение) и ставит сборку в очередь; когда сборка готова, индекс начинает отвечать сам. Так первый НПЦ в
/// зоне не стоит 0.5–1 с (а зона 129 из 5 670 кистей читается из пака дольше секунды).
/// </para>
/// Готовые индексы неизменяемы. Короткая блокировка защищает только кэш и порядок LRU, чтение файлов идёт вне неё.
/// </summary>
public sealed class StaticGeometryManager
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly object _sync = new();
    private readonly object _lifecycle = new();
    private readonly Dictionary<(uint TemplateId, uint ZoneKey), CacheEntry> _zones = new();
    private readonly Dictionary<uint, long> _worldRevisions = new();
    private readonly Dictionary<object, HashSet<(uint TemplateId, uint ZoneKey)>> _retainedByOwner = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(uint TemplateId, uint ZoneKey), int> _retained = new();
    private readonly LinkedList<CacheEntry> _lru = new();
    private readonly Queue<(CacheEntry Entry, Geometry.StaticCollision? Collision)> _notifications = new();
    private readonly SemaphoreSlim _buildSlots = new(MaxPendingBuilds, MaxPendingBuilds);
    private readonly IGeometryFileSource _source;
    private readonly int _maxZones;
    private int _builtZones;
    private int _pendingZones;
    private bool _dispatchingNotifications;

    /// <summary>
    /// Сколько зон читается из пака одновременно. Больше — очередь растёт и память всплескивает.
    /// </summary>
    private const int MaxPendingBuilds = 4;

    /// <summary>
    /// Raised after a zone's geometry is built, so a host can add it to its physics world (plan P5). Called on the
    /// background thread that built the zone. Lifecycle callbacks are serialized with eviction and invalidation.
    /// </summary>
    public event Action<WorldTemplate, uint, Geometry.StaticCollision>? ZoneBuilt;

    /// <summary>
    /// Raised when a zone leaves the cache (the cache is full): the host must drop what it made of that zone — the
    /// physics bodies of it, for one.
    /// </summary>
    public event Action<WorldTemplate, uint>? ZoneDropped;

    /// <summary>
    /// План S1: тела статики в физику получают только кисти, которые вообще может задеть юнит — те, чей след на
    /// плоскости мира попадает в зону ячейки, где проходит вода. Остальные тысячи кистей зоны (стены, скалы, интерьеры
    /// вдали от воды) остаются у BVH-запросов и в мир физики не идут.
    /// </summary>
    public bool PhysicsOnlyNearWater { get; set; } = true;

    /// <summary>Радиус вокруг центра ячейки (м), в котором кисти становятся телами физики.</summary>
    public float PhysicsCellRadius { get; set; } = 850f; // CELL_SIZE / 2 + запас

    /// <summary>Кисти зоны в том виде, в каком их получает физика мира (см. <see cref="PhysicsOnlyNearWater"/>).</summary>
    public IEnumerable<Geometry.StaticCollision.StaticShape> PhysicsShapes(WorldTemplate template, uint zoneKey, Geometry.StaticCollision collision)
    {
        if (!PhysicsOnlyNearWater)
            return collision.StaticShapes();

        if (!template.XmlWorldZones.TryGetValue(zoneKey, out var zone) || zone.Cells.Count == 0)
            return collision.StaticShapes(); // нет описания зоны — не рискуем, отдаём всё, как раньше

        var radius = PhysicsCellRadius;
        var halfCell = WorldManager.CELL_SIZE / 2f;
        var shapes = new List<Geometry.StaticCollision.StaticShape>();
        var selected = new HashSet<int>();
        foreach (var (cellX, cellY) in zone.Cells.Keys)
        {
            var centerX = cellX * WorldManager.CELL_SIZE + halfCell;
            var centerY = cellY * WorldManager.CELL_SIZE + halfCell;
            foreach (var (instanceId, shape) in collision.StaticShapeInstancesWithin(centerX, centerY, radius))
                if (selected.Add(instanceId))
                    shapes.Add(shape);
        }
        return shapes;
    }

    public StaticGeometryManager(IGeometryFileSource? source = null, int maxZones = 16)
    {
        _source = source ?? new ClientFileCollisionAssetSource();
        _maxZones = Math.Max(1, maxZones);
    }

    /// <summary>Сколько зон сейчас в кэше.</summary>
    public int CachedZones
    {
        get
        {
            lock (_sync)
                return _zones.Count;
        }
    }

    /// <summary>Сколько зон реально построено (счётчик сборок).</summary>
    public int BuiltZones => Volatile.Read(ref _builtZones);

    /// <summary>Сколько зон сейчас в работе.</summary>
    public int PendingZones => Volatile.Read(ref _pendingZones);

    /// <summary>Сколько клиентских ассетов прочитано (кеш моделей общий для процесса).</summary>
    public int AssetReads => Geometry.CollisionAssetCache.Shared.Reads;

    /// <summary>
    /// Геометрия зоны. Пока зона не собрана — пустой индекс (высота берётся из рельефа), сборка идёт в фоне.
    /// </summary>
    public Geometry.StaticCollision GetZone(WorldTemplate template, uint zoneKey)
    {
        ArgumentNullException.ThrowIfNull(template);
        var entry = Find(template.Id, zoneKey, out var revision) ?? Admit(template, zoneKey, revision, slotReserved: false);
        var task = entry?.Completion.Task;
        if (task is { IsCompletedSuccessfully: true })
            return task.Result;
        return Geometry.StaticCollision.Empty;
    }

    /// <summary>Starts a speculative build only when there is room; never evicts or refreshes an existing zone.</summary>
    public bool TryPrefetchZone(WorldTemplate template, uint zoneKey)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (zoneKey == 0 || template.XmlWorldZones == null || !template.XmlWorldZones.ContainsKey(zoneKey)) return true;
        long revision;
        lock (_sync)
        {
            if (_zones.ContainsKey((template.Id, zoneKey))) return true;
            _worldRevisions.TryGetValue(template.Id, out revision);
        }
        return Admit(template, zoneKey, revision, slotReserved: false, allowEviction: false) != null;
    }

    /// <summary>Keeps the union of each world's active hull zones resident until its owner releases them.</summary>
    public void SetRetainedZones(object owner, WorldTemplate template, IReadOnlyCollection<uint> zones)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(zones);
        lock (_lifecycle)
        {
            var dropped = new List<CacheEntry>();
            lock (_sync)
            {
                var next = new HashSet<(uint TemplateId, uint ZoneKey)>();
                foreach (var zone in zones)
                    if (zone != 0 && template.XmlWorldZones != null && template.XmlWorldZones.ContainsKey(zone)) next.Add((template.Id, zone));
                if (_retainedByOwner.Remove(owner, out var previous))
                    foreach (var key in previous)
                    {
                        if (next.Contains(key)) continue;
                        if (_retained[key] == 1) _retained.Remove(key);
                        else _retained[key]--;
                    }
                foreach (var key in next)
                {
                    if (previous != null && previous.Contains(key)) continue;
                    _retained.TryGetValue(key, out var count);
                    _retained[key] = count + 1;
                }
                if (next.Count != 0) _retainedByOwner.Add(owner, next);
                Trim(dropped);
            }
            foreach (var entry in dropped)
                if (entry.Collision != null) RaiseDropped(entry);
        }
    }

    private void Trim(List<CacheEntry> dropped)
    {
        var node = _lru.First;
        while (_zones.Count > (long)_maxZones + _retained.Count && node != null)
        {
            var next = node.Next;
            var entry = node.Value;
            if (!_retained.ContainsKey((entry.Template.Id, entry.ZoneKey)))
            {
                Remove(entry);
                dropped.Add(entry);
            }
            node = next;
        }
    }

    /// <summary>
    /// Дождаться готовности зоны (для прогревов и инструментов). Обычному игровому пути не нужен.
    /// </summary>
    public async Task<Geometry.StaticCollision> EnsureZoneAsync(WorldTemplate template, uint zoneKey)
    {
        ArgumentNullException.ThrowIfNull(template);
        var entry = Find(template.Id, zoneKey, out var revision);
        if (entry == null)
        {
            await _buildSlots.WaitAsync().ConfigureAwait(false);
            entry = Admit(template, zoneKey, revision, slotReserved: true);
            if (entry == null)
                return Geometry.StaticCollision.Empty;
        }
        try
        {
            return await entry.Completion.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "static geometry: zone {0} of world {1} failed", zoneKey, template.Name);
            return Geometry.StaticCollision.Empty;
        }
    }

    /// <summary>Готова ли зона (индекс собран и лежит в кэше).</summary>
    public bool IsZoneReady(WorldTemplate template, uint zoneKey)
    {
        lock (_sync)
            return _zones.TryGetValue((template.Id, zoneKey), out var entry) && entry.Completion.Task.IsCompletedSuccessfully;
    }

    /// <summary>
    /// Зоны мира, чей индекс уже собран: хост, который подключается позже, забирает их разом (иначе зона, прочитанная
    /// до его подписки, осталась бы без коллизии).
    /// </summary>
    public IEnumerable<(uint ZoneKey, Geometry.StaticCollision Collision)> BuiltZonesOf(uint templateId)
    {
        var built = new List<(uint ZoneKey, Geometry.StaticCollision Collision)>();
        lock (_sync)
        {
            foreach (var (key, entry) in _zones)
            {
                if (key.TemplateId == templateId && entry.Completion.Task.IsCompletedSuccessfully)
                    built.Add((key.ZoneKey, entry.Completion.Task.Result));
            }
        }
        return built;
    }

    private void Build(CacheEntry entry)
    {
        var template = entry.Template;
        var zoneKey = entry.ZoneKey;
        var result = Geometry.StaticCollision.Empty;
        try
        {
            lock (_sync)
            {
                if (!IsCurrent(entry))
                    return;
            }
            var cells = new List<Geometry.StaticCollisionCell>();
            if (template.XmlWorldZones.TryGetValue(zoneKey, out var zone))
            {
                foreach (var (cx, cy) in zone.Cells.Keys)
                    cells.Add(new Geometry.StaticCollisionCell(template.Name, cx, cy, template.OceanLevel));
            }
            if (cells.Count == 0)
            {
                Logger.Warn("static geometry: zone {0} of world {1} has no cells", zoneKey, template.Name);
                Complete(entry, Geometry.StaticCollision.Empty, publish: false);
                return;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            // The colliding vegetation goes into the same index: a ship sees the trees as well as the walls.
            var collision = Geometry.StaticCollision.Load(_source, cells, cache: null, includeVegetation: true);
            Interlocked.Increment(ref _builtZones);
            result = Complete(entry, collision, publish: true) ? collision : Geometry.StaticCollision.Empty;
            Logger.Info("static geometry: zone {0} of world {1} built in {2} ms: {3}", zoneKey, template.Name, watch.ElapsedMilliseconds, collision);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "static geometry: zone {0} of world {1} failed", zoneKey, template.Name);
            // A failed read is a completed empty fallback until explicit invalidation. Consumers waiting for a ready
            // zone must not wait forever, and normal queries must not retry or log the same failed source each tick.
            Complete(entry, Geometry.StaticCollision.Empty, publish: false);
        }
        finally
        {
            Interlocked.Decrement(ref _pendingZones);
            _buildSlots.Release();
            entry.Completion.TrySetResult(result);
        }
    }

    private bool Complete(CacheEntry entry, Geometry.StaticCollision collision, bool publish)
    {
        // Invalidation cannot interleave between the membership check and the callback. A removed generation never
        // publishes, and a replacement with the same key cannot receive its predecessor's completion.
        lock (_lifecycle)
        {
            lock (_sync)
            {
                if (!IsCurrent(entry))
                    return false;
                entry.Collision = collision;
            }
            if (publish)
                RaiseBuilt(entry, collision);
            lock (_sync)
                return IsCurrent(entry);
        }
    }

    private CacheEntry? Find(uint templateId, uint zoneKey, out long revision)
    {
        lock (_sync)
        {
            _worldRevisions.TryGetValue(templateId, out revision);
            if (!_zones.TryGetValue((templateId, zoneKey), out var entry))
                return null;
            Touch(entry);
            return entry;
        }
    }

    private CacheEntry? Admit(WorldTemplate template, uint zoneKey, long revision, bool slotReserved, bool allowEviction = true)
    {
        lock (_lifecycle)
        {
            CacheEntry entry;
            CacheEntry? dropped = null;
            lock (_sync)
            {
                _worldRevisions.TryGetValue(template.Id, out var currentRevision);
                if (revision != currentRevision)
                {
                    if (slotReserved)
                        _buildSlots.Release();
                    return null;
                }
                if (_zones.TryGetValue((template.Id, zoneKey), out var existing))
                {
                    if (slotReserved)
                        _buildSlots.Release();
                    if (allowEviction) Touch(existing);
                    return existing;
                }
                var retained = _retained.ContainsKey((template.Id, zoneKey));
                if (!allowEviction && !retained && _zones.Count >= _maxZones)
                {
                    if (slotReserved) _buildSlots.Release();
                    return null;
                }
                // Reserve before scheduling, including the warmup path: no check/increment race can exceed four reads.
                if (!slotReserved && !_buildSlots.Wait(0))
                    return null;
                entry = new CacheEntry(template, zoneKey);
                entry.Node = _lru.AddLast(entry);
                _zones.Add((template.Id, zoneKey), entry);
                Interlocked.Increment(ref _pendingZones);
                // Retained hull zones are additional to the ordinary LRU budget for NPC and other readers.
                if (_zones.Count > (long)_maxZones + _retained.Count)
                {
                    var node = _lru.First;
                    while (node != null && _retained.ContainsKey((node.Value.Template.Id, node.Value.ZoneKey))) node = node.Next;
                    if (node != null)
                    {
                        dropped = node.Value;
                        Remove(dropped);
                    }
                }
            }
            if (dropped?.Collision != null)
                RaiseDropped(dropped);
            if (ReferenceEquals(dropped, entry))
            {
                Interlocked.Decrement(ref _pendingZones);
                _buildSlots.Release();
                entry.Completion.TrySetResult(Geometry.StaticCollision.Empty);
                return null;
            }
            _ = Task.Run(() => Build(entry));
            return entry;
        }
    }

    /// <summary>
    /// Пол под точкой: верхняя статическая поверхность между <paramref name="above"/> и <paramref name="below"/>.
    /// </summary>
    public bool TryFloor(WorldTemplate template, uint zoneKey, float x, float y, float above, float below, out float z)
    {
        var collision = GetZone(template, zoneKey);
        return collision.TryFloor(x, y, above, below, out z);
    }

    /// <summary>
    /// Луч по статике зоны (план P6): <paramref name="floorOnly"/> отбрасывает стены, оставляя только поверхности,
    /// по которым можно стоять; иначе луч видит всё, включая вертикальные препятствия.
    /// </summary>
    public bool Raycast(WorldTemplate template, uint zoneKey, System.Numerics.Vector3 from, System.Numerics.Vector3 to, out float fraction, out System.Numerics.Vector3 normal, bool floorOnly)
    {
        var collision = GetZone(template, zoneKey);
        return collision.Raycast(from, to, out fraction, out normal, floorOnly ? Geometry.StaticCollision.FloorMinUpNormal : 0f);
    }

    /// <summary>
    /// Поверхность воды (озёра/реки) над точкой; <c>false</c>, если точки нет в объёме воды.
    /// </summary>
    public bool TryWaterSurface(WorldTemplate template, uint zoneKey, float x, float y, float z, out float surface)
    {
        var collision = GetZone(template, zoneKey);
        return collision.Water.TrySurface(x, y, z, out surface);
    }

    /// <summary>Уровень моря зоны (<c>null</c>, если море выключено).</summary>
    public float? OceanLevel(WorldTemplate template, uint zoneKey)
    {
        var collision = GetZone(template, zoneKey);
        return collision.Water.OceanLevel;
    }

    /// <summary>Сбрасывает кэш зон мира (например, при перезагрузке мира).</summary>
    public void Invalidate(uint templateId)
    {
        lock (_lifecycle)
        {
            var dropped = new List<CacheEntry>();
            lock (_sync)
            {
                _worldRevisions.TryGetValue(templateId, out var revision);
                _worldRevisions[templateId] = unchecked(revision + 1);
                var node = _lru.First;
                while (node != null)
                {
                    var next = node.Next;
                    var entry = node.Value;
                    if (entry.Template.Id == templateId)
                    {
                        Remove(entry);
                        if (entry.Collision != null)
                            dropped.Add(entry);
                    }
                    node = next;
                }
            }
            foreach (var entry in dropped)
                RaiseDropped(entry);
        }
    }

    private void Touch(CacheEntry entry)
    {
        _lru.Remove(entry.Node!);
        _lru.AddLast(entry.Node!);
    }

    private bool IsCurrent(CacheEntry entry)
        => _zones.TryGetValue((entry.Template.Id, entry.ZoneKey), out var current) && ReferenceEquals(current, entry);

    private void Remove(CacheEntry entry)
    {
        _zones.Remove((entry.Template.Id, entry.ZoneKey));
        _lru.Remove(entry.Node!);
    }

    private void RaiseBuilt(CacheEntry entry, Geometry.StaticCollision collision)
        => Notify(entry, collision);

    private void RaiseDropped(CacheEntry entry)
        => Notify(entry, null);

    private void Notify(CacheEntry entry, Geometry.StaticCollision? collision)
    {
        // The lifecycle lock serializes threads; the queue also serializes reentrant callbacks. Invalidation from a
        // ZoneBuilt subscriber must finish that notification before any ZoneDropped subscriber runs.
        _notifications.Enqueue((entry, collision));
        if (_dispatchingNotifications)
            return;
        _dispatchingNotifications = true;
        try
        {
            while (_notifications.TryDequeue(out var notification))
            {
                if (notification.Collision != null)
                    InvokeBuilt(notification.Entry, notification.Collision);
                else
                    InvokeDropped(notification.Entry);
            }
        }
        finally
        {
            _dispatchingNotifications = false;
        }
    }

    private void InvokeBuilt(CacheEntry entry, Geometry.StaticCollision collision)
    {
        var handlers = ZoneBuilt;
        if (handlers == null)
            return;
        foreach (Action<WorldTemplate, uint, Geometry.StaticCollision> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(entry.Template, entry.ZoneKey, collision);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "static geometry: ZoneBuilt subscriber failed for zone {0}", entry.ZoneKey);
            }
        }
    }

    private void InvokeDropped(CacheEntry entry)
    {
        var handlers = ZoneDropped;
        if (handlers == null)
            return;
        foreach (Action<WorldTemplate, uint> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(entry.Template, entry.ZoneKey);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "static geometry: ZoneDropped subscriber failed for zone {0}", entry.ZoneKey);
            }
        }
    }

    private sealed class CacheEntry(WorldTemplate template, uint zoneKey)
    {
        public WorldTemplate Template { get; } = template;
        public uint ZoneKey { get; } = zoneKey;
        public TaskCompletionSource<Geometry.StaticCollision> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LinkedListNode<CacheEntry>? Node { get; set; }
        public Geometry.StaticCollision? Collision { get; set; }
    }
}
