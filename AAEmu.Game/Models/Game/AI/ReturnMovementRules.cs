namespace AAEmu.Game.Models.Game.AI;

/// <summary>Домой после боя — порт <c>CombatRules.HomeRadiusMetres</c> / <c>ReturnStopsShort</c> (ArcheageZones).</summary>
public static class ReturnMovementRules
{
    /// <summary><c>return_state.lua</c> <c>move_timeoutCount = 20</c>; Zones <see cref="CombatRules.ReturnTimeoutMs"/>.</summary>
    public const long ReturnTimeoutMs = 20_000;

    /// <summary><c>return_state.lua</c> approach: бой заканчивается в ~2 m от spawn.</summary>
    public const float HomeRadiusMetres = 2f;

    /// <summary><c>AAEMU_ZONE_RETURN_STOPS_SHORT=0</c> — идти вплотную к home.</summary>
    public static bool ReturnStopsShort { get; } = Environment.GetEnvironmentVariable("AAEMU_ZONE_RETURN_STOPS_SHORT") != "0";

    /// <summary>
    /// Нет маршрута: straight/timeout вместо мгновенного телепорта (<c>MovementRules.ReturnWithoutRouteEnabled</c>).
    /// </summary>
    public static bool ReturnWithoutRouteEnabled { get; } =
        Environment.GetEnvironmentVariable("AAEMU_ZONE_RETURN_WITHOUT_ROUTE") != "0";

    /// <summary>Повторы планировщика run-home при deadline, прежде чем бежать напрямую.</summary>
    public const int ReturnPlanRetries = 3;

    /// <summary>Прибытие к промежуточной точке A* (как chase waypoint advance).</summary>
    public const float ReturnWaypointArrivalMetres = 0.8f;

    /// <summary>Teleport home на Z spawn (Zones <c>TeleportHomeHeightEnabled</c>).</summary>
    public static bool TeleportHomeHeightEnabled { get; } =
        Environment.GetEnvironmentVariable("AAEMU_ZONE_TELEPORT_HOME_HEIGHT") != "0";

    /// <summary>Return teleport сбрасывает patrol/walk state (Zones <c>TeleportEndsWalkEnabled</c>).</summary>
    public static bool TeleportEndsWalkEnabled { get; } =
        Environment.GetEnvironmentVariable("AAEMU_ZONE_TELEPORT_ENDS_WALK") != "0";

    /// <summary>Аггро, набранное по пути домой, атакуется после return (Zones <c>ReturnKeepsNewHostiles</c>).</summary>
    public static bool ReturnKeepsNewHostiles { get; } =
        Environment.GetEnvironmentVariable("AAEMU_ZONE_RETURN_NEW_HOSTILES") != "0";
}