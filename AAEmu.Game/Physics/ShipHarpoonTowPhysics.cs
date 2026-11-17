using System.Numerics;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Physics.Hulls;

namespace AAEmu.Game.Physics;

/// <summary>Adapts gameplay harpoon ropes to body-frame impulses; no engine-specific rigid bodies.</summary>
public static class ShipHarpoonTowPhysics
{
    public const float ServerRopePaidLengthAdditiveMeters = 12.5f;
    public const float SlackMarginMeters = 0.5f;
    private const float TowMaxAcceleration = 2f;

    public static IEnumerable<Slave> EnumerateAttachedSlaveDescendants(Slave root)
    {
        foreach (var child in root.AttachedSlaves)
        {
            yield return child;
            foreach (var descendant in EnumerateAttachedSlaveDescendants(child)) yield return descendant;
        }
    }

    public static void Apply(IReadOnlyList<Slave> ships, IHullPhysics physics, float dt)
    {
        if (dt <= 0f) return;
        foreach (var ship in ships)
        foreach (var cannon in EnumerateAttachedSlaveDescendants(ship))
        {
            var rope = cannon.HarpoonRope;
            if (!rope.IsEngaged) continue;
            var hook = ShipHarpoonRopeController.GetHookWorldPosition(cannon);
            var stretch = Vector3.Distance(cannon.Transform.World.Position, hook) - rope.RopeLength - ServerRopePaidLengthAdditiveMeters;
            if (!float.IsFinite(stretch) || stretch <= SlackMarginMeters) continue;
            var pose = physics.Pose(ship.ObjId);
            var delta = hook - new Vector3((float)pose.X, (float)pose.Y, pose.Z);
            delta.Z = 0f;
            var length = delta.Length();
            if (!float.IsFinite(length) || length < 0.2f) continue;
            var change = delta / length * MathF.Min(stretch, TowMaxAcceleration) * dt;
            Slave other = null;
            if (!rope.HookAttachedToTerrain)
            {
                foreach (var candidate in ships)
                    if (candidate.ObjId == rope.HookBasisObjId && candidate.ObjId != ship.ObjId) { other = candidate; break; }
                if (other == null) continue;
            }
            var ownShare = 1f;
            if (other != null)
            {
                var ownMass = MathF.Max(ship.ShipModel?.Mass ?? 1f, 1f);
                var otherMass = MathF.Max(other.ShipModel?.Mass ?? 1f, 1f);
                ownShare = otherMass / (ownMass + otherMass);
                Push(physics, other.ObjId, -change * (1f - ownShare));
            }
            Push(physics, ship.ObjId, change * ownShare);
        }
    }

    private static void Push(IHullPhysics physics, uint id, Vector3 worldVelocity)
    {
        var local = Vector3.Transform(worldVelocity, Quaternion.Conjugate(physics.Pose(id).Rotation));
        physics.ApplyImpulse(id, new HullImpulse(local, Vector3.Zero, Vector3.Zero, Vector3.Zero));
    }
}
