using AAEmu.Game.Models.Game.Items.Procs;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

public class ItemProcTests
{
    [Test]
    public async Task ProcClaimsCooldownOnlyForEligibleSuccessfulRolls()
    {
        var template = new ItemProcTemplate { Id = 1, ChanceRate = 20, CooldownSec = 5, TriggerSkillId = 33 };
        var proc = new ItemProc(template);
        var skill = new Skill(new SkillTemplate { Id = 33 });
        var wrongSkill = new Skill(new SkillTemplate { Id = 34 });
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await Assert.That(proc.TryBegin(now, 0, wrongSkill)).IsFalse();
        await Assert.That(proc.TryBegin(now, 20, skill)).IsFalse();
        await Assert.That(proc.TryBegin(now, 19, skill)).IsTrue();
        proc.Complete(success: true);
        await Assert.That(proc.TryBegin(now.AddSeconds(4), 0, skill)).IsFalse();
        await Assert.That(proc.TryBegin(now.AddSeconds(5), 0, skill)).IsTrue();
    }

    [Test]
    public async Task FailedProcCastAllowsRetryButOverlappingActivationDoesNot()
    {
        var proc = new ItemProc(new ItemProcTemplate { Id = 1, ChanceRate = 100 });
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await Assert.That(proc.TryBegin(now, 99, null)).IsTrue();
        await Assert.That(proc.TryBegin(now, 0, null)).IsFalse();
        proc.Complete(success: false);
        await Assert.That(proc.TryBegin(now, 99, null)).IsTrue();
    }

    [Test]
    public async Task UnequippingDisablesAProcWithoutResettingItsCooldown()
    {
        var proc = new ItemProc(new ItemProcTemplate { Id = 1, ChanceRate = 100, CooldownSec = 10 });
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await Assert.That(proc.TryBegin(now, 0, null)).IsTrue();
        proc.Complete(success: true);
        proc.SetEnabled(false);

        await Assert.That(proc.TryBegin(now.AddSeconds(11), 0, null)).IsFalse();
        proc.SetEnabled(true);
        await Assert.That(proc.TryBegin(now.AddSeconds(9), 0, null)).IsFalse();
        await Assert.That(proc.TryBegin(now.AddSeconds(10), 0, null)).IsTrue();
    }
}
