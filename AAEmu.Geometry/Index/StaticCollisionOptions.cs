namespace AAEmu.Geometry;

/// <summary>
/// Where an asset's collision geometry comes from.
/// </summary>
public enum GeometryPhysicsSource
{
    /// <summary>
    /// The asset's CryPhysics proxy (slot 0 of <c>MeshPhysicsData</c>), i.e. what the native dedicate
    /// physicalises. What a player sees may be finer; the proxy is what the game actually collides with.
    /// </summary>
    Proxy,

    /// <summary>The render mesh. For diagnostics and A/B comparison; coarser contacts and more triangles.</summary>
    Render,

    /// <summary>
    /// Proxy where the asset has one, render mesh where it does not. The default: full fidelity without losing
    /// assets that ship no proxy at all.
    /// </summary>
    ProxyThenRender
}

/// <summary>
/// The kill switches of <see cref="StaticCollision"/>, one property per switch under the same name (documented there): on by
/// default, off when the variable is "0".
/// </summary>
public sealed record StaticCollisionOptions
{
    public static StaticCollisionOptions Default { get; } = new();

    /// <summary>
    /// Where a brush's collision mesh comes from. <c>AAEMU_GEOMETRY_PHYSICS_SOURCE=proxy|render|proxy-then-render</c>
    /// (default <see cref="GeometryPhysicsSource.ProxyThenRender"/>).
    /// </summary>
    public GeometryPhysicsSource PhysicsSource { get; init; } = GeometryPhysicsSource.ProxyThenRender;

    public bool UndergroundFloorsEnabled { get; init; } = true;

    public bool UndergroundStructureFloorsEnabled { get; init; } = true;

    public bool InlandWaterEnabled { get; init; } = true;

    public bool SeaWaterEnabled { get; init; } = true;

    public bool InlandFishSwimEnabled { get; init; } = true;

    public bool SwimStanceEnabled { get; init; } = true;

    public bool SettleNoRiseEnabled { get; init; } = true;

    public bool SurfaceDepthByModelEnabled { get; init; } = true;

    /// <summary>
    /// The colliding vegetation is static geometry too (native's dedicate gives it collision):
    /// <c>AAEMU_GEOMETRY_VEGETATION=0</c> turns it off.
    /// </summary>
    public bool VegetationEnabled { get; init; } = true;

    /// <summary>Reads every switch from <paramref name="getEnv"/> (<see cref="Environment.GetEnvironmentVariable(string)"/> in production).</summary>
    public static StaticCollisionOptions FromEnvironment(Func<string, string?> getEnv)
    {
        ArgumentNullException.ThrowIfNull(getEnv);
        return new StaticCollisionOptions
        {
            UndergroundFloorsEnabled = getEnv("AAEMU_ZONE_UNDERGROUND_FLOORS") != "0",
            UndergroundStructureFloorsEnabled = getEnv("AAEMU_ZONE_UNDERGROUND_STRUCTURE") != "0",
            InlandWaterEnabled = getEnv("AAEMU_ZONE_INLAND_WATER") != "0",
            SeaWaterEnabled = getEnv("AAEMU_ZONE_SEA_SWIMMERS") != "0",
            InlandFishSwimEnabled = getEnv("AAEMU_ZONE_INLAND_FISH_SWIM") != "0",
            SwimStanceEnabled = getEnv("AAEMU_ZONE_SWIM_STANCE") != "0",
            SettleNoRiseEnabled = getEnv("AAEMU_ZONE_SETTLE_NO_RISE") != "0",
            SurfaceDepthByModelEnabled = getEnv("AAEMU_ZONE_SURFACE_DEPTH_BY_MODEL") != "0",
            VegetationEnabled = getEnv("AAEMU_GEOMETRY_VEGETATION") != "0",
            PhysicsSource = getEnv("AAEMU_GEOMETRY_PHYSICS_SOURCE")?.Trim().ToLowerInvariant() switch
            {
                "proxy" => GeometryPhysicsSource.Proxy,
                "render" => GeometryPhysicsSource.Render,
                _ => GeometryPhysicsSource.ProxyThenRender
            }
        };
    }
}
