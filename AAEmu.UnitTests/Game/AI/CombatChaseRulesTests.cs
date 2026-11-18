using AAEmu.Game.Models.Game.AI;

namespace AAEmu.UnitTests.Game.AI;

public sealed class CombatChaseRulesTests
{
    [Test]
    public async Task ChaseReplanTiming_MatchesZones()
    {
        await Assert.That(CombatChaseRules.ChaseReplanMs).IsEqualTo(500);
        await Assert.That(CombatChaseRules.ChaseReplanMetres).IsEqualTo(1.5f);
    }

    [Test]
    public async Task ChaseWaypointArrival_AlignsWithReturnRoute()
    {
        await Assert.That(CombatChaseRules.ChaseWaypointArrivalMetres)
            .IsEqualTo(ReturnMovementRules.ReturnWaypointArrivalMetres);
    }
}