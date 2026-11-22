#nullable enable

using System.Numerics;

using AAEmu.Game.Models.Game.Models;
using AAEmu.Geometry;

namespace AAEmu.Game.Physics.Hulls;

/// <summary>The ArcheageZones ship controller with the 3.5 ship row supplying the engine coefficients.</summary>
internal sealed class ShipDrive(ShipModelV1 model, GeometryBounds bounds, Func<Vector3, bool>? isWaterAt)
{
    private const float RudderAngle = MathF.PI / 4f;
    private const float Gravity = 9.8f;
    private const float MaxAccelerationSquared = 1e6f;
    private readonly Vector3 _stern = new(0f, bounds.Min.Y, bounds.Min.Z);
    private readonly Vector3 _bow = new(0f, bounds.Max.Y, bounds.Min.Z);
    private readonly float _length = bounds.Max.Y - bounds.Min.Y;
    private float _throttleRate;
    private float _steeringRate;
    private float _lastAcceleration;

    internal readonly record struct Output(float Throttle, float Steering, float Rpm, Vector3 Acceleration, Vector3 TorquePerMass);

    internal static ShipDrive? Create(ShipModelV1? model, GeometryBounds? bounds, Func<Vector3, bool>? isWaterAt)
    {
        // ShipPrefab.Bounds is the origin when there are no mesh slots.
        var box = bounds.GetValueOrDefault();
        if (model == null || !HullBody.IsFinite(box.Min) || !HullBody.IsFinite(box.Max) || box.Max.Y < box.Min.Y)
            return null;
        if (!(float.IsFinite(model.Velocity) && model.Velocity >= 0f && Positive(model.MaxRpmSec) && Positive(model.MinRpmSec) &&
              Positive(model.AccelExponent) &&
              float.IsFinite(model.WaterResistance) && model.WaterResistance >= 0f &&
              float.IsFinite(model.Accel) && model.Accel >= 0f && float.IsFinite(model.ReverseAccel) && model.ReverseAccel >= 0f &&
              float.IsFinite(model.ReverseVelocity) && model.ReverseVelocity >= 0f && float.IsFinite(model.TurnAccel) && model.TurnAccel >= 0f &&
              float.IsFinite(model.HaltRate) && model.HaltRate >= 0f))
            return null;
        return new ShipDrive(model, box, isWaterAt);
    }

    private static bool Positive(float value) => float.IsFinite(value) && value > 0f;

    internal void Reset() => _throttleRate = _steeringRate = _lastAcceleration = 0f;

    internal Output Step(HullBody body, Vector3 velocity, Vector3 spin, Vector3 up, float dt, bool hasWater, bool dead)
    {
        var attributes = body.DriveAttributes;
        var throttle = Smooth(body.Throttle, body.HelmThrottle, ref _throttleRate, dt, 0.2f);
        var steeringTime = 1f / Math.Clamp(1f + attributes.SteeringBonus, 0.5f, 10f);
        var steering = Smooth(body.Steering, body.HelmSteering, ref _steeringRate, dt, steeringTime);
        if (dead)
            return new Output(throttle, steering, body.Rpm, Vector3.Zero, Vector3.Zero);
        var surface = body.WaterLevel - body.Z;
        var sternDepth = Vector3.Dot(up, _stern);
        var bowDepth = Vector3.Dot(up, _bow);
        var sternWet = hasWater && sternDepth < surface;
        var bowWet = hasWater && bowDepth < surface;
        if (isWaterAt != null)
        {
            var rotation = body.Orientation();
            var pivot = new Vector3((float)body.X, (float)body.Y, body.Z);
            sternWet = isWaterAt(pivot + Vector3.Transform(_stern, rotation));
            bowWet = isWaterAt(pivot + Vector3.Transform(_bow, rotation));
        }
        var ground = sternWet ? bowWet ? 0 : 1 : bowWet ? -1 : Math.Sign(bowDepth - sternDepth);

        var reverse = throttle < 0f;
        var speedLimit = attributes.CanDrive
            ? attributes.SpeedMultiplier * (reverse ? model.ReverseVelocity + attributes.ReverseSpeedBonus : model.Velocity) : 0f;
        speedLimit = MathF.Max(0f, speedLimit);
        var ratio = speedLimit > 0f ? Math.Clamp(MathF.Abs(velocity.Y) / speedLimit, 0f, 1f) : 1f;
        var rpm = body.Rpm + dt / model.MaxRpmSec * MathF.Abs(throttle) * (1f - MathF.Pow(ratio, model.AccelExponent));
        rpm = MathF.Abs(throttle) < 0.01f ? Math.Clamp(rpm - dt / model.MinRpmSec, 0f, ratio) : Math.Clamp(rpm, 0f, 1f);
        var accel = throttle > 0f ? model.Accel : model.ReverseAccel;
        var forward = rpm > 0.01f
            ? Math.Sign(throttle) * (throttle * velocity.Y > 0f ? 1f - ratio : 1f) * Gravity * accel * rpm
            : -Math.Sign(velocity.Y) * Gravity * accel * model.HaltRate * ratio;
        forward *= body.Model.Mass / (body.Model.Mass + body.ExtraMass);
        if (speedLimit == 0f || (!sternWet && !bowWet) || ground * forward > 0f)
            forward = 0f;
        if (MathF.Abs(_lastAcceleration) > 0.1f && MathF.Abs(forward) > 0.1f && Math.Sign(_lastAcceleration) != Math.Sign(forward))
            rpm = 0f;
        _lastAcceleration = forward;

        var turn = attributes.CanTurn ? attributes.TurnMultiplier * model.TurnAccel : 0f;
        if (!sternWet || turn <= 0f || _length == 0f)
            return Forces(new Vector3(0f, forward, 0f), Vector3.Zero);

        var blade = Vector3.Transform(new Vector3(0f, -0.05f * _length, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, steering * RudderAngle));
        var flow = velocity + Vector3.Cross(spin, _stern);
        var flowSpeed = flow.Length();
        var thrust = Vector3.Zero;
        if (speedLimit > 0f && flowSpeed > 0.000345f)
        {
            var bladeDirection = Vector3.Normalize(blade);
            var alignment = MathF.Abs(Vector3.Dot(bladeDirection, flow / flowSpeed));
            var factor = Math.Clamp((1f - alignment) * flowSpeed / speedLimit, 0f, 1f);
            var normal = new Vector3(bladeDirection.Y, -bladeDirection.X, 0f);
            if (Vector3.Dot(normal, flow) > 0f)
                normal = -normal;
            thrust = normal * (10f * turn * factor);
        }
        var torque = Vector3.Cross(_stern + blade, thrust);
        // The final min-torque branch in ServerShipSimulationController is signed by steering and drive direction.
        torque.Z = (reverse ? 1f : -1f) * turn * _length / 3f * steering * MathF.Abs(steering);
        return Forces(thrust + new Vector3(0f, forward, 0f), torque);

        Output Forces(Vector3 acceleration, Vector3 moment) =>
            acceleration.LengthSquared() <= MaxAccelerationSquared && HullBody.IsFinite(moment)
                ? new Output(throttle, steering, rpm, acceleration, moment)
                : new Output(throttle, steering, rpm, Vector3.Zero, Vector3.Zero);
    }

    private static float Smooth(float value, float target, ref float rate, float dt, float smoothTime)
    {
        var omega = 2f / smoothTime;
        var x = omega * dt;
        var decay = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        var change = value - target;
        var temp = (rate + omega * change) * dt;
        rate = (rate - omega * temp) * decay;
        return target + (change + temp) * decay;
    }
}
