#nullable enable
using System.Numerics;

using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers.Bots.Navigation;

/// <summary>
/// Triangle nav queries for a world via the shared <see cref="NavCache"/> (same stack as ArcheageZones bots).
/// </summary>
public sealed class NavMeshGround(WorldTemplate template)
{
    private const float LayerEdgeSlack = 0.01f;

    private readonly WorldTemplate _template = template;

    /// <summary>Triangle grounding when <c>AAEMU_TRIANGLE_NAV</c> is not <c>0</c>/<c>false</c> (default: on).</summary>
    public static bool IsEnabled
    {
        get
        {
            var flag = Environment.GetEnvironmentVariable("AAEMU_TRIANGLE_NAV");
            return flag is not ("0" or "false" or "False");
        }
    }

    public bool TryGround(float x, float y, out float z)
    {
        var (block, triangle) = Locate(x, y);
        if (block == null || triangle < 0)
        {
            z = 0f;
            return false;
        }

        z = block.HeightAt(triangle, x, y);
        return true;
    }

    public bool TryGroundNear(float x, float y, float reference, out float z)
    {
        z = 0f;
        var block = BlockAt(x, y);
        if (block == null)
            return false;

        var best = float.PositiveInfinity;
        var height = 0f;
        block.Nearest(x, y, LayerEdgeSlack, t =>
        {
            if (block.InsideDistance(t, x, y) < -LayerEdgeSlack)
                return false;
            var h = block.HeightAt(t, x, y);
            if (MathF.Abs(h - reference) < best)
            {
                best = MathF.Abs(h - reference);
                height = h;
            }

            return false;
        }, out _, out _);

        if (float.IsPositiveInfinity(best))
            return false;

        z = height;
        return true;
    }

    public bool TryStructureFloor(float x, float y, float reference, float maxFlat, float maxRise, out float z)
    {
        z = 0f;
        var (block, triangle) = Locate(x, y);
        if (block == null || triangle < 0 || !block.IsForbidden(triangle))
            return false;

        var waypoint = block.NearestWaypoint(x, y, reference, maxFlat, maxRise, out _);
        if (waypoint < 0)
            return false;

        z = block.Centre(waypoint).Z;
        return true;
    }

    public bool IsWalkable(float x, float y)
    {
        var (block, triangle) = Locate(x, y);
        return block != null && triangle >= 0 && !block.IsForbidden(triangle);
    }

    public NavPlanReport Plan(Vector3 from, Vector3 to, NavOptions? options = null) =>
        NavPlanner.Instance.Plan(_template, from, to, options);

    private (NavBlock? Block, int Triangle) Locate(float x, float y)
    {
        var block = BlockAt(x, y);
        if (block == null)
            return (null, -1);
        return (block, block.Locate(x, y));
    }

    private NavBlock? BlockAt(float x, float y)
    {
        var bx = (int)MathF.Floor(x / NavBlock.Size);
        var by = (int)MathF.Floor(y / NavBlock.Size);
        return NavCache.Instance.Get(_template, bx, by);
    }
}