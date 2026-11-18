using System.Numerics;

namespace AAEmu.Game.Core.Managers.Bots.Navigation;

/// <summary>
/// A way across a block's side into the neighbour block: the neighbour's triangle, the stretch of the side the two
/// share (<see cref="Start"/> to <see cref="End"/>, on the leaving block's edge), how wide a walker may be to cross
/// there, and the water over it.
/// </summary>
public readonly record struct NavSeamLink(int Triangle, Vector3 Start, Vector3 End, float Radius, float Depth);

/// <summary>
/// Joins neighbouring blocks at their shared side (DESIGN 8.3). No link in the client files crosses a block, but
/// both blocks have triangle edges along the side line (within <c>Navigation.SeamToleranceMeters</c>, 0.1 m), usually split at the same points. Every pair of edges, one from each block, that overlaps along the line
/// is a way across through the overlapping stretch, so edges split at different points still join.
/// </summary>
public static class NavSeamStitcher
{
    /// <summary>
    /// Adds to <paramref name="into"/> every way from seam edge <paramref name="edge"/> of <paramref name="from"/>,
    /// on <paramref name="side"/>, into <paramref name="to"/>, the block across that side, in the order of
    /// <paramref name="to"/>'s edges along the side. Blocked ways are added too, with a radius below 0, so callers
    /// see them; the search skips them.
    /// </summary>
    public static void Links(NavBlock from, int side, in NavSeamEdge edge, NavBlock to, List<NavSeamLink> into)
    {
        if (from == null || to == null || side is < NavRules.West or > NavRules.North)
            return;
        var (dx, dy) = NavRules.Step(side);
        if (to.X != from.X + dx || to.Y != from.Y + dy)
            return;

        var alongY = side is NavRules.West or NavRules.East;
        var p = from.Corner(edge.Triangle, edge.Edge);
        var q = from.Corner(edge.Triangle, (edge.Edge + 1) % 3);
        var fromForbidden = from.IsForbidden(edge.Triangle);
        foreach (var other in to.Seams[NavRules.Opposite(side)])
        {
            if (other.Lo >= edge.Hi)
                break;
            var lo = MathF.Max(edge.Lo, other.Lo);
            var hi = MathF.Min(edge.Hi, other.Hi);
            var overlap = hi - lo;
            if (overlap < NavRules.MinSeamOverlap)
                continue;
            var radius = NavRules.SeamRadius(overlap, edge.OutRadius, other.InRadius, fromForbidden, to.IsForbidden(other.Triangle));
            into.Add(new NavSeamLink(other.Triangle, At(p, q, lo, alongY), At(p, q, hi, alongY), radius,
                MathF.Max(edge.Depth, other.Depth)));
        }
    }

    /// <summary>The point of edge p to q at <paramref name="along"/> (its y when <paramref name="alongY"/>, else its x).</summary>
    private static Vector3 At(Vector3 p, Vector3 q, float along, bool alongY)
    {
        var span = alongY ? q.Y - p.Y : q.X - p.X;
        var t = MathF.Abs(span) < 1e-6f ? 0f : Math.Clamp((along - (alongY ? p.Y : p.X)) / span, 0f, 1f);
        var point = Vector3.Lerp(p, q, t);
        // Exactly on the shared stretch along the side; the edge only decides the offset across it (under 0.1 m).
        return alongY ? point with { Y = along } : point with { X = along };
    }
}
