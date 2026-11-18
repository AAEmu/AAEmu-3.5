using AAEmu.Game.Physics.Static;

namespace AAEmu.UnitTests.Game.Physics.Static;

/// <summary>Constants ported from ArcheageZones <c>WalkerCollision</c> stuck-tail rules.</summary>
public sealed class WalkerStuckRulesTests
{
    [Test]
    public async Task StuckTiming_MatchesZoneNative_Traces()
    {
        await Assert.That(WalkerCollision.StuckEndMs).IsEqualTo(1_200);
        await Assert.That(WalkerCollision.StuckSpeed).IsEqualTo(0.15f);
    }
}