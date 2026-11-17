#nullable enable

using System.Numerics;

namespace AAEmu.Game.Physics.Hulls;

/// <summary>
/// A hull's state as its report carries it: continent position, rotation, world velocity (m/s), angular velocity (rad per
/// physics second), and the hull's own steering (−1..1), throttle step (the wire's −127..127) and rpm (0..1).
/// </summary>
public readonly record struct HullPose(double X, double Y, float Z, Quaternion Rotation, Vector3 Velocity,
    Vector3 AngularVelocity, float Steering, int ThrottleStep, float Rpm);
