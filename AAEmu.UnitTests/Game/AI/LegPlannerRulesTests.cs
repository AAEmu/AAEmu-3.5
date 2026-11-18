using System.Numerics;
using AAEmu.Game.Models.Game.AI;

namespace AAEmu.UnitTests.Game.AI;

public class LegPlannerRulesTests
{
    [Test]
    public async Task ForLeg_UsesRoamBudgetOnShortChord()
    {
        var from = new Vector3(0f, 0f, 100f);
        var to = new Vector3(4f, 0f, 100f);
        var roam = LegPlannerRules.ForLeg(from, to);
        var chase = LegPlannerRules.ChaseOptions();
        await Assert.That(roam.MaxSearchNodes).IsLessThan(chase.MaxSearchNodes);
        await Assert.That(roam.Deadline).IsLessThan(chase.Deadline);
    }

    [Test]
    public async Task OffMeshLegAllowed_MatchesZoneTraces()
    {
        await Assert.That(LegPlannerRules.OffMeshLegAllowed(startWalkable: false, unreachable: false, formationWalk: false, flatMetres: 6f))
            .IsTrue();
        await Assert.That(LegPlannerRules.OffMeshLegAllowed(startWalkable: true, unreachable: true, formationWalk: false, flatMetres: 6f))
            .IsTrue();
        await Assert.That(LegPlannerRules.OffMeshLegAllowed(startWalkable: true, unreachable: false, formationWalk: false, flatMetres: 6f))
            .IsFalse();
        await Assert.That(LegPlannerRules.OffMeshLegAllowed(startWalkable: false, unreachable: false, formationWalk: false, flatMetres: 20f))
            .IsFalse();
    }
}