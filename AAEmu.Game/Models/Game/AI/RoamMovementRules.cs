using System.Numerics;

using AAEmu.Game.Core.Managers.Bots.Navigation;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.AI;

/// <summary>
/// Отказ от roam-цели с слишком длинным маршрутом относительно хорды (порт <c>MovementRules.IsRoamDetour</c>).
/// </summary>
public static class RoamMovementRules
{
    public const float RoamDetourFactor = 2f;
    public const float RoamDetourSlackMetres = 3f;

    /// <summary>Попыток выбрать roam-цель до паузы (Zones <c>MovementRules.RoamTargetAttempts</c>).</summary>
    public const int RoamTargetAttempts = 4;

    /// <summary>Промежуточная точка A*-ноги roam (короче, чем designer patrol 2.5 m).</summary>
    public const float RoamWaypointArrivalMetres = 0.75f;

    /// <summary>Окончание roam-ноги у цели (legacy roam.lua ~1 m).</summary>
    public const float RoamGoalArrivalMetres = 1f;

    /// <summary>Высота луча P6 при проверке прямой roam-ноги (середина корпуса).</summary>
    public const float RoamWallProbeHeightMetres = 1.0f;

    public static bool Enabled
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("AAEMU_ROAM_DETOUR")
                ?? Environment.GetEnvironmentVariable("AAEMU_ZONE_ROAM_DETOUR");
            return env is not ("0" or "false" or "False");
        }
    }

    /// <summary>
    /// Не выбирать roam-цель за стеной по прямой (Zones): по умолчанию с
    /// <c>StaticGeometryEnabled</c>; <c>AAEMU_ROAM_WALL_CHECK=0</c> выкл.
    /// </summary>
    public static bool WallCheckEnabled
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("AAEMU_ROAM_WALL_CHECK");
            if (env is "0" or "false" or "False")
                return false;
            if (env is "1" or "true" or "True")
                return true;
            return AppConfiguration.Instance?.World?.StaticGeometryEnabled ?? false;
        }
    }

    public static float Flat(Vector3 from, Vector3 to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public static float RouteLength(IReadOnlyList<Vector3> waypoints)
    {
        if (waypoints.Count < 2)
            return 0f;
        var length = 0f;
        for (var i = 1; i < waypoints.Count; i++)
            length += Flat(waypoints[i - 1], waypoints[i]);
        return length;
    }

    public static bool IsRoamDetour(float routeMetres, float chordMetres) =>
        Enabled && routeMetres > RoamDetourFactor * chordMetres + RoamDetourSlackMetres;

    /// <summary>Короткая дистанция — без A*; иначе маршрут обязан существовать и не быть чрезмерным крюком.</summary>
    public static bool IsAcceptableRoamPath(IReadOnlyList<Vector3> path, float chordMetres)
    {
        if (chordMetres <= 0.5f)
            return true;
        if (path.Count == 0)
            return false;
        return !IsRoamDetour(RouteLength(path), chordMetres);
    }

    /// <summary>Отсекает roam-цели вне проходимого треугольника (Zones mesh probe).</summary>
    public static bool IsMeshWalkable(WorldTemplate? template, float x, float y)
    {
        if (!NavMeshGround.IsEnabled || template == null)
            return true;
        return new NavMeshGround(template).IsWalkable(x, y);
    }
}