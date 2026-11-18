namespace AAEmu.UnitTests.Game.NPChar;

public sealed class NpcFootingRulesTests
{
    [Test]
    public async Task KeepUnseenFloor_WhenBoundAndTerrainDropsBelow_StaysOnLevel()
    {
        const float level = 50f;
        const float reference = 50f;
        const float ground = 30f;
        await Assert.That(AAEmu.Game.Models.Game.NPChar.NpcFootingRules.KeepUnseenFloor(level, reference, ground)).IsEqualTo(level);
    }

    [Test]
    public async Task KeepUnseenFloor_WhenFarFromLevel_ReturnsNull()
    {
        await Assert.That(AAEmu.Game.Models.Game.NPChar.NpcFootingRules.KeepUnseenFloor(50f, 40f, 30f)).IsNull();
    }

    [Test]
    public async Task IsUnseenFloor_MatchesZoneSemantics()
    {
        await Assert.That(AAEmu.Game.Models.Game.NPChar.NpcFootingRules.IsUnseenFloor(1106.04f, 1037.57f, inWater: false, floorNear: false)).IsTrue();
        await Assert.That(AAEmu.Game.Models.Game.NPChar.NpcFootingRules.IsUnseenFloor(1049.0f, 1037.57f, inWater: false, floorNear: false)).IsFalse();
        await Assert.That(AAEmu.Game.Models.Game.NPChar.NpcFootingRules.IsUnseenFloor(1106.04f, 1037.57f, inWater: false, floorNear: true)).IsFalse();
    }
}