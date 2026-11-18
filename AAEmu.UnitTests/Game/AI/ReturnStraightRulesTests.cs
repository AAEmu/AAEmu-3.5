using AAEmu.Game.Models.Game.AI;

namespace AAEmu.UnitTests.Game.AI;

public sealed class ReturnStraightRulesTests
{
    [Test]
    public async Task BlinkMaxClimb_MatchesZonesAreaRules()
    {
        await Assert.That(ReturnStraightRules.BlinkMaxClimbDegrees).IsEqualTo(45f);
    }
}