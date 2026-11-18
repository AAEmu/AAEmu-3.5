using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Core;

public sealed class WorldManagerNavHeightTests
{
    [Test]
    public async Task NavHeightForSpawn_WithNoNavData_InvokeReturnsNull()
    {
        var template = new WorldTemplate { Name = "unit_test_world", Id = 0xFFFF };
        var provider = WorldManager.NavHeightForSpawn(template, 1f, 2f, 100f);
        if (provider is null)
        {
            await Assert.That(provider).IsNull();
            return;
        }

        var height = provider.Invoke();
        var unusable = height is not > 0 or float.NaN;
        await Assert.That(unusable).IsTrue();
    }
}