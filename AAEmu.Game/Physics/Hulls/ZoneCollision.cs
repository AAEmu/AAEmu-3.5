#nullable enable

using AAEmu.Geometry;

namespace AAEmu.Game.Physics.Hulls;

/// <summary>Terrain, client brush geometry and the water surface supplied directly by the game world.</summary>
public sealed record ZoneCollision(Func<float, float, float?>? Terrain, StaticCollision Static, float OceanLevel)
{
    /// <summary>Geometry at a point, including zones reached by the hull before its centre crosses a boundary.</summary>
    public Func<float, float, StaticCollision?>? StaticAt { get; init; }
}
