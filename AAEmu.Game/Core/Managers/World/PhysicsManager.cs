using System.Collections.Concurrent;
using System.Numerics;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Physics;
using AAEmu.Game.Physics.Hulls;
using AAEmu.Game.Physics.Static;
using AAEmu.Geometry;
using NLog;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>In-process ArcheageZones simulation. Domain commands enter a single simulation thread; only 3.5 client packets leave it.</summary>
public sealed class PhysicsManager : IDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly ConcurrentQueue<Action<long>> _commands = new();
    private readonly Dictionary<uint, Ship> _ships = [];
    private readonly HashSet<uint> _aboard = [];
    private readonly List<Transform> _attachmentChildren = [];
    private readonly HashSet<uint> _suspended = [];
    private readonly HashSet<uint> _waitingForGeometry = [];
    private readonly Dictionary<uint, Task<ShipHullGeometry>> _pendingHullGeometry = [];
    private readonly List<uint> _active = [];
    private readonly List<Slave> _snapshot = [];
    private readonly HullCollisionReporter _collisionReporter = new();
    private readonly HashSet<uint> _prefetchedZones = [];
    private readonly HashSet<uint> _checkedZones = [];
    private readonly List<uint> _prefetchOrder = [];
    private readonly List<Vector2> _prefetchPositions = [];
    private readonly object _tickLock = new();
    private readonly object _lifetimeLock = new();
    private readonly Func<uint, float> _mass;
    private readonly Func<uint, bool> _prefetchZone;
    private readonly Func<uint, bool> _zoneReady;
    private readonly Action<uint, HullCollisionWindow, float> _reportCollision;
    private readonly ShipHullGeometryResolver _hullGeometry;
    private readonly bool _useAuthoredDrive;
    private IHullPhysics _hulls;
    private Func<float, float, float?> _terrain;
    private Func<float, float, StaticCollision> _geometryAt;
    private StaticGeometryManager _geometry;
    private Thread _thread;
    private volatile bool _running;
    private bool _disposed;
    private long _previousTick = -1;
    private long _tickVersion;
    private long _reportedAt = -1;
    private long _lastPrefetch = -1;
    private int _prefetchCursor;

    public PhysicsManager(IHullPhysics hulls = null, ShipHullGeometryResolver hullGeometry = null)
    {
        _hulls = hulls;
        _hullGeometry = hullGeometry ?? ShipHullGeometryResolver.Instance;
        _useAuthoredDrive = AppConfiguration.Instance?.World?.AuthoredShipDriveEnabled ?? false;
        _mass = id => _ships.TryGetValue(id, out var ship) ? ship.ReportMass : 1f;
        _prefetchZone = CollectPrefetchZone;
        _zoneReady = IsZoneReady;
        _reportCollision = ReportCollision;
    }

    public WorldInstance SimulationWorld { get; init; }
    public int ShipCount { get { lock (_tickLock) return _ships.Count; } }

    public void Initialize()
    {
        _terrain ??= (x, y) => SimulationWorld.Template.TryGetHeight(x, y, out var height) ? height : null;
        _geometryAt ??= GeometryAt;
        _hulls ??= new FittedHullPhysics(ZoneCollision, doodads: _ => ShipDoodadCollision.Instance.Current(SimulationWorld.Id),
            isDead: id => _ships.TryGetValue(id, out var ship) && ship.Unit.Hp <= 0, useAuthoredDrive: _useAuthoredDrive,
            isWaterAt: point => SimulationWorld.Water.IsWater(point, out _));
    }

    public void AttachStaticGeometry(StaticGeometryManager geometry)
    {
        lock (_tickLock)
        {
            var previous = Interlocked.Exchange(ref _geometry, geometry);
            if (previous != null && !ReferenceEquals(previous, geometry))
                previous.SetRetainedZones(this, SimulationWorld.Template, []);
        }
    }

    private ZoneCollision ZoneCollision(ushort zoneKey)
    {
        var template = SimulationWorld.Template;
        var collision = Volatile.Read(ref _geometry)?.GetZone(template, zoneKey) ?? StaticCollision.Empty;
        return new ZoneCollision(_terrain, collision, template.OceanLevel) { StaticAt = _geometryAt };
    }

    private StaticCollision GeometryAt(float x, float y)
    {
        var template = SimulationWorld.Template;
        var geometry = Volatile.Read(ref _geometry);
        if (geometry == null || template.ZoneKeyByRegions == null || x < 0f || y < 0f) return StaticCollision.Empty;
        var sx = (int)(x / WorldManager.REGION_SIZE);
        var sy = (int)(y / WorldManager.REGION_SIZE);
        if (sx >= template.ZoneKeyByRegions.GetLength(0) || sy >= template.ZoneKeyByRegions.GetLength(1)) return StaticCollision.Empty;
        var zone = template.ZoneKeyByRegions[sx, sy];
        return zone == 0 ? StaticCollision.Empty : geometry.GetZone(template, zone);
    }

    private bool CollectPrefetchZone(uint zoneKey) { _prefetchOrder.Add(zoneKey); return true; }

    private bool IsZoneReady(uint zoneKey)
    {
        var geometry = Volatile.Read(ref _geometry);
        var template = SimulationWorld.Template;
        return geometry == null || template.XmlWorldZones == null || !template.XmlWorldZones.ContainsKey(zoneKey)
            || geometry.IsZoneReady(template, zoneKey);
    }

    private bool CollisionReady(Vector2 position)
    {
        var zones = SimulationWorld.Template.ZoneKeyByRegions;
        if (zones == null || Volatile.Read(ref _geometry) == null) return true;
        _checkedZones.Clear();
        return HullZonePrefetch.Visit(zones, position, _checkedZones, _zoneReady);
    }

    private void PrefetchNearby(long nowMs)
    {
        var zones = SimulationWorld.Template.ZoneKeyByRegions;
        if (_prefetchPositions.Count == 0 && _prefetchedZones.Count == 0) return;
        if (_lastPrefetch >= 0 && nowMs - _lastPrefetch < 1000 && _prefetchPositions.Count != 0) return;
        _lastPrefetch = nowMs;
        _prefetchedZones.Clear();
        _prefetchOrder.Clear();
        if (zones != null && _prefetchPositions.Count != 0)
        {
            var start = _prefetchCursor % _prefetchPositions.Count;
            _prefetchCursor = (start + 1) % _prefetchPositions.Count;
            for (var i = 0; i < _prefetchPositions.Count; i++)
                HullZonePrefetch.Visit(zones, _prefetchPositions[(start + i) % _prefetchPositions.Count], _prefetchedZones, _prefetchZone);
        }
        var geometry = Volatile.Read(ref _geometry);
        if (geometry == null) return;
        geometry.SetRetainedZones(this, SimulationWorld.Template, _prefetchedZones);
        foreach (var zone in _prefetchOrder)
            if (!geometry.TryPrefetchZone(SimulationWorld.Template, zone)) break;
    }

    public void AddShip(Slave slave) => AddShip(slave, ModelManager.Instance.GetShipModel(slave.ModelId));

    public void AddShip(Slave slave, ShipModelV1 model)
    {
        ArgumentNullException.ThrowIfNull(slave);
        if (model == null || !float.IsFinite(model.Mass) || model.Mass <= 0f)
        {
            Logger.Warn("Invalid hull model for ship {0}", slave.ObjId);
            return;
        }
        var seed = new ShipMoveType();
        seed.UseSlaveBase(slave);
        Enqueue(now =>
        {
            if (_ships.ContainsKey(slave.ObjId))
            {
                _hulls.Remove(slave.ObjId);
                _collisionReporter.Remove(slave.ObjId);
            }
            slave.ShipModel = model;
            _ships[slave.ObjId] = new Ship(slave, model.Mass);
            _hulls.Add(slave.ObjId, (ushort)slave.Transform.ZoneId, model, model.Mass, slave.Template?.ObbSize ?? Vector3.Zero);
            _hulls.SetWorld(slave.ObjId, (int)SimulationWorld.Id);
            _pendingHullGeometry.Remove(slave.ObjId);
            if (!string.IsNullOrWhiteSpace(model.Normal) ||
                (model.TubeRadius != 0f && model.TubeLength != 0f && model.TubeOffsetZ != 0f))
                _pendingHullGeometry[slave.ObjId] = _hullGeometry.ResolveAsync(model.Normal ?? string.Empty, model.TubeRadius, model.TubeLength, model.TubeOffsetZ);
            var surface = SimulationWorld.Water.GetWaterSurface(slave.Transform.World.Position, out _);
            _hulls.Seed(slave.ObjId, seed, surface > 0f ? surface : SimulationWorld.Template.OceanLevel, now);
            _hulls.Activate(slave.ObjId, now);
            _waitingForGeometry.Add(slave.ObjId);
            _hulls.SetHeld(slave.ObjId, true, now);
            slave.PhysicsState = new ShipPhysicsState(_hulls.Pose(slave.ObjId));
        });
    }

    public void RemoveShip(Slave slave)
    {
        if (slave == null) return;
        Enqueue(_ =>
        {
            if (!_ships.TryGetValue(slave.ObjId, out var current) || !ReferenceEquals(current.Unit, slave)) return;
            _hulls.Remove(slave.ObjId);
            _ships.Remove(slave.ObjId);
            _suspended.Remove(slave.ObjId);
            _waitingForGeometry.Remove(slave.ObjId);
            _pendingHullGeometry.Remove(slave.ObjId);
            _collisionReporter.Remove(slave.ObjId);
            slave.PhysicsState = null;
        });
    }

    public void SetHelm(Slave slave, sbyte throttle, sbyte steering)
    {
        var now = Environment.TickCount64;
        Enqueue(_ =>
        {
            if (_ships.TryGetValue(slave.ObjId, out var current) && ReferenceEquals(current.Unit, slave))
                _hulls.SetHelm(slave.ObjId, Math.Clamp(throttle / 127f, -1f, 1f), Math.Clamp(steering / 127f, -1f, 1f), now);
        });
    }

    public void ApplyImpulse(Slave slave, HullImpulse impulse, Vector3? source = null) => Enqueue(now =>
    {
        if (!_ships.TryGetValue(slave.ObjId, out var ship) || !ReferenceEquals(ship.Unit, slave)) return;
        UpdateShipMass(ship, now);
        _hulls.ApplyImpulse(slave.ObjId, impulse, source);
    });

    public void Explode(HullExplosion blast) => Enqueue(now =>
    {
        foreach (var ship in _ships.Values)
            UpdateShipMass(ship, now);
        _hulls.Explode(blast);
    });

    private void Enqueue(Action<long> command)
    {
        lock (_lifetimeLock)
        {
            if (!_disposed) _commands.Enqueue(command);
        }
    }

    /// <summary>Deterministic simulation boundary, also used by tests without starting a worker.</summary>
    public void Tick(long nowMs)
    {
        lock (_tickLock)
        {
            if (_disposed) return;
            _tickVersion++;
            while (_commands.TryDequeue(out var command)) command(nowMs);
            _active.Clear();
            _snapshot.Clear();
            _prefetchPositions.Clear();
            var dt = _previousTick < 0 ? 0f : Math.Clamp((nowMs - _previousTick) / 1000f, 0f, 0.25f);
            _previousTick = nowMs;
            foreach (var entry in _ships.Values)
            {
                var ship = entry.Unit;
                try
                {
                    if (ship.ParentWorld != null && ship.ParentWorld.Id != SimulationWorld.Id) continue;
                    UpdateShipMass(entry, nowMs);
                    var shipPosition = ship.Transform.World.Position;
                    _prefetchPositions.Add(new Vector2(shipPosition.X, shipPosition.Y));
                    var id = ship.ObjId;
                    if (_pendingHullGeometry.TryGetValue(id, out var geometryLoad) && geometryLoad.IsCompleted)
                    {
                        _pendingHullGeometry.Remove(id);
                        if (geometryLoad.IsCompletedSuccessfully)
                        {
                            var loaded = geometryLoad.Result;
                            var size = ship.Template?.ObbSize ?? Vector3.Zero;
                            if ((!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0f || size.Y <= 0f) &&
                                loaded.Bounds.X > 0f && loaded.Bounds.Y > 0f)
                                _hulls.SetShape(id, loaded.Bounds);
                            if (loaded.Buoyancy is { } buoyancy)
                                _hulls.SetBuoyancy(id, buoyancy, ship.ShipModel.WaterDensity, loaded.BrushBounds);
                        }
                    }
                    if (_waitingForGeometry.Contains(id) && !_pendingHullGeometry.ContainsKey(id) && CollisionReady(new Vector2(shipPosition.X, shipPosition.Y)))
                    {
                        _waitingForGeometry.Remove(id);
                        _hulls.SetHeld(id, false, nowMs);
                    }
                    var zone = (ushort)ship.Transform.ZoneId;
                    _hulls.SetZone(id, zone);
                    var geometry = Volatile.Read(ref _geometry);
                    if (geometry != null)
                    {
                        geometry.GetZone(SimulationWorld.Template, zone);
                    }
                    var (frozen, driveBlocked, turnBlocked) = ship.Buffs.GetShipControlRestrictions();
                    if (frozen)
                    {
                        if (_suspended.Add(id)) _hulls.Deactivate(id, nowMs);
                        continue;
                    }
                    if (_suspended.Remove(id)) _hulls.Activate(id, nowMs);
                    if (!ship.AttachedCharacters.ContainsKey(AttachPointKind.Driver)) _hulls.SetHelm(id, 0f, 0f, nowMs);
                    ShipHarpoonRopeController.TickHarpoonRopeControllerLifetime(ship);
                    var position = ship.Transform.World.Position;
                    var surface = SimulationWorld.Water.GetWaterSurface(position, out _);
                    var groundKnown = SimulationWorld.Template.TryGetHeight(position.X, position.Y, out var ground);
                    var hasWater = float.IsFinite(surface) && (groundKnown ? ground < surface : MathF.Abs(position.Z - surface) < 10f);
                    _hulls.SetEnvironment(id, hasWater, surface, groundKnown ? ground : null);
                    _hulls.SetMovementModifiers(id, ship.MoveSpeedMul, ship.TurnSpeed,
                        _useAuthoredDrive ? GetShipDriveAttributes(ship, !driveBlocked, !turnBlocked) : null);
                    var recoil = ShipHarpoonRopeController.TickTensionTearAndGetHullRecoilDeltaV(ship, dt);
                    if (recoil.LengthSquared() > 0f) ApplyWorldVelocity(id, recoil);
                    _active.Add(id);
                    _snapshot.Add(ship);
                }
                catch (Exception ex) { Logger.Error(ex, "Hull input failed for {0}", ship.ObjId); }
            }
            _hulls.Advance(_active, nowMs, _mass);
            ShipHarpoonTowPhysics.Apply(_snapshot, _hulls, dt);
            PrefetchNearby(nowMs);
            var report = _reportedAt < 0 || nowMs - _reportedAt >= 62;
            if (report) _reportedAt = nowMs;
            foreach (var ship in _snapshot)
            {
                try
                {
                    var pose = _hulls.Pose(ship.ObjId);
                    ship.PhysicsState = new ShipPhysicsState(pose);
                    ship.Transform.Local.SetPosition((float)pose.X, (float)pose.Y, pose.Z);
                    ship.Transform.Local.ApplyFromQuaternion(pose.Rotation);
                    ship.Throttle = (sbyte)Math.Clamp(pose.ThrottleStep, -127, 127);
                    ship.Steering = (sbyte)Math.Clamp(MathF.Round(pose.Steering * 127f), -127f, 127f);
                    ship.Rpm = (sbyte)Math.Clamp(MathF.Round(pose.Rpm * 127f), 0f, 127f);
                    ship.Speed = new Vector2(pose.Velocity.X, pose.Velocity.Y).Length();
                    ship.Transform.FinalizeTransform();
                    if (report) ship.BroadcastPacket(new SCOneUnitMovementPacket(ship.ObjId, ShipMovementCodec.Movement(ship, pose)), false);
                }
                catch (Exception ex) { Logger.Error(ex, "Hull report failed for {0}", ship.ObjId); }
            }
            _collisionReporter.Flush(nowMs, _ships.Keys, _hulls, _reportCollision);
        }
    }

    private void UpdateShipMass(Ship entry, long nowMs)
    {
        if (entry.MassTick == _tickVersion) return;
        var ship = entry.Unit;
        var hullMass = ship.ShipModel.Mass;
        var reportMass = hullMass;
        _aboard.Clear();
        var attributeMass = AttributeMass(ship) + SeatedRiderMass(ship);
        foreach (var part in entry.Parts)
        {
            if (!ReferenceEquals(part.Transform.Parent, ship.Transform)) continue;
            attributeMass += AttributeMass(part) + SeatedRiderMass(part);
            if (float.IsFinite(part.FittedCollisionMass) && part.FittedCollisionMass > 0f)
                reportMass += part.FittedCollisionMass;
        }
        entry.ReportMass = MathF.Max(reportMass, 1f);

        var template = ship.Template;
        if (template is { Capacity: > 0, AddedShipMassPerUnit: > 0f })
        {
            CountLinkedRiders(ship);
            foreach (var part in entry.Parts)
                if (ReferenceEquals(part.Transform.Parent, ship.Transform))
                    CountLinkedRiders(part);
        }

        var mass = entry.Load.Update(nowMs, hullMass, template?.Capacity ?? 0, template?.AddedShipMassPerUnit ?? 0f,
            _aboard.Count, attributeMass, SlaveGameData.Instance.ShipMassReductionPerSecond);
        if (entry.ExtraMass != mass)
        {
            _hulls.SetExtraMass(ship.ObjId, mass);
            entry.ExtraMass = mass;
        }
        entry.MassTick = _tickVersion;
    }

    private float SeatedRiderMass(Slave vehicle)
    {
        var mass = 0f;
        foreach (var (_, rider) in vehicle.AttachedCharacters)
            if (ReferenceEquals(rider.Transform.Parent, vehicle.Transform) && _aboard.Add(rider.ObjId))
                mass += AttributeMass(rider);
        return mass;
    }

    private void CountLinkedRiders(Slave vehicle)
    {
        CountLinkedRidersAt(vehicle.Transform);
        foreach (var doodad in vehicle.AttachedDoodads)
            if (ReferenceEquals(doodad.Transform.Parent, vehicle.Transform))
                CountLinkedRidersAt(doodad.Transform);
    }

    private void CountLinkedRidersAt(Transform attachment)
    {
        attachment.CopyChildrenTo(_attachmentChildren);
        foreach (var child in _attachmentChildren)
        {
            if (!ReferenceEquals(child.Parent, attachment) || child.GameObject is not Unit rider) continue;
            if (ReferenceEquals(child.StickyParent, attachment) ||
                rider is Character character && ReferenceEquals(character.Bonding?.GetOwner()?.Transform, attachment))
                _aboard.Add(rider.ObjId);
        }
        _attachmentChildren.Clear();
    }

    private static float AttributeMass(Unit unit)
    {
        var multiplier = unit.CalculateWithBonuses(0d, UnitAttribute.MassMul);
        var baseMass = multiplier == 0d ? 0d : Math.Truncate((float)multiplier * unit.Facets * 0.001f);
        return (float)Math.Truncate(unit.CalculateWithBonuses(baseMass, UnitAttribute.Mass));
    }

    private void ApplyWorldVelocity(uint id, Vector3 delta)
    {
        var yaw = _hulls.Pose(id).Rotation;
        var local = Vector3.Transform(delta, Quaternion.Conjugate(yaw));
        _hulls.ApplyImpulse(id, new HullImpulse(local, Vector3.Zero, Vector3.Zero, Vector3.Zero));
    }

    internal static ShipDriveAttributes GetShipDriveAttributes(Slave ship, bool canDrive, bool canTurn)
    {
        var facets = Attribute(UnitAttribute.Facets, ship.Facets);
        return new ShipDriveAttributes(
            Attribute(UnitAttribute.MoveSpeedMul) * 0.001f, Attribute(UnitAttribute.TurnSpeed) * 0.01f,
            FacetBonus(UnitAttribute.SteeringSpeed, UnitAttribute.SteeringSpeedMul),
            FacetBonus(UnitAttribute.ReverseVelocity, UnitAttribute.ReverseVelocityMul), canDrive, canTurn);

        float Attribute(UnitAttribute attribute, double baseValue = 0d)
        {
            var value = Math.Truncate(ship.CalculateWithAdditiveBonuses(baseValue, attribute));
            return (float)SlaveGameData.Instance.ClampShipDriveAttribute(attribute, value);
        }
        float FacetBonus(UnitAttribute attribute, UnitAttribute multiplier)
        {
            var baseValue = Math.Truncate(Attribute(multiplier) * facets * 0.001f);
            return Attribute(attribute, baseValue) * 0.001f;
        }
    }

    private void ReportCollision(uint id, HullCollisionWindow contact, float impact)
    {
        if (!_ships.TryGetValue(id, out var ship)) return;
        try
        {
            _ships.TryGetValue(contact.OtherBcId, out var other);
            var mass = _hulls.ReportsImpactMass ? contact.OwnMass : ship.ReportMass;
            SlaveCollisionDamage.ApplyFromImpact(ship.Unit, contact.OwnPart, impact, mass, other?.Unit);
        }
        catch (Exception ex) { Logger.Error(ex, "Hull collision failed for {0}", id); }
    }

    public void StartPhysics()
    {
        lock (_lifetimeLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_running) return;
            Initialize();
            _running = true;
            _thread = new Thread(Run) { Name = "Hull-" + SimulationWorld.Id, IsBackground = true };
            _thread.Start();
        }
    }

    private void Run()
    {
        while (_running)
        {
            var now = Environment.TickCount64;
            try { Tick(now); }
            catch (Exception ex) { Logger.Error(ex, "Hull simulation failed in world {0}", SimulationWorld.Id); }
            Thread.Sleep(15);
        }
    }

    public void Stop()
    {
        Thread thread;
        lock (_lifetimeLock) { _running = false; thread = _thread; }
        if (thread != null && thread != Thread.CurrentThread && !thread.Join(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Hull simulation did not stop in world " + SimulationWorld.Id);
    }

    public void Dispose()
    {
        Stop();
        lock (_tickLock)
        lock (_lifetimeLock)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var ship in _ships.Values) ship.Unit.PhysicsState = null;
            _ships.Clear();
            Volatile.Read(ref _geometry)?.SetRetainedZones(this, SimulationWorld.Template, []);
            _suspended.Clear();
            _waitingForGeometry.Clear();
            _pendingHullGeometry.Clear();
            ShipDoodadCollision.Instance.RemoveWorld(SimulationWorld.Id);
            while (_commands.TryDequeue(out _)) { }
            _hulls?.Dispose();
        }
    }

    private sealed class Ship
    {
        public Ship(Slave unit, float hullMass)
        {
            Unit = unit;
            // SlaveManager fits the children before registering the hull. The roster belongs to this hull lifetime.
            Parts = unit.AttachedSlaves.ToArray();
            ReportMass = MathF.Max(hullMass, 1f);
        }

        public Slave Unit { get; }
        public Slave[] Parts { get; }
        public float ReportMass { get; set; }
        public HullMassState Load { get; } = new();
        public float ExtraMass { get; set; }
        public long MassTick { get; set; }
    }
}
