namespace AAEmu.Geometry;

/// <summary>The stance a unit should be in, as far as the water decides it.</summary>
public enum WaterStance
{
    /// <summary>Not in water, or the rules say nothing: the caller keeps its own stance.</summary>
    Unchanged,

    /// <summary>Standing in shallow water.</summary>
    Stand,

    /// <summary>In the water column: moving by swimming.</summary>
    Swim,

    /// <summary>At the bottom, below the stand threshold: settled on the bed.</summary>
    Settle
}

/// <summary>
/// The water stance rules of a zone, ported from ArcheageZones' <c>ZoneWater</c> (plan P3) and pinned to the same
/// thresholds: a ground unit at least <see cref="MinDepthMetres"/> under a surface swims when it is more than
/// <see cref="OffBedMetres"/> above the floor or at least <see cref="BedDepthMetres"/> under the surface, and settles
/// on the bed below the minimum depth.
/// <para>
/// The thresholds are the ones fitted on native traces (sea traces 172, 173, 245, 257, 281, 344 of 2026-11-08:
/// in the water column 99 % of records are Swim; on the bed 0.25–0.75 m under the surface 437 of 739 are Swim,
/// 1.25 m and deeper 971 of 993; within 0.3 m of the surface none).
/// </para>
/// </summary>
public static class StanceRules
{
    /// <summary>Shallower than this under a surface, a ground unit stands (and does not swim).</summary>
    public const float MinDepthMetres = ZoneWater.SwimStanceMinDepthMetres;

    /// <summary>Closer than this to the floor, a unit is on the bed.</summary>
    public const float OffBedMetres = ZoneWater.SwimStanceOffBedMetres;

    /// <summary>This deep under the surface, a unit on the bed still counts as swimming.</summary>
    public const float BedDepthMetres = ZoneWater.SwimStanceBedDepthMetres;

    /// <summary>Water shallower than this never changes a stance.</summary>
    public const float ShallowWaterMetres = 0.3f;

    /// <summary>
    /// The stance the water puts a ground unit in.
    /// </summary>
    /// <param name="depthUnderSurface">Metres from the surface down to the unit (negative above the surface).</param>
    /// <param name="heightAboveFloor">Metres from the floor up to the unit.</param>
    /// <param name="floorKnown">Whether the floor under the unit is known at all.</param>
    /// <returns>The stance, or <see cref="WaterStance.Unchanged"/> when the water decides nothing.</returns>
    public static WaterStance Resolve(float depthUnderSurface, float heightAboveFloor, bool floorKnown = true)
    {
        if (!StaticCollision.SwimStanceEnabled)
            return WaterStance.Unchanged;

        // Above the surface, or barely in it: the water says nothing.
        if (depthUnderSurface < MinDepthMetres)
            return WaterStance.Unchanged;

        // Deep in the water column and off the bed: swimming, whatever the floor says.
        if (depthUnderSurface >= BedDepthMetres)
            return WaterStance.Swim;

        // Near the bed: swim only if there is a gap under the unit; otherwise the water is shallow enough to settle.
        if (!floorKnown || heightAboveFloor > OffBedMetres)
            return WaterStance.Swim;
        return WaterStance.Settle;
    }

    /// <summary>Shorthand for the common case: a surface and a floor, both known.</summary>
    public static WaterStance Resolve(float surfaceZ, float unitZ, float floorZ) =>
        Resolve(surfaceZ - unitZ, unitZ - floorZ);
}
