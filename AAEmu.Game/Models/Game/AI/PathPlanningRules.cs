using AAEmu.Game.Core.Managers.Bots.Navigation;
using AAEmu.Game.Models;

namespace AAEmu.Game.Models.Game.AI;

/// <summary>Когда <see cref="PathNode.FindPath"/> / chase / return используют планировщик (geo nodes или triangle nav).</summary>
public static class PathPlanningRules
{
    public static bool UseNavPathPlanner
    {
        get
        {
            var world = AppConfiguration.Instance?.World;
            return world is { GeoDataMode: true } || NavMeshGround.IsEnabled;
        }
    }

    /// <summary>Узловой geodata <c>ReducePath</c>; triangle nav уже сглажен в <see cref="NavPlanner"/>.</summary>
    public static bool UseLegacyGeoPathSimplify =>
        AppConfiguration.Instance?.World is { GeoDataMode: true };
}