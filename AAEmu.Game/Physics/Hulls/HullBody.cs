#nullable enable

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
    private bool _waterContact;

    public float VelocityX => -Surge * MathF.Sin(Yaw) + Sway * MathF.Cos(Yaw);
    public float VelocityY => Surge * MathF.Cos(Yaw) + Sway * MathF.Sin(Yaw);

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
    }

    /// <summary>The throttle as the wire quantises it; thrust and the rpm ramp follow its sign.</summary>
    public int ThrottleStep => (int)MathF.Round(Throttle * 127f);

    /// <summary>Advances one <see cref="StepSeconds"/> step.</summary>
    public void Step()
    {
        const float dt = (float)StepSeconds;
        var m = Model;

        // The hull's steering: a servo with a rate and an acceleration cap.
        var targetRate = Math.Clamp(m.SteerGain * (HelmSteering - Steering), -m.SteerRate, m.SteerRate);
        SteeringRate += Math.Clamp(targetRate - SteeringRate, -m.SteerAccel * dt, m.SteerAccel * dt);
        Steering = Math.Clamp(Steering + SteeringRate * dt, -1f, 1f);

        // The hull's throttle lags the helm's; the engine spins up only while it is ahead.
        Throttle += (HelmThrottle - Throttle) * (1f - MathF.Exp(-dt / m.ThrottleLag));
        var step = ThrottleStep;
        Rpm = step > 0 ? MathF.Min(1f, Rpm + m.RpmUp * dt) : MathF.Max(0f, Rpm - m.RpmDown * dt);

        var u = Surge;
        var v = Sway;
        var w = YawRate;
        var speed = MathF.Sqrt(u * u + v * v);
        var afloat = HasWater && Z - KeelDepth <= WaterLevel && (GroundHeight == null || GroundHeight < WaterLevel);
        if (afloat) _waterContact = true;
        else if (!HasWater || Z > WaterLevel + 20f || GroundHeight >= WaterLevel) _waterContact = false;
        var thrust = afloat ? (step > 0 ? Rpm * (m.ThrustAtRest - m.ThrustFade * u) : step < 0 ? m.ReverseThrust * (step / 127f) : 0f) * MoveSpeedMultiplier : 0f;
        var du = thrust - m.SurgeDrag * u - m.SurgeDrag2 * u * speed + m.SurgeYawSway * w * v;
        var dv = -m.SwayDrag * v - m.SwayDrag2 * v * speed + m.SwayYawSurge * w * u + m.SwaySteerSurge * Steering * u;
        // The rudder's turning power never changes sign: a fit holds only over the speeds it saw, and a seam catch-up
        // can push a hull past them.
        var rudder = MathF.Max(0f, m.YawSteer + m.YawSteerRpm * Rpm + m.YawSteerSurge * u + m.YawSteerSurge2 * u * u);
        var dw = -(afloat ? rudder * Steering * TurnSpeedMultiplier : 0f)
                 - (m.YawDrag + m.YawDragSurge * MathF.Abs(u)) * w - m.YawDrag2 * w * MathF.Abs(w);
        Surge = Math.Clamp(u + du * dt, -MaxSpeed, MaxSpeed);
        Sway = Math.Clamp(v + dv * dt, -MaxSpeed, MaxSpeed);
        YawRate = Math.Clamp(w + dw * dt, -MaxYawRate, MaxYawRate);

        const float k = HullModel.PhysicsTimeScale;
        Yaw = WrapAngle(Yaw + k * YawRate * dt);
        X += k * VelocityX * dt;
        Y += k * VelocityY * dt;

        // Heave: a damped spring to the waterline, which rises a little with speed.
        if (_waterContact)
        {
            var waterline = WaterLevel - m.Draft + m.RisePerSpeed * MathF.Abs(Surge);
            HeaveRate += (-m.HeaveSpring * (Z - waterline) - m.HeaveDamping * HeaveRate) * dt;
        }
        else
            HeaveRate -= 9.81f * dt;
        Z += HeaveRate * dt;
        if (GroundHeight is { } ground && Z < ground + KeelDepth)
        {
            Z = ground + KeelDepth;
            if (HeaveRate < 0f) HeaveRate = 0f;
        }
    }

    /// <summary>
    /// <c>HullImpulse</c>: <paramref name="velocity"/> and <paramref name="angularVelocity"/> are added in the hull's
    /// own frame (x starboard, y bow, z up — the World's seam catch-up puts its speed on +y), the impulses divided by
    /// the mass first.
    /// </summary>
    public void ApplyImpulse(System.Numerics.Vector3 velocity, System.Numerics.Vector3 angularVelocity,
        System.Numerics.Vector3 impulse, System.Numerics.Vector3 angularImpulse)
    {
        if (!IsFinite(velocity) || !IsFinite(angularVelocity) || !IsFinite(impulse) || !IsFinite(angularImpulse))
            return;
        var totalMass = Model.Mass + ExtraMass;
        var mass = float.IsFinite(totalMass) && totalMass > 0f ? MathF.Max(1f, totalMass) : 1f;
        Surge = Math.Clamp(Surge + velocity.Y + impulse.Y / mass, -MaxSpeed, MaxSpeed);
        Sway = Math.Clamp(Sway + velocity.X + impulse.X / mass, -MaxSpeed, MaxSpeed);
        YawRate = Math.Clamp(YawRate + angularVelocity.Z + angularImpulse.Z / mass, -MaxYawRate, MaxYawRate);
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
        float x = qx / 32767f, y = qy / 32767f, z = qz / 32767f;
        var w = MathF.Sqrt(MathF.Max(0f, 1f - x * x - y * y - z * z));
        return MathF.Atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z));
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
