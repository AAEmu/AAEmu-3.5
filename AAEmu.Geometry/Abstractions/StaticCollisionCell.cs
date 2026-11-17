using System.Numerics;

namespace AAEmu.Geometry;

/// <summary>
/// Common constants of the geometry layer.
/// </summary>
public static class StaticGeometry
{
    /// <summary>Cell edge in metres (the client's own cell size).</summary>
    public const int CellSize = 1024;

    /// <summary>Spacing of the water volume lookup grid (metres).</summary>
    public const float WaterGridMetres = 64f;
}

/// <summary>
/// One cell to load static geometry for.
/// </summary>
/// <param name="WorldName">World name, i.e. the <c>worlds/&lt;world&gt;</c> folder.</param>
/// <param name="CellX">Cell index along X.</param>
/// <param name="CellY">Cell index along Y.</param>
/// <param name="OceanLevel">Sea level, for the water volumes.</param>
public readonly record struct StaticCollisionCell(string WorldName, int CellX, int CellY, float OceanLevel)
{
    /// <summary>The cell's folder inside the client data.</summary>
    public string CellFolder => $"game/worlds/{WorldName}/cells/{CellX:000}_{CellY:000}";

    /// <summary>The cell's offset in continent metres.</summary>
    public Vector3 CellOffset => new(CellX * (float)StaticGeometry.CellSize, CellY * (float)StaticGeometry.CellSize, 0f);
}
