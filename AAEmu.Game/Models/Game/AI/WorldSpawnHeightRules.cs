namespace AAEmu.Game.Models.Game.AI;

/// <summary>
/// Высота из <c>WZNpcState</c> (порт <c>MovementRules.WorldSpawnHeight</c>). Используется, когда
/// ArcheageServer World relay появится в AAC3500 — пока только константы и kill-switch.
/// </summary>
public static class WorldSpawnHeightRules
{
    public const float MaxMetres = 1f;

    public static bool Enabled { get; } = Environment.GetEnvironmentVariable("AAEMU_WORLD_SPAWN_HEIGHT") != "0";
}