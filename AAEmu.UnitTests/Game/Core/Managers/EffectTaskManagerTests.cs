using AAEmu.Game.Core.Managers;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class EffectTaskManagerTests
{
    [Test]
    public async Task EffectTaskManager_IsSingleton()
    {
        var instance1 = EffectTaskManager.Instance;
        var instance2 = EffectTaskManager.Instance;
        await Assert.That(instance1).IsSameReferenceAs(instance2);
    }
}
