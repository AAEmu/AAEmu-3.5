using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Physics.Hulls;

namespace AAEmu.Game.Physics;

/// <summary>Adapts internal hull poses to the existing 3.5 client movement format.</summary>
public static class ShipMovementCodec
{
    // AAC 3503 decodes the signed ship velocity components as value / 32767 * 30 m/s.
    private const float MaxVelocity = 30f;
    public const float VelocityScale = short.MaxValue / MaxVelocity;

    public static short Velocity(float value) => float.IsFinite(value)
        ? (short)MathF.Floor(Math.Clamp(value, -MaxVelocity, MaxVelocity) * VelocityScale + 0.5f)
        : (short)0;

    public static ShipMoveType Movement(Slave slave, HullPose pose)
    {
        var movement = new ShipMoveType { Type = MoveTypeEnum.Ship };
        movement.UseSlaveBase(slave);
        (movement.RotationX, movement.RotationY, movement.RotationZ) = HullBody.ShortsFromQuaternion(pose.Rotation);
        movement.X = (float)pose.X;
        movement.Y = (float)pose.Y;
        movement.Z = pose.Z;
        movement.VelX = Velocity(pose.Velocity.X);
        movement.VelY = Velocity(pose.Velocity.Y);
        movement.VelZ = Velocity(pose.Velocity.Z);
        movement.AngVelX = pose.AngularVelocity.X;
        movement.AngVelY = pose.AngularVelocity.Y;
        movement.AngVelZ = pose.AngularVelocity.Z;
        movement.Steering = (sbyte)Math.Clamp(MathF.Round(pose.Steering * 127f), -127f, 127f);
        movement.Throttle = (sbyte)Math.Clamp(pose.ThrottleStep, -127, 127);
        movement.Rpm = (sbyte)Math.Clamp(MathF.Round(pose.Rpm * 127f), 0f, 127f);
        return movement;
    }
}
