using AAEmu.Game.Models;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// ArcheageZones <c>SpawnerSystem</c> on a monolithic Game server (no ZWSpawnNpc wire).
/// </summary>
public static class ZoneNpcSpawnRules
{
    /// <summary><c>AAEMU_LEGACY_NPC_SPAWN=1</c> restores json + player-radius <see cref="AAEmu.Game.Core.Managers.World.SpawnManager"/>.</summary>
    public static bool LegacyRadiusSpawnEnabled { get; } =
        Environment.GetEnvironmentVariable("AAEMU_LEGACY_NPC_SPAWN") == "1";

    /// <summary>Zone placements from <c>npc_spawners.g</c>; default on when client spawn is enabled.</summary>
    public static bool AuthoritativeFromClientG { get; } =
        !LegacyRadiusSpawnEnabled && Environment.GetEnvironmentVariable("AAEMU_ZONE_NPC_SPAWN") != "0"
        && AppConfiguration.Instance.World.ClientNpcSpawnEnabled;

    /// <summary>Announcements per tick (native ~17 per 100 ms at peak).</summary>
    public const int SpawnsPerTick = 32;

    public const int TickMs = 100;

    /// <summary>Matches Zones <c>SpawnerSystem.ScheduleCheckIntervalMs</c> (1 s).</summary>
    public const int ScheduleCheckIntervalTicks = 10;

    /// <summary>
    /// Безуспешных попыток подряд, после которых слот перестаёт объявляться каждый тик и уходит на паузу.
    /// Штатное «размещение уже полно» до этого порога не доходит: оно снимается первым же удачным спавном.
    /// </summary>
    public const int StallAttemptsBeforeBackoff = 30;

    /// <summary>Пауза слота, который не дал ни одного юнита (защита от шторма повторов).</summary>
    public const int StallBackoffMs = 1000;

    /// <summary>Не чаще одного предупреждения о застое на размещение в этом интервале.</summary>
    public const int StallWarnIntervalSeconds = 30;

    /// <summary>Мгновенная дозаполнка ниже <c>min_population</c> (Zones <c>MinPopulationRespawns</c>).</summary>
    public static bool MinPopulationEnabled { get; } =
        Environment.GetEnvironmentVariable("AAEMU_ZONE_MIN_POPULATION") != "0";

    /// <summary>Таймер respawn от смерти, не от деспавна трупа (Zones default on).</summary>
    public static bool RespawnFromDeathEnabled { get; } =
        Environment.GetEnvironmentVariable("AAEMU_ZONE_RESPAWN_FROM_DEATH") != "0";
}