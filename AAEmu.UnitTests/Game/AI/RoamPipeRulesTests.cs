using AAEmu.Game.Models.Game.AI;

namespace AAEmu.UnitTests.Game.AI;

public sealed class RoamPipeRulesTests
{
    [Test]
    public async Task RoamPipeTiming_IsPositiveAndOrdered()
    {
        var (cut, next) = RoamPipeRules.RoamPipeTiming(new Random(1), RoamPipeRules.DefaultAiTickMs, 1f);
        await Assert.That(cut).IsGreaterThan(0);
        await Assert.That(next).IsGreaterThan(cut);
    }

    [Test]
    public async Task RoamTimeoutMs_RespectsClientMinDistFloor()
    {
        var ms = RoamPipeRules.RoamTimeoutMs(new Random(1), 4f);
        await Assert.That(ms).IsGreaterThanOrEqualTo(2_000);
        await Assert.That(ms).IsLessThanOrEqualTo(6_000);
    }

    [Test]
    public async Task AiTickMs_MatchesZoneBuckets()
    {
        await Assert.That(RoamPipeRules.AiTickMs(10f)).IsEqualTo(100);
        await Assert.That(RoamPipeRules.AiTickMs(45f)).IsEqualTo(200);
        await Assert.That(RoamPipeRules.AiTickMs(100f)).IsEqualTo(900);
    }
}