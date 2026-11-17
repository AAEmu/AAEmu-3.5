using System.Numerics;

namespace AAEmu.Game.Physics.Hulls;

/// <summary>Visits nearby region keys nearest-first without allocating on the simulation thread.</summary>
public static class HullZonePrefetch
{
    public const int RegionMetres = 64;
    public const int ReachMetres = 512;

    public static bool Visit(uint[,] zones, Vector2 position, HashSet<uint> visited, Func<uint, bool> request)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || position.X < 0f || position.Y < 0f) return true;
        var centreX = (int)(position.X / RegionMetres);
        var centreY = (int)(position.Y / RegionMetres);
        var width = zones.GetLength(0);
        var height = zones.GetLength(1);
        if (centreX >= width || centreY >= height) return true;
        var radius = ReachMetres / RegionMetres;
        for (var ring = 0; ring <= radius; ring++)
        for (var dx = -ring; dx <= ring; dx++)
        for (var dy = -ring; dy <= ring; dy++)
        {
            if (Math.Abs(dx) != ring && Math.Abs(dy) != ring) continue;
            var x = centreX + dx;
            var y = centreY + dy;
            if (x < 0 || y < 0 || x >= width || y >= height) continue;
            var key = zones[x, y];
            if (key != 0 && visited.Add(key) && !request(key)) return false;
        }
        return true;
    }
}
