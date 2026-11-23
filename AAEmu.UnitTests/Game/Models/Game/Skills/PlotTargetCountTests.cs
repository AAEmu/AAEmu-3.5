using AAEmu.Game.Models.Game.Skills;
using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

[NotInParallel]
public class PlotTargetCountTests
{
    [Test]
    public async Task ConditionsAndSetVariableUseTheCurrentEventsRealTargetCount()
    {
        var caster = new Unit { ObjId = 1, Hp = 100 };
        var skill = new Skill(new SkillTemplate { Id = 100 });
        var state = new PlotState(caster, new SkillCasterUnit(caster.ObjId), caster,
            new SkillCastUnitTarget(caster.ObjId), null, skill);
        skill.ActivePlotState = state;
        var info = new PlotTargetInfo(caster, caster);
        info.EffectedTargets.Add(new Unit { ObjId = 2 });
        info.EffectedTargets.Add(new Unit { ObjId = 3 });
        info.EffectedTargets.Add(new BaseUnit { ObjId = uint.MaxValue });
        info.EffectedTargets.Add(null);
        var node = new PlotNode { Event = new PlotEventTemplate() };

        node.CheckConditions(state, info);
        var condition = new PlotCondition { Kind = PlotConditionType.Variable, Param1 = 12, Param2 = 1, Param3 = 2 };
        var result = condition.Check(caster, null, caster, null, null, skill);
        var effect = new SpecialEffect { SpecialEffectTypeId = SpecialType.SetVariable, Value1 = 1, Value3 = 12 };
        effect.Apply(caster, null, caster, null, null, new EffectSource(skill), null, DateTime.UtcNow);

        await Assert.That(result).IsTrue();
        await Assert.That(state.Variables[1]).IsEqualTo(2);
        info.EffectedTargets.Clear();
        node.CheckConditions(state, info);
        await Assert.That(condition.Check(caster, null, caster, null, null, skill)).IsFalse();
    }

    [Test]
    public async Task MissingPlotStateAndInvalidVariableIndexDoNotThrow()
    {
        var caster = new Unit { ObjId = 1 };
        var skill = new Skill(new SkillTemplate { Id = 100 });
        var condition = new PlotCondition { Kind = PlotConditionType.Variable, Param1 = 12, Param2 = 1, Param3 = 0 };

        await Assert.That(condition.Check(caster, null, caster, null, null, skill)).IsFalse();
        var effect = new SpecialEffect { SpecialEffectTypeId = SpecialType.SetVariable, Value1 = -1, Value3 = 11 };
        effect.Apply(caster, null, caster, null, null, new EffectSource(skill), null, DateTime.UtcNow);
        skill.ActivePlotState = new PlotState(caster, null, caster, null, null, skill);
        effect.Apply(caster, null, caster, null, null, new EffectSource(skill), null, DateTime.UtcNow);
        await Assert.That(skill.ActivePlotState.Variables[0]).IsEqualTo(0);
    }

    [Test]
    public async Task AreaConditionChecksZeroHitsInsteadOfTakingTheSuccessBranch()
    {
        var caster = new Unit { ObjId = 1, Hp = 100 };
        var skill = new Skill(new SkillTemplate { Id = 100 });
        var state = new PlotState(caster, null, caster, null, null, skill);
        skill.ActivePlotState = state;
        var targetInfo = new PlotTargetInfo(caster, caster);
        var node = new PlotNode { Event = new PlotEventTemplate() };
        node.Event.Conditions.AddLast(new PlotEventCondition
        {
            SourceId = PlotEffectSource.OriginalSource,
            TargetId = PlotEffectTarget.OriginalSource,
            Condition = new PlotCondition { Kind = PlotConditionType.Variable, Param1 = 12, Param2 = 2, Param3 = 0 }
        });

        await Assert.That(node.CheckConditions(state, targetInfo)).IsFalse();
        targetInfo.EffectedTargets.Add(new Unit { ObjId = 2 });
        await Assert.That(node.CheckConditions(state, targetInfo)).IsTrue();
    }

    [Test]
    public async Task LocationEffectUpdatesVariableWhenAreaHasNoTargets()
    {
        var singleton = typeof(Singleton<SkillManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = singleton.GetValue(null);
        var manager = new SkillManager(null, null);
        typeof(SkillManager).GetField("_effects", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager,
            new Dictionary<string, Dictionary<uint, EffectTemplate>>
            {
                ["SpecialEffect"] = new()
                {
                    [1] = new SpecialEffect { SpecialEffectTypeId = SpecialType.SetVariable, Value1 = 1, Value3 = 12 }
                }
            });
        singleton.SetValue(null, manager);
        try
        {
            var caster = new Unit { ObjId = 1, Hp = 100 };
            var skill = new Skill(new SkillTemplate { Id = 100 });
            var state = new PlotState(caster, new SkillCasterUnit(caster.ObjId), caster,
                new SkillCastUnitTarget(caster.ObjId), null, skill);
            skill.ActivePlotState = state;
            state.Variables[1] = 9;
            var info = new PlotTargetInfo(caster, caster);
            var effect = new PlotEventEffect
            {
                ActualType = "SpecialEffect", ActualId = 1, SourceId = PlotEffectSource.OriginalSource,
                TargetId = PlotEffectTarget.Location
            };
            byte flag = 0;

            effect.ApplyEffect(state, info, new PlotEventTemplate(), ref flag);

            await Assert.That(state.Variables[1]).IsEqualTo(0);
        }
        finally
        {
            singleton.SetValue(null, previous);
        }
    }
}
