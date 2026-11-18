using AAEmu.Game.Models.Game.AI;

namespace AAEmu.UnitTests.Game.AI;

public sealed class ReturnMovementRulesTests
{
    [Test]
    public async Task ReturnWaypointArrival_MatchesChaseAdvanceScale()
    {
        await Assert.That(ReturnMovementRules.ReturnWaypointArrivalMetres).IsEqualTo(0.8f);
        await Assert.That(ReturnMovementRules.ReturnPlanRetries).IsEqualTo(3);
        await Assert.That(ReturnMovementRules.ReturnTimeoutMs).IsEqualTo(20_000);
        await Assert.That(ReturnMovementRules.TeleportHomeHeightEnabled).IsTrue();
        await Assert.That(ReturnMovementRules.TeleportEndsWalkEnabled).IsTrue();
        await Assert.That(ReturnMovementRules.ReturnKeepsNewHostiles).IsTrue();
    }
}