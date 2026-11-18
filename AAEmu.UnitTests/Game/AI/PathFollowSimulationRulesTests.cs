using AAEmu.Game.Models.Game.AI;

namespace AAEmu.UnitTests.Game.AI;

public sealed class PathFollowSimulationRulesTests
{
    [Test]
    public async Task OpenWorldAiPath_DoesNotMirrorIntoSimulation()
    {
        await Assert.That(PathFollowRules.ShouldMirrorPathInSimulation("AIPath_179_moang_29")).IsFalse();
    }

    [Test]
    public async Task LegacyDiskPath_OnlyMirrorsWhenOptIn()
    {
        await Assert.That(PathFollowRules.ShouldMirrorPathInSimulation("SomeQuestPath"))
            .IsEqualTo(PathFollowRules.LegacyDiskPathEnabled);
    }
}