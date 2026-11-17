#nullable enable

using System.Numerics;

using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Physics.Hulls;

/// <summary>The hardest contact since the previous collision report.</summary>
public readonly record struct HullCollisionWindow(Vector2 Point, float Z, byte OwnPart, uint SourceBcId,
    byte SourcePart, float SourceMass, float Impact, HullContactKind Kind, uint OtherBcId, float OwnMass = 0f);

/// <summary>A body-frame velocity change and force impulse applied directly to a hull.</summary>
public readonly record struct HullImpulse(Vector3 Velocity, Vector3 AngularVelocity, Vector3 Impulse, Vector3 AngularImpulse);

/// <summary>A physical blast in one world instance. Radius is authored in millimetres.</summary>
public readonly record struct HullExplosion(int World, uint TargetBcId, double X, double Y, float Z, float Radius, float HoleSize,
    float Pressure);

/// <summary>In-process ship simulation. Identifiers belong to the owning game world; no transport is involved.</summary>
public interface IHullPhysics : IDisposable
{
    const int HelmTimeoutMs = 5000;

    bool CollisionsEnabled { get; set; }
    bool UsesRestingImpact { get; }
    bool ReportsImpactMass { get; }
    long ContactSteps { get; }
    void Add(uint bcId, ushort zoneKey, ShipModelV1? ship, float shipMass, Vector3 authoredSize = default);
    void Remove(uint bcId);
    void SetZone(uint bcId, ushort zoneKey);
    void SetWorld(uint bcId, int world);
    void Seed(uint bcId, ShipMoveType pose, float? oceanLevel, long nowMs);
    void Activate(uint bcId, long nowMs);
    void Deactivate(uint bcId, long nowMs);
    void SetHeld(uint bcId, bool held, long nowMs);
    void SetShape(uint bcId, Vector3 authoredSize);
    void SetHelm(uint bcId, float throttle, float steering, long nowMs);
    void ApplyImpulse(uint bcId, HullImpulse impulse, Vector3? source = null);
    void Explode(HullExplosion blast);
    void SetExtraMass(uint bcId, float mass);
    void SetWaterLevel(uint bcId, float waterLevel);
    void SetEnvironment(uint bcId, bool hasWater, float waterLevel, float? groundHeight);
    void SetMovementModifiers(uint bcId, float moveSpeedMultiplier, float turnSpeedMultiplier);
    void Advance(IReadOnlyList<uint> active, long nowMs, Func<uint, float> reportMass);
    HullPose Pose(uint bcId);
    void TakeCollisionWindows(uint bcId, List<HullCollisionWindow> into);
    long? FirstContactMs(uint bcId);
}
