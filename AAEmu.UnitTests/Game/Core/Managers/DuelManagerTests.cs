using AAEmu.Game.Core.Managers;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class DuelManagerTests
{
    [Test]
    public async Task DuelManager_IsSingleton()
    {
        var instance1 = DuelManager.Instance;
        var instance2 = DuelManager.Instance;
        await Assert.That(instance1).IsSameReferenceAs(instance2);
    }
}
