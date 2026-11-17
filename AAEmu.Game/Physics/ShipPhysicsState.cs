using AAEmu.Game.Physics.Hulls;

namespace AAEmu.Game.Physics;

/// <summary>Immutable snapshot published atomically by the world's simulation thread.</summary>
public sealed record ShipPhysicsState(HullPose Pose);
