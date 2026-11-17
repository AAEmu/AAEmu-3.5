#nullable enable

using System.Numerics;

using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Geometry;

namespace AAEmu.Game.Physics.Hulls;

/// <summary>
/// The fitted hull model: <see cref="HullBody"/> integrates <see cref="HullModel"/> in fixed steps, <see cref="HullContacts"/>
/// resolves the seabed under the keel, static brushes, ship-blocking doodads and other hulls. Each hull collides with the
/// terrain and brushes of its own zone (<paramref name="zones"/>; null while that zone has none) and with the doodads and
/// hulls of its own physical world (<see cref="SetWorld"/>). A dead hull (<paramref name="isDead"/>) rests once its wreck has settled on a
/// contact when <paramref name="restDeadOnly"/> is enabled: it is not stepped or collided until an impulse, a blast or a hull closing on it wakes
/// it, and it reports nothing more until it is revived or seeded.
/// </summary>
/// <remarks>
/// Not native: no buoyancy roll or pitch (reported level), contacts from a box and sampled rays, not the hull's physics
/// proxy.
/// </remarks>
public sealed class FittedHullPhysics(Func<ushort, ZoneCollision?> zones, Func<int, StaticCollision?>? doodads = null,
    Func<uint, bool>? isDead = null, bool restDeadOnly = true)
    : IHullPhysics
{
    /// <summary>A dead hull on a contact whose speeds (m/s, rad/s) are all under this has settled: native's reporter drops an impact this small.</summary>
    public const float SettledSpeed = 0.01f;

    public const float StaticSourceMass = 10000f;
    public const double MaxCatchUpMs = 250.0;

    private readonly object _sync = new();
    private readonly bool _deadRest = restDeadOnly;
    private readonly Dictionary<uint, Hull> _hulls = [];
    private readonly List<HullContact> _contacts = [];
    private readonly List<Hull> _pairs = [];
    private bool _collisionsEnabled = true;
    private bool _poseAtTickTime;
    private long _contactSteps;

    public bool CollisionsEnabled
    {
        get { lock (_sync) return _collisionsEnabled; }
        set { lock (_sync) _collisionsEnabled = value; }
    }
    /// <summary>Reports carry the pose at the tick's time: the last step's state carried on for the time since that step.</summary>
    public bool PoseAtTickTime
    {
        get { lock (_sync) return _poseAtTickTime; }
        set { lock (_sync) _poseAtTickTime = value; }
    }
    public bool UsesRestingImpact => true;
    public bool ReportsImpactMass => false;
    public long ContactSteps { get { lock (_sync) return _contactSteps; } }

    /// <summary>Whether the hull is a rested wreck, out of the physics (tests).</summary>
    public bool Rests(uint bcId) { lock (_sync) return _hulls.TryGetValue(bcId, out var hull) && hull.Resting; }

    public void Add(uint bcId, ushort zoneKey, ShipModelV1? ship, float shipMass, Vector3 authoredSize = default)
    {
        lock (_sync) AddCore(bcId, zoneKey, ship, shipMass, authoredSize);
    }

    private void AddCore(uint bcId, ushort zoneKey, ShipModelV1? ship, float shipMass, Vector3 authoredSize)
    {
        var shape = HullShape.For(ship, authoredSize);
        _hulls[bcId] = new Hull(bcId, new HullBody(HullModel.For(ship)) { KeelDepth = shape.KeelDepth }, shape, HullContacts.Outline(shape), shipMass) { ZoneKey = zoneKey };
    }

    public void Remove(uint bcId)
    {
        lock (_sync)
        {
            _hulls.Remove(bcId);
            _pairs.Clear();
        }
    }

    public void SetZone(uint bcId, ushort zoneKey) { lock (_sync) _hulls[bcId].ZoneKey = zoneKey; }

    public void SetWorld(uint bcId, int world) { lock (_sync) _hulls[bcId].World = world; }

    public void SetExtraMass(uint bcId, float mass)
    {
        lock (_sync) _hulls[bcId].Body.ExtraMass = float.IsFinite(mass) ? MathF.Max(0f, mass) : 0f;
    }

    public void SetWaterLevel(uint bcId, float waterLevel)
    {
        if (!float.IsFinite(waterLevel))
            return;
        lock (_sync) _hulls[bcId].Body.WaterLevel = waterLevel;
    }

    public void SetEnvironment(uint bcId, bool hasWater, float waterLevel, float? groundHeight)
    {
        lock (_sync)
        {
            var body = _hulls[bcId].Body;
            body.HasWater = hasWater && float.IsFinite(waterLevel);
            if (float.IsFinite(waterLevel)) body.WaterLevel = waterLevel;
            body.GroundHeight = groundHeight is { } ground && float.IsFinite(ground) ? ground : null;
        }
    }

    public void SetMovementModifiers(uint bcId, float moveSpeedMultiplier, float turnSpeedMultiplier)
    {
        lock (_sync)
        {
            var body = _hulls[bcId].Body;
            if (float.IsFinite(moveSpeedMultiplier) && moveSpeedMultiplier >= 0f)
                body.MoveSpeedMultiplier = MathF.Min(moveSpeedMultiplier, 10f);
            if (float.IsFinite(turnSpeedMultiplier) && turnSpeedMultiplier >= 0f)
                body.TurnSpeedMultiplier = MathF.Min(turnSpeedMultiplier, 10f);
        }
    }

    public void Seed(uint bcId, ShipMoveType pose, float? oceanLevel, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(pose);
        if (!float.IsFinite(pose.X) || !float.IsFinite(pose.Y) || !float.IsFinite(pose.Z))
            return;
        lock (_sync) SeedCore(bcId, pose, oceanLevel, nowMs);
    }

    private void SeedCore(uint bcId, ShipMoveType pose, float? oceanLevel, long nowMs)
    {
        var hull = _hulls[bcId];
        var body = hull.Body;
        body.X = pose.X;
        body.Y = pose.Y;
        body.Z = pose.Z;
        body.HeaveRate = 0;
        body.Yaw = HullBody.YawFromShorts(pose.RotationX, pose.RotationY, pose.RotationZ);
        body.SetWorldVelocity(pose.VelX * 1f / 2048f, pose.VelY * 1f / 2048f);
        body.YawRate = float.IsFinite(pose.AngVelZ) ? Math.Clamp(pose.AngVelZ, -HullBody.MaxYawRate, HullBody.MaxYawRate) : 0f;
        body.Steering = Math.Clamp(pose.Steering / 127f, -1f, 1f);
        body.SteeringRate = 0;
        body.Throttle = Math.Clamp(pose.Throttle / 127f, -1f, 1f);
        body.Rpm = Math.Clamp(pose.Rpm / 127f, 0f, 1f);
        body.HelmThrottle = body.Throttle;
        body.HelmSteering = body.Steering;
        var ocean = oceanLevel is { } level && float.IsFinite(level) ? level : 100f;
        body.WaterLevel = MathF.Abs(pose.Z - ocean) < 2f ? ocean : pose.Z + body.Model.Draft;
        hull.Seeded = true;
        hull.Resting = false;
        hull.Quiet = false;
        hull.HelmAtMs = nowMs; // a seed carries the helm state across a seam
        hull.ReportPose = null;
        hull.SimulatedToMs = nowMs;
        hull.WindowContact = null;
        hull.WindowImpact = 0f;
    }

    public void Activate(uint bcId, long nowMs)
    {
        lock (_sync)
        {
            var hull = _hulls[bcId];
            hull.Controlled = true;
            hull.SimulatedToMs = nowMs;
        }
    }

    public void Deactivate(uint bcId, long nowMs)
    {
        lock (_sync)
        {
            var hull = _hulls[bcId];
            hull.Controlled = hull.Active = false;
            hull.SimulatedToMs = nowMs;
            hull.Body.DropHelm();
            hull.HelmAtMs = -1;
            hull.ReportPose = null;
        }
    }

    public void SetHeld(uint bcId, bool held, long nowMs)
    {
        lock (_sync)
        {
            var hull = _hulls[bcId];
            if (hull.Held == held) return;
            hull.Held = held;
            hull.Active = false;
            hull.SimulatedToMs = nowMs;
            hull.ReportPose = null;
        }
    }

    public void SetShape(uint bcId, Vector3 authoredSize)
    {
        lock (_sync)
        {
            var hull = _hulls[bcId];
            hull.Shape = HullShape.FromAuthoredSize(authoredSize);
            hull.Outline = HullContacts.Outline(hull.Shape);
            hull.Body.KeelDepth = hull.Shape.KeelDepth;
        }
    }

    public void SetHelm(uint bcId, float throttle, float steering, long nowMs)
    {
        lock (_sync) SetHelmCore(bcId, throttle, steering, nowMs);
    }

    private void SetHelmCore(uint bcId, float throttle, float steering, long nowMs)
    {
        var hull = _hulls[bcId];
        if (!hull.Controlled || !float.IsFinite(throttle) || !float.IsFinite(steering))
            return;
        hull.Body.HelmThrottle = Math.Clamp(throttle, -1f, 1f);
        hull.Body.HelmSteering = Math.Clamp(steering, -1f, 1f);
        hull.HelmAtMs = nowMs;
    }

    /// <summary>The fitted model turns an impulse by the hull's heading whatever its source.</summary>
    public void ApplyImpulse(uint bcId, HullImpulse impulse, Vector3? source = null)
    {
        if (!HullBody.IsFinite(impulse.Velocity) || !HullBody.IsFinite(impulse.AngularVelocity)
            || !HullBody.IsFinite(impulse.Impulse) || !HullBody.IsFinite(impulse.AngularImpulse))
            return;
        lock (_sync) ApplyImpulseCore(bcId, impulse);
    }

    private void ApplyImpulseCore(uint bcId, HullImpulse impulse)
    {
        var hull = _hulls[bcId];
        if (hull.Held) return;
        hull.Body.ApplyImpulse(impulse.Velocity, impulse.AngularVelocity, impulse.Impulse, impulse.AngularImpulse);
        hull.ReportPose = null;
        hull.Resting = false;
    }

    /// <summary>
    /// A physical explosion as the World pushed each body with it before the ship host raised it: every simulated hull of the blast's world
    /// but its target within the radius over 1000 horizontally (strictly) takes the direction from the centre times
    /// pressure · (1 − distance / that radius) as its velocity change and impulse, measured from its reported pose as the World measured.
    /// </summary>
    public void Explode(HullExplosion blast)
    {
        lock (_sync) ExplodeCore(blast);
    }

    private void ExplodeCore(HullExplosion blast)
    {
        var radius = blast.Radius / 1000f;
        if (!float.IsFinite(radius) || radius <= 0f || !float.IsFinite(blast.Pressure) || blast.Pressure <= 0f)
            return;
        var centre = new Vector3((float)blast.X, (float)blast.Y, blast.Z);
        if (!HullBody.IsFinite(centre))
            return;
        foreach (var hull in _hulls.Values)
        {
            if (hull.BcId == blast.TargetBcId || hull.World != blast.World || !(hull.Active || hull.Resting))
                continue;
            var reported = Pose(hull.BcId);
            var at = new Vector3((float)reported.X, (float)reported.Y, reported.Z);
            var dx2 = (at.X - centre.X) * (at.X - centre.X);
            var dy2 = (at.Y - centre.Y) * (at.Y - centre.Y);
            if (dx2 + dy2 >= radius * radius)
                continue;
            var delta = at - centre;
            var distance = delta.Length();
            var direction = distance > 0.001f ? delta / distance : Vector3.UnitZ;
            var force = blast.Pressure * Math.Clamp(1f - distance / radius, 0f, 1f);
            if (force <= 0f)
                continue;
            var push = direction * force;
            hull.Body.ApplyImpulse(push, Vector3.Zero, push, Vector3.Zero);
            hull.ReportPose = null;
            hull.Resting = false;
        }
    }

    public void Advance(IReadOnlyList<uint> active, long nowMs, Func<uint, float> reportMass)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(reportMass);
        lock (_sync) AdvanceCore(active, nowMs, reportMass);
    }

    private void AdvanceCore(IReadOnlyList<uint> active, long nowMs, Func<uint, float> reportMass)
    {
        foreach (var hull in _hulls.Values)
        {
            hull.Active = false;
            hull.ReportPose = null;
        }

        foreach (var bcId in active)
        {
            var hull = _hulls[bcId];
            if (hull.Held)
            {
                hull.SimulatedToMs = nowMs;
                continue;
            }
            if (!hull.Seeded || !hull.Controlled)
                continue;
            var stepMs = HullBody.StepSeconds * 1000;
            if (nowMs < hull.SimulatedToMs)
                hull.SimulatedToMs = nowMs;
            else if (nowMs - hull.SimulatedToMs > MaxCatchUpMs)
                hull.SimulatedToMs = nowMs - MaxCatchUpMs;
            if ((hull.Resting || hull.Quiet) && !IsDead(bcId))
                hull.Resting = hull.Quiet = false;
            if (hull.Resting)
            {
                // A moored obstacle to the other hulls; its clock keeps up so it does not catch up when woken.
                hull.SimulatedToMs += Math.Floor((nowMs - hull.SimulatedToMs) / stepMs) * stepMs;
                continue;
            }

            hull.Active = true;
            hull.Touched = false;
            while (hull.SimulatedToMs + stepMs <= nowMs)
            {
                if (hull.HelmAtMs >= 0 && hull.SimulatedToMs - hull.HelmAtMs > IHullPhysics.HelmTimeoutMs)
                {
                    hull.Body.DropHelm();
                    hull.HelmAtMs = -1;
                }

                hull.Body.Step();
                hull.Touched = CollisionsEnabled && Collide(hull);
                hull.SimulatedToMs += stepMs;
            }

            // Hull-hull contacts below resolve after the tick's reports were taken.
            hull.ReportPose = PoseAtTickTime ? PoseOf(hull.Body, (nowMs - hull.SimulatedToMs) / 1000.0) : PoseOf(hull.Body);
        }

        if (CollisionsEnabled)
            CollidePairs(reportMass);
        foreach (var bcId in active)
        {
            var hull = _hulls[bcId];
            if (!hull.Active)
                continue;
            hull.Resting = hull.Touched && _deadRest && IsDead(bcId) && Settled(hull.Body);
            if (hull.Resting && !hull.Quiet)
            {
                hull.Quiet = true;
                LeaveWindow(hull, reportMass);
            }
        }
    }

    public HullPose Pose(uint bcId)
    {
        lock (_sync) return PoseCore(bcId);
    }

    private HullPose PoseCore(uint bcId)
    {
        var hull = _hulls[bcId];
        return hull.ReportPose ?? PoseOf(hull.Body);
    }

    public void TakeCollisionWindows(uint bcId, List<HullCollisionWindow> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        lock (_sync)
        {
            if (TakeCollisionWindow(bcId) is { } window)
                into.Add(window);
        }
    }

    private HullCollisionWindow? TakeCollisionWindow(uint bcId)
    {
        if (!_hulls.TryGetValue(bcId, out var hull) || hull.WindowContact is not { } contact)
            return null;

        var (source, sourcePart, sourceMass) = hull.WindowSource;
        var window = new HullCollisionWindow(contact.Point, contact.Z, HullContacts.Part(hull.Body, hull.Shape, contact), source, sourcePart,
            sourceMass, hull.WindowImpact, contact.Kind, contact.OtherBcId);
        hull.WindowContact = null;
        hull.WindowImpact = 0f;
        return window;
    }

    public long? FirstContactMs(uint bcId) { lock (_sync) return _hulls.TryGetValue(bcId, out var hull) ? hull.FirstContactMs : null; }

    public void Dispose()
    {
        lock (_sync)
        {
            _hulls.Clear();
            _pairs.Clear();
            _contacts.Clear();
        }
    }

    private static HullPose PoseOf(HullBody body) =>
        new(body.X, body.Y, body.Z, new Quaternion(0f, 0f, MathF.Sin(body.Yaw / 2), MathF.Cos(body.Yaw / 2)),
            new Vector3(body.VelocityX, body.VelocityY, body.HeaveRate), new Vector3(0f, 0f, body.YawRate),
            body.Steering, body.ThrottleStep, body.Rpm);

    /// <summary>The pose <paramref name="aheadSeconds"/> after the body's last step, at its velocities.</summary>
    private static HullPose PoseOf(HullBody body, double aheadSeconds)
    {
        const float k = HullModel.PhysicsTimeScale;
        var ahead = (float)aheadSeconds;
        var yaw = HullBody.WrapAngle(body.Yaw + k * body.YawRate * ahead);
        return new HullPose(body.X + k * body.VelocityX * aheadSeconds, body.Y + k * body.VelocityY * aheadSeconds, body.Z + body.HeaveRate * ahead,
            new Quaternion(0f, 0f, MathF.Sin(yaw / 2), MathF.Cos(yaw / 2)), new Vector3(body.VelocityX, body.VelocityY, body.HeaveRate),
            new Vector3(0f, 0f, body.YawRate), body.Steering, body.ThrottleStep, body.Rpm);
    }

    private bool IsDead(uint bcId) => isDead?.Invoke(bcId) == true;

    private static bool Settled(HullBody body) =>
        MathF.Abs(body.Surge) < SettledSpeed && MathF.Abs(body.Sway) < SettledSpeed && MathF.Abs(body.HeaveRate) < SettledSpeed
        && MathF.Abs(body.YawRate) < SettledSpeed;

    /// <summary>One step's static contacts of a hull: impulses for every contact, the push-out from the deepest wall. False: none.</summary>
    private bool Collide(Hull hull)
    {
        _contacts.Clear();
        var zone = zones(hull.ZoneKey);
        HullContacts.FindStatic(hull.Body, hull.Shape, hull.Outline, zone?.Terrain, zone?.Static ?? StaticCollision.Empty, doodads?.Invoke(hull.World), _contacts, zone?.StaticAt);
        if (_contacts.Count == 0)
            return false;
        _contactSteps++;
        hull.FirstContactMs ??= (long)hull.SimulatedToMs;
        var mass = hull.Body.Model.Mass > 0 ? hull.Body.Model.Mass : hull.ShipMass;
        HullContact? deepestWall = null;
        _contacts.Sort(ContactDepthComparer.Instance);
        foreach (var contact in _contacts)
        {
            if (contact.Kind != HullContactKind.Terrain && deepestWall == null)
                deepestWall = contact;
            var removed = HullContacts.Resolve(hull.Body, hull.Shape, mass, contact, pushOut: false);
            Note(hull, contact, removed, 0, 0, StaticSourceMass);
        }

        if (deepestWall is { } wall)
        {
            var push = wall.Normal * MathF.Min(wall.Penetration, HullContacts.MaxPushPerStep);
            hull.Body.X += push.X;
            hull.Body.Y += push.Y;
        }

        return true;
    }

    /// <summary>Hull against hull, once per tick for every pair with at least one simulated hull.</summary>
    private void CollidePairs(Func<uint, float> reportMass)
    {
        if (_hulls.Count < 2)
            return;
        _pairs.Clear();
        foreach (var hull in _hulls.Values)
            if (hull.Seeded && !hull.Held)
                _pairs.Add(hull);
        var hulls = _pairs;
        for (var i = 0; i < hulls.Count; i++)
        for (var j = i + 1; j < hulls.Count; j++)
        {
            var a = hulls[i];
            var b = hulls[j];
            if (a.World != b.World || (!a.Active && !b.Active))
                continue;
            if (!a.Active || (a.Quiet && b.Active && !b.Quiet))
                (a, b) = (b, a); // a moves and reports; b may be moored or a quiet wreck
            var contact = HullContacts.FindPair(a.Body, a.Shape, a.Outline, b.Body, b.Shape, b.BcId);
            if (contact == null)
            {
                var reverse = HullContacts.FindPair(b.Body, b.Shape, b.Outline, a.Body, a.Shape, a.BcId);
                if (reverse == null)
                    continue;
                // b's point inside a: the same contact seen from a, its push-out reversed.
                contact = reverse.Value with { Normal = -reverse.Value.Normal, OtherBcId = b.BcId };
            }

            if (b.Resting && HullContacts.ClosingSpeed(a.Body, b.Body, contact.Value) > SettledSpeed)
            {
                // A hull closing on a rested wreck wakes it; one lying against it does not.
                b.Resting = false;
                b.Active = true;
            }

            _contactSteps++;
            a.Touched = b.Touched = true;
            var massA = ContactMass(a, reportMass(a.BcId));
            var massB = ContactMass(b, reportMass(b.BcId));
            var removed = HullContacts.ResolvePair(a.Body, a.Shape, massA, b.Body, b.Shape, massB, b.Active, contact.Value);
            var partB = HullContacts.Part(b.Body, b.Shape, contact.Value);
            Note(a, contact.Value, removed, b.BcId, partB, massB);
        }
    }

    /// <summary>
    /// A wreck turning quiet leaves its window's contact with another hull to that hull, so the contact goes out once; a static contact from
    /// before goes out at the next flush.
    /// </summary>
    private void LeaveWindow(Hull wreck, Func<uint, float> reportMass)
    {
        if (wreck.WindowContact is not { } contact || wreck.WindowSource.BcId is not (not 0 and var other) || !_hulls.TryGetValue(other, out var hull) ||
            hull.Quiet)
            return;
        Note(hull, contact with { Normal = -contact.Normal, OtherBcId = wreck.BcId }, wreck.WindowImpact, wreck.BcId,
            HullContacts.Part(wreck.Body, wreck.Shape, contact), reportMass(wreck.BcId));
        wreck.WindowContact = null;
        wreck.WindowImpact = 0f;
    }

    /// <summary>Keeps the hardest contact of the hull's current report window; a quiet wreck takes none.</summary>
    private static void Note(Hull hull, HullContact contact, float impact, uint sourceBcId, byte sourcePart, float sourceMass)
    {
        if (hull.Quiet || (hull.WindowContact != null && impact <= hull.WindowImpact))
            return;
        hull.WindowContact = contact;
        hull.WindowImpact = impact;
        hull.WindowSource = (sourceBcId, sourcePart, sourceMass);
    }

    private static float ContactMass(Hull hull, float reported) =>
        float.IsFinite(reported) && reported > 0f ? reported : MathF.Max(1f, hull.Body.Model.Mass + hull.Body.ExtraMass);

    private sealed class ContactDepthComparer : IComparer<HullContact>
    {
        public static ContactDepthComparer Instance { get; } = new();
        public int Compare(HullContact x, HullContact y) => y.Penetration.CompareTo(x.Penetration);
    }

    private sealed class Hull(uint bcId, HullBody body, HullShape shape, List<Vector2> outline, float shipMass)
    {
        public uint BcId { get; } = bcId;
        public HullBody Body { get; } = body;
        public HullShape Shape { get; set; } = shape;
        public List<Vector2> Outline { get; set; } = outline;
        public float ShipMass { get; } = shipMass;
        public ushort ZoneKey { get; set; }
        public int World { get; set; }
        public bool Seeded { get; set; }
        public bool Held { get; set; }
        public bool Controlled { get; set; }
        /// <summary>Simulated in the latest <see cref="Advance"/>.</summary>
        public bool Active { get; set; }
        /// <summary>A dead hull's settled wreck: not stepped or collided, a moored obstacle to hulls that do not close on it.</summary>
        public bool Resting { get; set; }
        /// <summary>The hull's last step touched something static, or another hull touched it this tick.</summary>
        public bool Touched { get; set; }
        /// <summary>A dead hull that has rested: it takes no new contact into its window until it is revived or seeded.</summary>
        public bool Quiet { get; set; }
        /// <summary>The pose after this hull's steps in the latest <see cref="Advance"/>; null after a seed or impulse.</summary>
        public HullPose? ReportPose { get; set; }
        /// <summary>The hardest contact since the last collision report, the speed it removed and what was hit.</summary>
        public HullContact? WindowContact { get; set; }
        public float WindowImpact { get; set; }
        public (uint BcId, byte Part, float Mass) WindowSource { get; set; }
        public long? FirstContactMs { get; set; }
        public double SimulatedToMs { get; set; }
        /// <summary>When the helm (or a seed) last set the hull's input; -1 once dropped.</summary>
        public long HelmAtMs { get; set; } = -1;
    }
}
