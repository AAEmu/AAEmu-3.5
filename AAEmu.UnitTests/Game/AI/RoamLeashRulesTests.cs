using AAEmu.Game.Models.Game.AI;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.UnitTests.Game.AI;

public sealed class RoamLeashRulesTests
{
    [Test]
    public async Task LeashLimit_MatchesZonesReturnHomeBeyondPlusDefaultRoamMax()
    {
        await Assert.That(RoamLeashRules.LeashLimitMetres((Npc?)null))
            .IsEqualTo(RoamTargetRules.DefaultRoamingMaxDist + RoamLeashRules.ReturnHomeBeyondMetres);
    }
}