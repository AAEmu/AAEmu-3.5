using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

[NotInParallel]
public sealed class PlotCombatDiceTests
{
    [Test]
    public async Task PreparedHitIsInheritedOnlyByItsOwnBranchAndTarget()
    {
        var caster = new TestUnit { ObjId = 1, Hp = 1000, SpellAccuracy = 0 };
        var target = new TestUnit { ObjId = 2, Hp = 1000 };
        var root = CreateNode(1);
        var roll = Connect(root, 2, false);
        AddEffect(roll, "SpecialEffect", 29302);
        AddConditionBranch(roll, 3, 3, false);
        AddConditionBranch(roll, 4, 3, false, successId: 90001).Event.TargetUpdateMethodId = (uint)PlotTargetUpdateMethodType.OriginalSource;
        AddConditionBranch(root, 5, 3, false, successId: 90002);
        var (tree, state) = CreatePlot(1, caster, target, root, DamageType.Magic);

        using var manager = UseEffects(Damage(DamageType.Magic), 9832,
            new SpecialEffect { SpecialEffectTypeId = SpecialType.CombatDice, Value1 = 4 });
        await tree.ExecuteAsync(state);

        await Assert.That(state.Tickets.ContainsKey(90000)).IsTrue();
        await Assert.That(state.Tickets.ContainsKey(90001)).IsFalse();
        await Assert.That(state.Tickets.ContainsKey(90002)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CriticalOnlyBranchUsesThePreviousDamageResult(bool critical)
    {
        var caster = new TestUnit { ObjId = 1, Hp = 1000, RangedCritical = critical ? 100 : 0 };
        var target = new TestUnit { ObjId = 2, Hp = 1000, MaxHp = 1000 };
        var root = CreateNode(21514);
        AddEffect(root, "DamageEffect", 8904);
        AddConditionBranch(root, 21516, 2, false);
        var (tree, state) = CreatePlot(2633, caster, target, root, DamageType.Ranged);
        // Ordinary skill effects use this table independently of the plot.
        state.ActiveSkill.HitTypes[target.ObjId] = SkillHitType.RangedMiss;

        using var manager = UseEffects(Damage(DamageType.Ranged), 8904);
        await tree.ExecuteAsync(state);

        await Assert.That(target.LastHit).IsEqualTo(critical ? SkillHitType.RangedCritical : SkillHitType.RangedHit);
        await Assert.That(state.Tickets.ContainsKey(90000)).IsEqualTo(critical);
        await Assert.That(state.ActiveSkill.HitTypes[target.ObjId]).IsEqualTo(SkillHitType.RangedMiss);
    }

    [Test]
    [Arguments(4, DamageType.Magic, 24226u, 24227u, 3, false, SkillHitType.SpellHit)]
    [Arguments(5, DamageType.Melee, 5050u, 5048u, 252, true, SkillHitType.MeleeMiss)]
    [Arguments(8, DamageType.Ranged, 24226u, 24227u, 3, false, SkillHitType.RangedMiss)]
    public async Task AuthoredRollControlsDamageAndTheZeroDelayBranch(int kind, DamageType damageType, uint parentId,
        uint childId, int mask, bool negate, SkillHitType expected)
    {
        var caster = new TestUnit { ObjId = 1, Hp = 1000, MeleeAccuracy = 0, SpellAccuracy = 0, RangedAccuracy = 0 };
        var target = new TestUnit { ObjId = 2, Hp = 1000, MaxHp = 1000, DodgeRate = 100, BlockRate = 100 };
        var root = CreateNode(parentId);
        AddEffect(root, "SpecialEffect", 29302);
        AddEffect(root, "DamageEffect", 9832);
        AddConditionBranch(root, childId, mask, negate, perTarget: true);
        var (tree, state) = CreatePlot(2928, caster, target, root, damageType);

        using var manager = UseEffects(Damage(damageType), 9832,
            new SpecialEffect { SpecialEffectTypeId = SpecialType.CombatDice, Value1 = kind });
        await tree.ExecuteAsync(state);

        var hit = kind == 4;
        await Assert.That(hit ? target.LastHit : caster.LastHit).IsEqualTo(expected);
        await Assert.That(target.Hp).IsEqualTo(hit ? 975 : 1000);
        await Assert.That(state.Tickets.ContainsKey(90000)).IsEqualTo(hit);
    }

    [Test]
    [Arguments(SkillHitType.RangedCritical, 2)]
    [Arguments(SkillHitType.MeleeBlock, 5)]
    [Arguments(SkillHitType.SpellResist, 7)]
    [Arguments(SkillHitType.Immune, 8)]
    [Arguments(SkillHitType.Invalid, 1)]
    public async Task DamagePacketWritesItsActualCombatDiceResult(SkillHitType hitType, int result)
    {
        var packet = new SCUnitDamagedPacket(new CastSkill(1, 1), new SkillCasterUnit(2), 2, 3, 10, 0) { HitType = hitType };
        var bytes = packet.Write(new PacketStream()).GetBytes();
        await Assert.That(bytes[^1]).IsEqualTo((byte)result);
    }

    private static (PlotTree, PlotState) CreatePlot(uint id, Unit caster, Unit target, PlotNode root, DamageType type)
    {
        var skill = new Skill(new SkillTemplate { Id = id, DamageTypeId = (uint)type }) { TlId = SkillTlIdManager.GetNextId(caster) };
        return (new PlotTree(id) { RootNode = root }, new PlotState(caster, new SkillCasterUnit(caster.ObjId), target,
            new SkillCastUnitTarget(target.ObjId), null, skill));
    }

    private static DamageEffect Damage(DamageType type) => new()
    {
        DamageType = type, WeaponSlotId = -1, UseFixedDamage = true, FixedMin = 25, FixedMax = 25
    };

    private static PlotNode CreateNode(uint id) => new()
    {
        Event = new PlotEventTemplate
        {
            Id = id,
            SourceUpdateMethodId = (uint)PlotSourceUpdateMethodType.PreviousSource,
            TargetUpdateMethodId = (uint)PlotTargetUpdateMethodType.PreviousTarget
        }
    };

    private static void AddEffect(PlotNode node, string type, uint id) => node.Event.Effects.AddLast(new PlotEventEffect
    {
        ActualType = type, ActualId = id, SourceId = PlotEffectSource.OriginalSource, TargetId = PlotEffectTarget.Target
    });

    private static PlotNode AddConditionBranch(PlotNode root, uint childId, int mask, bool negate, bool perTarget = false,
        uint successId = 90000)
    {
        var condition = Connect(root, childId, perTarget);
        condition.Event.Conditions.AddLast(new PlotEventCondition
        {
            SourceId = PlotEffectSource.OriginalSource, TargetId = PlotEffectTarget.Target,
            Condition = new PlotCondition { Kind = PlotConditionType.CombatDiceResult, Param1 = mask, NotCondition = negate }
        });
        Connect(condition, successId, false);
        return condition;
    }

    private static PlotNode Connect(PlotNode parent, uint id, bool perTarget)
    {
        var child = CreateNode(id);
        child.Parent = parent;
        child.ParentNextEvent = new PlotNextEvent { Event = child.Event, PerTarget = perTarget };
        parent.Children.Add(child);
        parent.Event.NextEvents.AddLast(child.ParentNextEvent);
        return child;
    }

    private static SkillManagerScope UseEffects(DamageEffect damage, uint damageId, SpecialEffect dice = null)
    {
        var manager = new SkillManager(null, null);
        var effects = new Dictionary<string, Dictionary<uint, EffectTemplate>> { ["DamageEffect"] = new() { [damageId] = damage } };
        if (dice != null)
            effects.Add("SpecialEffect", new() { [29302] = dice });
        typeof(SkillManager).GetField("_effects", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, effects);
        return new SkillManagerScope(manager);
    }

    private sealed class SkillManagerScope : IDisposable
    {
        private readonly FieldInfo _field = typeof(Singleton<SkillManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object _previous;

        public SkillManagerScope(SkillManager manager)
        {
            _previous = _field.GetValue(null);
            _field.SetValue(null, manager);
        }

        public void Dispose() => _field.SetValue(null, _previous);
    }

    private sealed class TestUnit : Unit
    {
        public SkillHitType LastHit { get; private set; }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            if (packet is SCUnitDamagedPacket damaged)
                LastHit = damaged.HitType;
        }
    }
}
