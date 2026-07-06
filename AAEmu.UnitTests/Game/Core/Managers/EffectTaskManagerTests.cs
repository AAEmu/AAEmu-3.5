using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using GameTask = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class EffectTaskManagerTests
{
    [Test]
    public async Task AddDispelTask_SchedulesDispelTask()
    {
        var mockTaskManager = Mock.Of<ITaskManager>();

        var manager = new EffectTaskManager(mockTaskManager.Object);
        var owner = new BaseUnit();
        var caster = new BaseUnit();
        var buff = new Buff(owner, caster, new SkillCasterUnit(), new BuffTemplate(), new Skill(), DateTime.UtcNow);
        manager.AddDispelTask(buff, 1000);

        await Assert.That(manager).IsNotNull();
        mockTaskManager.Schedule(Any<GameTask>(), TimeSpan.FromMilliseconds(1000), (TimeSpan?)null, -1).WasCalled(Times.Once);
        Mock.VerifyNoOtherCalls(mockTaskManager);
    }
}
