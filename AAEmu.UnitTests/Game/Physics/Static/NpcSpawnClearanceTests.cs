using AAEmu.Game.Physics.Static;

namespace AAEmu.UnitTests.Game.Physics.Static;

public sealed class NpcSpawnClearanceTests
{
    [Test]
    public async Task ClearanceMetres_MatchesZonesTerrainFloor()
    {
        await Assert.That(NpcGroundResolver.ClearanceMetres).IsEqualTo(0.4f);
    }
}