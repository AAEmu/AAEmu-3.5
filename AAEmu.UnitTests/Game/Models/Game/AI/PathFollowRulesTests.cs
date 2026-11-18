using System.Numerics;

using AAEmu.Game.Models.Game.AI;
using AAEmu.Game.Models.Game.AI.v2.Controls;

namespace AAEmu.UnitTests.Game.Models.Game.AI;

public class PathFollowRulesTests
{
    [Test]
    public async Task ParsesZoneKeyFromDesignerPathName()
    {
        await Assert.That(PathFollowRules.TryParseZoneKeyFromPathName("AIPath_129_moang_33", out var z)).IsTrue();
        await Assert.That(z).IsEqualTo(129u);
        await Assert.That(PathFollowRules.TryParseZoneKeyFromPathName("NuiForestkeeperArthur", out _)).IsFalse();
    }

    [Test]
    public async Task DesignerPaths_DoNotMirrorIntoSimulationByDefault()
    {
        await Assert.That(PathFollowRules.ShouldMirrorPathInSimulation("AIPath_179_moang_29")).IsFalse();
        await Assert.That(PathFollowRules.ShouldMirrorPathInSimulation("NuiForestkeeperArthur")).IsFalse();
    }

    [Test]
    public async Task LegacyDiskPath_BlockedForDesignerNamesByDefault()
    {
        await Assert.That(PathFollowRules.LegacyDiskPathEnabled).IsFalse();
        await Assert.That(PathFollowRules.AllowsLegacyDiskPath("AIPath_179_moang_29")).IsFalse();
        await Assert.That(PathFollowRules.AllowsLegacyDiskPath("NuiForestkeeperArthur")).IsFalse();
    }

    [Test]
    public async Task PatrolWaypointArrival_TighterThanSimulationCheckpointAdvance()
    {
        await Assert.That(PathFollowRules.PatrolWaypointArrivalMetres).IsLessThan(PathFollowRules.FollowPathEndAccuracy);
    }

    [Test]
    public async Task TrimLookAheadSkipsPointsInsideRadius()
    {
        var route = new List<AiPathPoint>
        {
            new() { Position = new Vector3(0, 0, 5) },
            new() { Position = new Vector3(5, 0, 5) },
            new() { Position = new Vector3(20, 0, 5) }
        };
        var here = new Vector3(0, 0, 10);
        var trimmed = PathFollowRules.TrimLookAhead(route, here, lookAheadMetres: 10f);
        await Assert.That(trimmed.Count).IsEqualTo(1);
        await Assert.That(trimmed[0].Position).IsEqualTo(new Vector3(20, 0, 10));
    }
}