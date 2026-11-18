namespace AAEmu.Game.Models.Game.AI;

/// <summary>Пол здания по waypoints навблока (порт <c>MovementRules.StructureFloors</c> / <c>TryStructureFloor</c>).</summary>
public static class StructureFloorRules
{
    public const float FlatMetres = 3f;
    public const float RiseMetres = 1f;
    public const float MinLiftMetres = 0.25f;

    public static bool Enabled
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("AAEMU_STRUCTURE_FLOOR")
                ?? Environment.GetEnvironmentVariable("AAEMU_ZONE_STRUCTURE_FLOORS");
            return env is not ("0" or "false" or "False");
        }
    }
}