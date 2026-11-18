using System.Numerics;

namespace AAEmu.Game.Core.Managers.Bots.Navigation;

/// <summary>
/// Funnel smoothing (string pulling, DESIGN 8.3): turns a corridor of portals into the fewest straight legs that
/// pass through every portal in order, so the route stays inside the corridor and turns only at portal ends. The
/// portal ends are pulled in by the walker's radius first, so it turns a radius away from the corner it rounds.
/// </summary>
public static class NavFunnel
{
    /// <summary>Two waypoints closer than this, in flat metres, are one.</summary>
    private const float SamePoint = 0.001f;

    /// <summary>
    /// The waypoints from <paramref name="start"/> through <paramref name="portals"/> (in walking order, ends named
    /// as seen walking through) to <paramref name="goal"/>, start and goal included. A turn's height is the ground
    /// height at that portal end.
    /// </summary>
    public static List<Vector3> StringPull(Vector3 start, Vector3 goal, IReadOnlyList<NavPortal> portals, float actorRadius)
    {
        var count = (portals?.Count ?? 0) + 2;
        var lefts = new Vector3[count];
        var rights = new Vector3[count];
        lefts[0] = rights[0] = start;
        for (var i = 1; i < count - 1; i++)
        {
            var portal = portals[i - 1];
            var (left, right) = Shrink(portal.Left, portal.Right, actorRadius);
            lefts[i] = left;
            rights[i] = right;
        }

        lefts[count - 1] = rights[count - 1] = goal;

        var path = new List<Vector3> { start };
        var apex = start;
        var funnelLeft = start;
        var funnelRight = start;
        int apexIndex = 0, leftIndex = 0, rightIndex = 0;
        // Each restart moves the apex forward, so this bound is never reached on a sane corridor.
        var guard = count * count + 16;
        for (var i = 1; i < count; i++)
        {
            if (--guard < 0)
                break;
            var left = lefts[i];
            var right = rights[i];

            // The right end narrows the funnel when it is left of (or on) the right edge of it.
            if (Cross(apex, funnelRight, right) >= 0f)
            {
                if (Same(apex, funnelRight) || Cross(apex, funnelLeft, right) < 0f)
                {
                    funnelRight = right;
                    rightIndex = i;
                }
                else
                {
                    // Crossed over the left edge: the left end is a turn, and the funnel restarts there.
                    Add(path, funnelLeft);
                    apex = funnelLeft;
                    apexIndex = leftIndex;
                    funnelLeft = funnelRight = apex;
                    leftIndex = rightIndex = apexIndex;
                    i = apexIndex;
                    continue;
                }
            }

            // The left end narrows it when it is right of (or on) the left edge.
            if (Cross(apex, funnelLeft, left) <= 0f)
            {
                if (Same(apex, funnelLeft) || Cross(apex, funnelRight, left) > 0f)
                {
                    funnelLeft = left;
                    leftIndex = i;
                }
                else
                {
                    Add(path, funnelRight);
                    apex = funnelRight;
                    apexIndex = rightIndex;
                    funnelLeft = funnelRight = apex;
                    leftIndex = rightIndex = apexIndex;
                    i = apexIndex;
                }
            }
        }

        if (!Same(path[^1], goal) || path.Count == 1)
            path.Add(goal);
        else
            path[^1] = goal;
        return path;
    }

    /// <summary>Pulls both ends of a portal in by <paramref name="radius"/>, never past its middle; heights follow.</summary>
    public static (Vector3 Left, Vector3 Right) Shrink(Vector3 left, Vector3 right, float radius)
    {
        var length = NavGeometry.Flat(left, right);
        if (radius <= 0f || length < 1e-6f)
            return (left, right);
        var t = MathF.Min(radius / length, 0.5f);
        return (Vector3.Lerp(left, right, t), Vector3.Lerp(right, left, t));
    }

    private static float Cross(Vector3 a, Vector3 b, Vector3 c) =>
        NavGeometry.Cross(NavGeometry.Xy(a), NavGeometry.Xy(b), NavGeometry.Xy(c));

    private static bool Same(Vector3 a, Vector3 b) => NavGeometry.Flat(a, b) < SamePoint;

    private static void Add(List<Vector3> path, Vector3 point)
    {
        if (!Same(path[^1], point))
            path.Add(point);
    }
}
