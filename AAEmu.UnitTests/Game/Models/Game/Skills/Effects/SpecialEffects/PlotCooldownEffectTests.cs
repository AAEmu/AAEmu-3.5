using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

public class PlotCooldownEffectTests
{
    [Test]
    public async Task AnAuthoredPlotCooldownArmsItsSharedTagInMilliseconds()
    {
        var caster = NewCaster();
        var shield = new Skill(new SkillTemplate { Id = 10399, CooldownTagId = 3296 });
        var alternateShield = new SkillTemplate { Id = 36456, CooldownTagId = 3296 };
        var before = DateTime.UtcNow;

        Apply(caster, shield, 18000, plot: 266, plotEvent: 2880);

        var expiry = caster.Cooldowns.Cooldowns[shield.Id];
        await Assert.That(expiry).IsGreaterThanOrEqualTo(before.AddMilliseconds(18000));
        await Assert.That(expiry).IsLessThanOrEqualTo(DateTime.UtcNow.AddMilliseconds(18000));
        await Assert.That(caster.Cooldowns.CheckSkillCooldown(alternateShield)).IsTrue();
    }

    [Test]
    public async Task CancelledPlotAndPlayersIgnoringCooldownsDoNotArmIndividualOrSharedTimers()
    {
        var caster = NewCaster();
        var shield = new Skill(new SkillTemplate { Id = 10399, CooldownTagId = 3296 }) { Cancelled = true };
        var alternateShield = new SkillTemplate { Id = 36456, CooldownTagId = 3296 };

        Apply(caster, shield, 18000, plot: 266, plotEvent: 2880);
        await Assert.That(caster.Cooldowns.Cooldowns.ContainsKey(shield.Id)).IsFalse();
        await Assert.That(caster.Cooldowns.CheckSkillCooldown(alternateShield)).IsFalse();

        caster.IgnoreSkillCooldowns = true;
        shield.Cancelled = false;
        Apply(caster, shield, 18000, plot: 266, plotEvent: 2880);
        await Assert.That(caster.Cooldowns.Cooldowns.ContainsKey(shield.Id)).IsFalse();
        await Assert.That(caster.Cooldowns.CheckSkillCooldown(alternateShield)).IsFalse();
    }

    [Test]
    public async Task APlotCancellationRequestCannotArmThePendingCooldownEvent()
    {
        var caster = NewCaster();
        var firebolt = new Skill(new SkillTemplate { Id = 10752, CooldownTagId = 3308 });
        var plotState = new PlotState(caster, new SkillCasterUnit(caster.ObjId), caster,
            new SkillCastUnitTarget(caster.ObjId), null, firebolt);
        firebolt.ActivePlotState = plotState;
        plotState.RequestCancellation();

        Apply(caster, firebolt, 1000, plot: 280, plotEvent: 19208);

        await Assert.That(caster.Cooldowns.Cooldowns.ContainsKey(firebolt.Id)).IsFalse();
    }

    [Test]
    public async Task APlotCooldownPublishesTheActiveIndividualExpiryToItsSharedTag()
    {
        var caster = NewCaster();
        var shield = new Skill(new SkillTemplate { Id = 10399, CooldownTagId = 3296 });
        var alternateShield = new SkillTemplate { Id = 36456, CooldownTagId = 3296 };
        var activeExpiry = DateTime.UtcNow.AddMinutes(2);
        caster.Cooldowns.Cooldowns[shield.Id] = activeExpiry;

        Apply(caster, shield, 18000, plot: 266, plotEvent: 2880);

        await Assert.That(caster.Cooldowns.Cooldowns[shield.Id]).IsEqualTo(activeExpiry);
        await Assert.That(caster.Cooldowns.CheckSkillCooldown(alternateShield)).IsTrue();
    }

    [Test]
    public async Task StockZeroAndNegativeDurationsDoNotCreateAPlotTimer()
    {
        var caster = NewCaster();
        var firebolt = new Skill(new SkillTemplate { Id = 10752, CooldownTagId = 3308 });
        var negativeRow = new Skill(new SkillTemplate { Id = 17151 });

        Apply(caster, firebolt, 0, plot: 280, plotEvent: 2346);
        Apply(caster, negativeRow, -69312512, plot: 323, plotEvent: 2469);

        await Assert.That(caster.Cooldowns.Cooldowns.ContainsKey(firebolt.Id)).IsFalse();
        await Assert.That(caster.Cooldowns.Cooldowns.ContainsKey(negativeRow.Id)).IsFalse();
    }

    private static Character NewCaster() => new(new UnitCustomModelParams()) { Id = 1, ObjId = 10 };

    private static void Apply(Character caster, Skill skill, int milliseconds, uint plot, uint plotEvent)
    {
        var effect = new SpecialEffect { SpecialEffectTypeId = SpecialType.Cooldown, Value1 = milliseconds };
        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), caster, new SkillCastUnitTarget(caster.ObjId),
            new CastPlot(plot, 1, plotEvent, skill.Id), new EffectSource(skill), null, DateTime.UtcNow);
    }
}
