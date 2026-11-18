using AAEmu.Game.Models.Game.AI;

namespace AAEmu.UnitTests.Game.AI;

public sealed class RoamMovementRulesTests
{
    [Test]
    public async Task IsRoamDetour_MatchesZoneThresholds()
    {
        await Assert.That(RoamMovementRules.IsRoamDetour(315f, 5.3f)).IsTrue();
        await Assert.That(RoamMovementRules.IsRoamDetour(7f, 5f)).IsFalse();
        await Assert.That(RoamMovementRules.IsRoamDetour(12f, 5f)).IsFalse();
        await Assert.That(RoamMovementRules.IsRoamDetour(14f, 5f)).IsTrue();
    }

    [Test]
    public async Task IsMeshWalkable_NullTemplate_AllowsTarget()
    {
        await Assert.That(RoamMovementRules.IsMeshWalkable(null, 0f, 0f)).IsTrue();
    }

    [Test]
    public async Task RoamTargetAttempts_MatchesZones()
    {
        await Assert.That(RoamMovementRules.RoamTargetAttempts).IsEqualTo(4);
    }

    [Test]
    public async Task RoamArrivalThresholds_AreBelowPatrolLegAccuracy()
    {
        await Assert.That(RoamMovementRules.RoamWaypointArrivalMetres).IsLessThan(PathFollowRules.PatrolWaypointArrivalMetres);
        await Assert.That(RoamMovementRules.RoamGoalArrivalMetres).IsLessThanOrEqualTo(PathFollowRules.PatrolWaypointArrivalMetres);
    }

    [Test]
    public async Task IsAcceptableRoamPath_RejectsEmptyRouteBeyondHalfMetre()
    {
        await Assert.That(RoamMovementRules.IsAcceptableRoamPath([], 2f)).IsFalse();
        await Assert.That(RoamMovementRules.IsAcceptableRoamPath([], 0.3f)).IsTrue();
    }
}