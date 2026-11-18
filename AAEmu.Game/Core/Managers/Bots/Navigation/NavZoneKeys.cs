using System.Numerics;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers.Bots.Navigation;

/// <summary>
/// The zone keys a route passes through (DESIGN 8.5: a straight leg in one zone can clip a region of another along
/// an irregular border, so every 64 m region a leg crosses is checked, not just its ends).
/// </summary>
public static class NavZoneKeys
{
    /// <summary>
    /// Every zone key under <paramref name="waypoints"/>, in the order first entered, the start's first. A region with
    /// no zone gives key 0, which is listed like any other so a walker can refuse it. Empty when the world has no
    /// region map.
    /// </summary>
    public static List<uint> Along(WorldTemplate world, IReadOnlyList<Vector3> waypoints)
    {
        var keys = new List<uint>();
        var map = world?.ZoneKeyByRegions;
        if (map == null || waypoints == null || waypoints.Count == 0)
            return keys;

        void Visit(int regionX, int regionY)
        {
            if (regionX < 0 || regionY < 0 || regionX >= map.GetLength(0) || regionY >= map.GetLength(1))
                return;
            var key = map[regionX, regionY];
            if (!keys.Contains(key))
                keys.Add(key);
        }

        const float size = WorldManager.REGION_SIZE;
        Visit(Region(waypoints[0].X), Region(waypoints[0].Y));
        for (var i = 1; i < waypoints.Count; i++)
        {
            var from = waypoints[i - 1];
            var to = waypoints[i];
            int x = Region(from.X), y = Region(from.Y);
            int endX = Region(to.X), endY = Region(to.Y);
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var stepX = Math.Sign(dx);
            var stepY = Math.Sign(dy);
            // Grid walk: the fraction of the leg at which it next crosses a region line in x and in y.
            var nextX = stepX == 0 ? float.PositiveInfinity : ((x + (stepX > 0 ? 1 : 0)) * size - from.X) / dx;
            var nextY = stepY == 0 ? float.PositiveInfinity : ((y + (stepY > 0 ? 1 : 0)) * size - from.Y) / dy;
            var deltaX = stepX == 0 ? float.PositiveInfinity : size / MathF.Abs(dx);
            var deltaY = stepY == 0 ? float.PositiveInfinity : size / MathF.Abs(dy);
            var steps = Math.Abs(endX - x) + Math.Abs(endY - y);
            for (var s = 0; s < steps; s++)
            {
                if (nextX <= nextY)
                {
                    x += stepX;
                    nextX += deltaX;
                }
                else
                {
                    y += stepY;
                    nextY += deltaY;
                }

                Visit(x, y);
            }
        }

        return keys;
    }

    private static int Region(float coordinate) => (int)MathF.Floor(coordinate / WorldManager.REGION_SIZE);
}
