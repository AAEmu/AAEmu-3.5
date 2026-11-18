using AAEmu.Game.Models.Game.AI;

namespace AAEmu.UnitTests.Game.AI;

public sealed class RoamTargetRulesTests
{
    [Test]
    public async Task ClientRoamingRange_MatchesZonesDefaults()
    {
        var (min, max) = RoamTargetRules.ClientRoamingRange(null, null);
        await Assert.That(min).IsEqualTo(1f);
        await Assert.That(max).IsEqualTo(6f);
    }

    [Test]
    public async Task ClientRoamingRange_EnforcesMinPlusOne()
    {
        var (min, max) = RoamTargetRules.ClientRoamingRange(3f, 3f);
        await Assert.That(min).IsEqualTo(3f);
        await Assert.That(max).IsEqualTo(4f);
    }

    [Test]
    public async Task NpcRoamingParamsParser_ReadsLuaKeys()
    {
        const string lua = """
            roamingMinDist = 2.5,
            roamingMaxDist = 5.5,
            """;
        var (min, max) = NpcRoamingParamsParser.Parse(lua);
        await Assert.That(min).IsEqualTo(2.5f);
        await Assert.That(max).IsEqualTo(5.5f);
    }

    [Test]
    public async Task ClientRoamingOffset_MatchesZonesFormula()
    {
        var random = new Random(42);
        var offset = RoamTargetRules.ClientRoamingOffset(random, 1f, 6f);
        await Assert.That(MathF.Abs(offset)).IsLessThanOrEqualTo(6f);
    }
}