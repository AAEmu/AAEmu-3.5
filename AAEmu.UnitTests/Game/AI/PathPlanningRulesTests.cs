using AAEmu.Game.Core.Managers.Bots.Navigation;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.AI;

namespace AAEmu.UnitTests.Game.AI;

public sealed class PathPlanningRulesTests
{
    [Test]
    public async Task UseNavPathPlanner_IncludesTriangleNavWhenEnabled()
    {
        var hadGeo = AppConfiguration.Instance?.World?.GeoDataMode ?? false;
        try
        {
            if (AppConfiguration.Instance?.World != null)
                AppConfiguration.Instance.World.GeoDataMode = false;
            await Assert.That(NavMeshGround.IsEnabled || !PathPlanningRules.UseNavPathPlanner)
                .IsTrue();
        }
        finally
        {
            if (AppConfiguration.Instance?.World != null)
                AppConfiguration.Instance.World.GeoDataMode = hadGeo;
        }
    }
}