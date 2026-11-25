#nullable enable

using System.Numerics;

namespace AAEmu.Game.Physics.Hulls;

/// <summary>
/// One hull's state and its fixed-step integration (<see cref="HullModel"/>). World frame: x east, y north, z up;
/// yaw 0 has the bow on +y and grows counter-clockwise, so the bow is (−sin yaw, cos yaw) and starboard
/// (cos yaw, sin yaw). <see cref="Surge"/> and <see cref="Sway"/> are the body-frame velocities (m/s per physics
/// second), <see cref="YawRate"/> rad per physics second (the wire's <c>angVel.z</c>).
/// </summary>
public sealed class HullBody
{
    /// <summary>The step the coefficients were fitted with (1/30 s, the native host's frame).</summary>
    public const double StepSeconds = 1.0 / 30.0;
    /// <summary>Ceilings that keep a bad impulse from running the integration away.</summary>
    public const float MaxSpeed = 60f;
    public const float MaxYawRate = 6f;
    private const float Gravity = 9.81f;
    private const float MaxTilt = 1.2f;

    public HullBody(HullModel model) => Model = model;

    public HullModel Model { get; }
    /// <summary>Occupants, equipment and other load beyond the bare ship model.</summary>
    public float ExtraMass { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public float Z { get; set; }
    public float HeaveRate { get; set; }
    public float Yaw { get; set; }
    public float Surge { get; set; }
    public float Sway { get; set; }
    public float YawRate { get; set; }
    internal float Pitch { get; private set; }
    internal float Roll { get; private set; }
    internal float PitchRate { get; private set; }
    internal float RollRate { get; private set; }
    /// <summary>The hull's own steering, −1..1 (the report's <c>steering</c> / 127).</summary>
    public float Steering { get; set; }
    public float SteeringRate { get; set; }
    /// <summary>The hull's own throttle, −1..1 (the report's <c>throttle</c> / 127).</summary>
    public float Throttle { get; set; }
    /// <summary>Engine rpm, 0..1 (the report's <c>rpm</c> / 127).</summary>
    public float Rpm { get; set; }
    /// <summary>The helm, −1..1 (the last <c>ShipRequestMoveType</c> / 127).</summary>
    public float HelmThrottle { get; set; }
    public float HelmSteering { get; set; }
    /// <summary>The water surface the hull floats on (the level's ocean, or the seed's for inland water).</summary>
    public float WaterLevel { get; set; } = 100f;
    public bool HasWater { get; set; } = true;
    public float? GroundHeight { get; set; }
    public float KeelDepth { get; set; } = HullShape.ReferenceKeelDepth;
    public float MoveSpeedMultiplier { get; set; } = 1f;
    public float TurnSpeedMultiplier { get; set; } = 1f;
    internal ShipDriveAttributes DriveAttributes { get; set; } = new(1f, 1f, 0f, 0f, true, true);
    internal ShipDrive? AuthoredDrive { get; set; }
    internal (HullBuoyancy Volumes, double WaterDensity)? Buoyancy { get; set; }
    private bool _waterContact;

    public float VelocityX => -Surge * MathF.Sin(Yaw) + Sway * MathF.Cos(Yaw);
    public float VelocityY => Surge * MathF.Cos(Yaw) + Sway * MathF.Sin(Yaw);

    private Quaternion Tilt =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, Roll) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, Pitch);

    internal Quaternion Orientation(float ahead = 0f)
    {
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Yaw + YawRate * ahead);
        if (!Buoyancy.HasValue) return yaw;
        return yaw * Quaternion.CreateFromAxisAngle(Vector3.UnitY, Math.Clamp(Roll + RollRate * ahead, -MaxTilt, MaxTilt))
                   * Quaternion.CreateFromAxisAngle(Vector3.UnitX, Math.Clamp(Pitch + PitchRate * ahead, -MaxTilt, MaxTilt));
    }

    private Vector3 AngularVelocity => Buoyancy.HasValue
        ? new Vector3(PitchRate * MathF.Cos(Roll), RollRate, YawRate - PitchRate * MathF.Sin(Roll))
        : new Vector3(0f, 0f, YawRate);

    internal Vector3 WorldAngularVelocity => Vector3.Transform(AngularVelocity, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Yaw));

    internal void SeedOrientation(short x, short y, short z, Vector3 worldRate)
    {
        var q = QuaternionFromShorts(x, y, z);
        Yaw = YawFromShorts(x, y, z);
        Pitch = Math.Clamp(MathF.Atan2(2f * (q.W * q.X + q.Y * q.Z), 1f - 2f * (q.X * q.X + q.Y * q.Y)), -MaxTilt, MaxTilt);
        Roll = Math.Clamp(MathF.Asin(Math.Clamp(2f * (q.W * q.Y - q.Z * q.X), -1f, 1f)), -MaxTilt, MaxTilt);
        worldRate = new Vector3(FiniteRate(worldRate.X), FiniteRate(worldRate.Y), FiniteRate(worldRate.Z));
        var rate = Vector3.Transform(worldRate, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -Yaw));
        PitchRate = FiniteRate(rate.X / MathF.Cos(Roll));
        RollRate = rate.Y;
        YawRate = FiniteRate(rate.Z + PitchRate * MathF.Sin(Roll));
    }

    private static float FiniteRate(float value) => float.IsFinite(value) ? Math.Clamp(value, -MaxYawRate, MaxYawRate) : 0f;

    /// <summary>Sets the body-frame velocities from a world-frame one.</summary>
    public void SetWorldVelocity(float vx, float vy)
    {
        Surge = -vx * MathF.Sin(Yaw) + vy * MathF.Cos(Yaw);
        Sway = vx * MathF.Cos(Yaw) + vy * MathF.Sin(Yaw);
    }

    /// <summary>
    /// A helm nobody refreshed for <see cref="IHullPhysics.HelmTimeoutMs"/>: native snaps the hull's own throttle, steering
    /// and rpm to rest at once (6013 in 191: 5.04 s after the driver's last frame), and the hull then drifts to a stop.
    /// </summary>
    public void DropHelm()
    {
        HelmThrottle = 0;
        HelmSteering = 0;
        Throttle = 0;
        Steering = 0;
        SteeringRate = 0;
        Rpm = 0;
        AuthoredDrive?.Reset();
    }

    /// <summary>The throttle as the wire quantises it; thrust and the rpm ramp follow its sign.</summary>
    public int ThrottleStep => (int)MathF.Round(Throttle * 127f);

    /// <summary>Advances one <see cref="StepSeconds"/> step.</summary>
    public void Step(bool dead = false)
    {
        // HullBuilder configures CryPhysics with a 20 ms maximum step. Reports still follow the 30 Hz grid.
        var substeps = AuthoredDrive == null ? 1 : 2;
        for (var substep = 0; substep < substeps; substep++)
            StepSubstep(dead, (float)StepSeconds / substeps);
    }

    internal void StepSubstep(bool dead, float dt)
    {
        var m = Model;

        var waterAvailable = HasWater && (GroundHeight == null || GroundHeight < WaterLevel);
        var useWaterForces = Buoyancy.HasValue && (m.WaterResistance > 0f || m.WaterDamping > 0f);
        var tilt = Buoyancy.HasValue ? Tilt : Quaternion.Identity;
        var toHull = Quaternion.Conjugate(tilt);
        var up = Vector3.Transform(Vector3.UnitZ, toHull);
        var coupled = AuthoredDrive != null;
        // A hull using the fallback planar fit uses geometric resistance for heave and tilt.
        var linear = coupled ? Vector3.Transform(new Vector3(Sway, Surge, HeaveRate), toHull) : up * HeaveRate;
        var angular = Vector3.Transform(AngularVelocity - Vector3.UnitZ * (coupled ? 0f : YawRate), toHull);
        var wet = Buoyancy is { } measured && waterAvailable
            ? measured.Volumes.Measure(up, WaterLevel - Z, m.MassCentre, useWaterForces ? linear : Vector3.Zero,
                useWaterForces ? angular : Vector3.Zero) : default;
        var afloat = waterAvailable && (Buoyancy.HasValue ? wet.Volume > 0d : Z - KeelDepth <= WaterLevel);
        if (afloat) _waterContact = true;
        else if (!HasWater || Z > WaterLevel + 20f || GroundHeight >= WaterLevel) _waterContact = false;

        if (Buoyancy is { } buoyancy)
            StepWater(wet, buoyancy.WaterDensity, buoyancy.Volumes.Capacity, tilt, up, linear, angular, useWaterForces, waterAvailable, dead, dt);
        else if (_waterContact)
        {
            // Fitted heave for hulls without a measured flotation proxy.
            var waterline = WaterLevel - m.Draft + m.RisePerSpeed * MathF.Abs(Surge);
            HeaveRate += (-m.HeaveSpring * (Z - waterline) - m.HeaveDamping * HeaveRate) * dt;
        }
        else
            HeaveRate -= Gravity * dt;
        if (!coupled)
            StepFittedDrive(dt, afloat, dead);
        const float k = HullModel.PhysicsTimeScale;
        var oldOffset = Buoyancy.HasValue ? Vector3.Transform(m.MassCentre, Orientation()) : Vector3.Zero;
        var vx = VelocityX;
        var vy = VelocityY;
        Yaw = WrapAngle(Yaw + k * YawRate * dt);
        if (coupled)
            SetWorldVelocity(vx, vy);
        X += k * VelocityX * dt;
        Y += k * VelocityY * dt;
        Z += HeaveRate * dt;
        if (Buoyancy.HasValue)
        {
            (Pitch, PitchRate) = AdvanceTilt(Pitch, PitchRate, k * dt);
            (Roll, RollRate) = AdvanceTilt(Roll, RollRate, k * dt);
            var shift = oldOffset - Vector3.Transform(m.MassCentre, Orientation());
            X += shift.X;
            Y += shift.Y;
            Z += shift.Z;
        }
        if (GroundHeight is { } ground && Z < ground + KeelDepth)
        {
            Z = ground + KeelDepth;
            if (HeaveRate < 0f) HeaveRate = 0f;
        }
    }

    private void StepFittedDrive(float dt, bool afloat, bool dead)
    {
        var m = Model;
        var targetRate = Math.Clamp(m.SteerGain * (HelmSteering - Steering), -m.SteerRate, m.SteerRate);
        SteeringRate += Math.Clamp(targetRate - SteeringRate, -m.SteerAccel * dt, m.SteerAccel * dt);
        Steering = Math.Clamp(Steering + SteeringRate * dt, -1f, 1f);
        var previousThrottle = Throttle;
        Throttle += (HelmThrottle - Throttle) * (1f - MathF.Exp(-dt / m.ThrottleLag));
        var step = ThrottleStep;
        if (!dead)
        {
            if (Throttle * previousThrottle < 0f)
                Rpm = 0f;
            else
                Rpm = step == 0 ? MathF.Max(0f, Rpm - m.RpmDown * dt) : MathF.Min(1f, Rpm + m.RpmUp * dt);
        }

        var u = Surge;
        var v = Sway;
        var w = YawRate;
        var speed = MathF.Sqrt(u * u + v * v);
        var powered = afloat && !dead;
        var reverseLimit = m.ReverseSpeedLimit * MoveSpeedMultiplier;
        var thrust = 0f;
        if (powered && step > 0)
            thrust = Rpm * (m.ThrustAtRest - m.ThrustFade * u) * MoveSpeedMultiplier;
        else if (powered && step < 0 && reverseLimit > 0f)
        {
            // Counter-thrust can brake forward motion while reverse RPM is still building.
            var braking = Math.Clamp(u / reverseLimit, 0f, 1f);
            var reversePower = Rpm + (1f - Rpm) * braking;
            var remainingSpeed = Math.Clamp(1f + u / reverseLimit, 0f, 1f);
            thrust = m.ReverseThrust * (step / 127f) * reversePower * remainingSpeed * MoveSpeedMultiplier;
        }
        if (ExtraMass > 0f)
            thrust *= m.Mass / (m.Mass + ExtraMass);
        var du = thrust + m.SurgeYawSway * w * v - m.SurgeDrag * u - m.SurgeDrag2 * u * speed;
        var dv = m.SwayYawSurge * w * u + (dead ? 0f : m.SwaySteerSurge * Steering * u) - m.SwayDrag * v - m.SwayDrag2 * v * speed;
        var surgeSpeed = MathF.Abs(u);
        var rudder = MathF.Max(0f, m.YawSteer + m.YawSteerRpm * Rpm + m.YawSteerSurge * surgeSpeed + m.YawSteerSurge2 * u * u);
        var turnDirection = step < 0 || (step == 0 && u < 0f) ? 1f : -1f;
        var dw = (powered ? turnDirection * rudder * Steering * TurnSpeedMultiplier : 0f)
                 - (m.YawDrag + m.YawDragSurge * surgeSpeed) * w - m.YawDrag2 * w * MathF.Abs(w);
        Surge = Math.Clamp(u + du * dt, -MaxSpeed, MaxSpeed);
        Sway = Math.Clamp(v + dv * dt, -MaxSpeed, MaxSpeed);
        YawRate = Math.Clamp(w + dw * dt, -MaxYawRate, MaxYawRate);
    }

    private void StepWater(HullBuoyancy.Submersion wet, double density, double capacity, Quaternion tilt, Vector3 up,
        Vector3 linear, Vector3 angular, bool useWaterForces, bool waterAvailable, bool dead, float dt)
    {
        var mass = (double)Model.Mass + ExtraMass;
        // r575 CRigidEntity (RVA 0xc0e50) applies gravity against the displaced water mass.
        var acceleration = Gravity * (wet.Volume * density / mass - 1d);
        var velocityDelta = Vector3.Zero;
        var angularDelta = Vector3.Zero;
        var damp = 0f;
        if (wet.Volume > 0d)
        {
            var torque = Vector3.Cross(wet.FirstMoment, up) * (float)(Gravity * density);
            angularDelta = InverseInertia(torque, mass) * dt;
            damp = Model.HeaveDamping;
            if (useWaterForces)
            {
                // r575 scales each part's integral by the area's resistance and the model multiplier.
                var dragScale = 1000f * Model.WaterResistance * dt;
                var dragDelta = wet.Drag * (float)(dragScale / mass);
                var dragAngularDelta = InverseInertia(wet.DragTorque, mass) * dragScale;
                var power = Vector3.Dot(linear, wet.Drag) + Vector3.Dot(angular, wet.DragTorque);
                var slope = Vector3.Dot(dragDelta, wet.Drag) + Vector3.Dot(dragAngularDelta, wet.DragTorque);
                // Clamp stiff impulses before applying their heave and tilt components.
                var scale = slope > 0f ? Math.Clamp(-power / slope, 0f, 1f) : 0f;
                velocityDelta = dragDelta * scale;
                angularDelta += dragAngularDelta * scale;
                damp = Model.WaterDamping;
            }
        }

        if (AuthoredDrive is { } drive)
        {
            // CRigidEntity (r575 0xc0e50) blends body and water damping by the submerged share of the flotation parts.
            var immersion = capacity > 0d ? (float)Math.Clamp(wet.Volume / capacity, 0d, 1d) : 0f;
            damp = float.Lerp(dead ? 0.2f : 0.1f, Model.WaterDamping, immersion);
            var control = drive.Step(this, linear, angular, up, dt, waterAvailable, dead);
            (Throttle, Steering, Rpm) = (control.Throttle, control.Steering, control.Rpm);
            velocityDelta += control.Acceleration * dt;
            var rudder = InverseInertia(control.TorquePerMass, 1d);
            var angularAcceleration = rudder.Length();
            if (angularAcceleration > 5f)
                rudder *= 5f / angularAcceleration;
            angularDelta += rudder * dt;
        }

        var damping = MathF.Exp(-damp * dt);
        var delta = Vector3.Transform(velocityDelta, tilt);
        var rate = Vector3.Transform(angularDelta, tilt);
        var pitchDelta = rate.X / MathF.Cos(Roll);
        PitchRate = FiniteRate(PitchRate * damping + pitchDelta);
        RollRate = FiniteRate(RollRate * damping + rate.Y);
        if (AuthoredDrive != null)
        {
            Surge = Math.Clamp((Surge + delta.Y) * damping, -MaxSpeed, MaxSpeed);
            Sway = Math.Clamp((Sway + delta.X) * damping, -MaxSpeed, MaxSpeed);
            YawRate = FiniteRate(YawRate * damping + rate.Z + pitchDelta * MathF.Sin(Roll));
        }
        HeaveRate = (float)Math.Clamp((HeaveRate + delta.Z) * damping + acceleration * dt, -MaxSpeed, MaxSpeed);
    }

    private static (float Angle, float Rate) AdvanceTilt(float angle, float rate, float dt)
    {
        var target = angle + rate * dt;
        return MathF.Abs(target) <= MaxTilt ? (target, rate) : (Math.Clamp(target, -MaxTilt, MaxTilt), 0f);
    }

    private Vector3 InverseInertia(Vector3 torque, double mass) =>
        new((float)(torque.X / (mass * Model.PitchInertiaPerMass)), (float)(torque.Y / (mass * Model.RollInertiaPerMass)),
            (float)(torque.Z / (mass * Model.YawInertiaPerMass)));

    /// <summary>
    /// <c>HullImpulse</c>: <paramref name="velocity"/> and <paramref name="angularVelocity"/> are added in the hull's
    /// own frame (x starboard, y bow, z up). Linear and angular impulses use the mass and inertia of the loaded hull.
    /// </summary>
    public void ApplyImpulse(System.Numerics.Vector3 velocity, System.Numerics.Vector3 angularVelocity,
        System.Numerics.Vector3 impulse, System.Numerics.Vector3 angularImpulse)
    {
        if (!IsFinite(velocity) || !IsFinite(angularVelocity) || !IsFinite(impulse) || !IsFinite(angularImpulse))
            return;
        var totalMass = Model.Mass + ExtraMass;
        var mass = float.IsFinite(totalMass) && totalMass > 0f ? MathF.Max(1f, totalMass) : 1f;
        // r575 cryphysics_dedicate.dll, CRigidEntity::Action (RVA 0xc5d30), impulse branch.
        var tilt = Buoyancy.HasValue ? Tilt : Quaternion.Identity;
        var linear = Vector3.Transform(velocity + impulse / mass, tilt);
        Surge = Math.Clamp(Surge + linear.Y, -MaxSpeed, MaxSpeed);
        Sway = Math.Clamp(Sway + linear.X, -MaxSpeed, MaxSpeed);
        HeaveRate = Math.Clamp(HeaveRate + linear.Z, -MaxSpeed, MaxSpeed);
        var delta = angularVelocity + InverseInertia(angularImpulse, mass);
        if (Buoyancy.HasValue)
        {
            delta = Vector3.Transform(delta, tilt);
            var pitchDelta = delta.X / MathF.Cos(Roll);
            PitchRate = FiniteRate(PitchRate + pitchDelta);
            RollRate = FiniteRate(RollRate + delta.Y);
            delta.Z += pitchDelta * MathF.Sin(Roll);
        }
        YawRate = FiniteRate(YawRate + delta.Z);
    }

    internal static bool IsFinite(System.Numerics.Vector3 vector) =>
        float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);

    public static float WrapAngle(float angle)
    {
        if (!float.IsFinite(angle))
            return 0f;
        angle %= 2 * MathF.PI;
        while (angle > MathF.PI)
            angle -= 2 * MathF.PI;
        while (angle < -MathF.PI)
            angle += 2 * MathF.PI;
        return angle;
    }

    /// <summary>Yaw from the wire's quaternion shorts (x, y, z × 32767; w rebuilt as +√(1 − |v|²)).</summary>
    public static float YawFromShorts(short qx, short qy, short qz)
    {
        var q = QuaternionFromShorts(qx, qy, qz);
        return MathF.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
    }

    private static Quaternion QuaternionFromShorts(short x, short y, short z)
    {
        var v = new Vector3(x, y, z) / 32767f;
        var length = v.LengthSquared();
        return length > 1f ? new Quaternion(Vector3.Normalize(v), 0f) : new Quaternion(v, MathF.Sqrt(1f - length));
    }

    /// <summary>The level hull's quaternion shorts for <paramref name="yaw"/> (w kept positive, as the reader rebuilds it).</summary>
    public static (short X, short Y, short Z) ShortsFromYaw(float yaw)
    {
        var z = MathF.Sin(yaw / 2);
        var w = MathF.Cos(yaw / 2);
        if (w < 0)
            z = -z;
        return (0, 0, (short)Math.Clamp(MathF.Round(z * 32767f), -32767, 32767));
    }

    /// <summary>The wire's quaternion shorts for <paramref name="q"/> (x, y, z × 32767; w kept positive, as the reader rebuilds it).</summary>
    public static (short X, short Y, short Z) ShortsFromQuaternion(System.Numerics.Quaternion q)
    {
        if (q.W < 0)
            q = new System.Numerics.Quaternion(-q.X, -q.Y, -q.Z, -q.W);
        return (Short(q.X), Short(q.Y), Short(q.Z));
    }

    private static short Short(float component) => (short)Math.Clamp(MathF.Round(component * 32767f), -32767, 32767);
}
