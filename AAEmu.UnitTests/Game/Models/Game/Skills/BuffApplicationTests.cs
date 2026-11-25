using System.Reflection;
using System.Collections.Concurrent;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Skills;
using Microsoft.Data.Sqlite;
using GameTask = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

[NotInParallel]
public class BuffApplicationTests
{
    private readonly Dictionary<uint, BuffTemplate> _templates = [];
    private readonly Dictionary<uint, List<BuffTriggerTemplate>> _triggers = [];
    private readonly Dictionary<uint, List<CombatBuffTemplate>> _combatBuffs = [];
    private readonly List<(FieldInfo Field, object Previous)> _singletons = [];
    private TaskManager _tasks;

    [Before(Test)]
    public void Setup()
    {
        var skills = new SkillManager(null, null);
        SetField(skills, "_buffs", _templates);
        SetField(skills, "_buffTags", new Dictionary<uint, List<uint>> { [1] = [10], [2] = [11] });
        SetField(skills, "_taggedBuffs", new Dictionary<uint, List<uint>> { [10] = [1], [11] = [2] });
        SetField(skills, "_skillTags", new Dictionary<uint, List<uint>> { [15] = [50] });
        SetField(skills, "_buffTriggers", _triggers);
        SetField(skills, "_skillModifiers", new Dictionary<uint, List<SkillModifier>>());
        SetField(skills, "_combatBuffs", _combatBuffs);
        Install(skills);

        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE buff_modifiers (owner_type, owner_id, buff_attribute_id, buff_id, synergy, tag_id, unit_modifier_type_id, value);
            CREATE TABLE buff_tolerances (id, buff_tag_id, step_duration, final_step_buff_id, character_time_reduction);
            CREATE TABLE buff_tolerance_steps (id, buff_tolerance_id, hit_chance, time_reduction);
            CREATE TABLE tagged_immune_buffs (buff_id, buff_tag_id);
            CREATE TABLE tagged_require_buffs (buff_id, buff_tag_id);
            CREATE TABLE buff_breakers (id, buff_id, buff_tag_id);
            CREATE TABLE buff_unit_modifiers (id, owner_type, owner_id, buff_id, tag_id);
            CREATE TABLE unit_modifiers (owner_type, owner_id, linear_level_bonus, unit_attribute_id, unit_modifier_type_id, value);
            INSERT INTO buff_unit_modifiers VALUES (1, 'Buff', 9101, 9102, 0), (2, 'Buff', 9103, 0, 11);
            INSERT INTO unit_modifiers VALUES ('BuffUnitModifier', 1, 0, 78, 0, -300), ('BuffUnitModifier', 2, 0, 78, 0, 200);
            INSERT INTO buff_tolerances VALUES (1, 10, 30, 3, 0);
            INSERT INTO buff_tolerance_steps VALUES (9, 1, 100, 0), (7, 1, 100, 0), (8, 1, 100, 25);
            INSERT INTO tagged_immune_buffs VALUES (4, 10), (4, 11), (4, 11);
            INSERT INTO tagged_require_buffs VALUES (5, 11);
            INSERT INTO buff_breakers VALUES (1, 906, 11), (2, 906, 11), (3, 908, 11);
            """;
        command.ExecuteNonQuery();

        var buffData = new BuffGameData();
        buffData.Load(connection, connection);
        buffData.PostLoad();
        Install(buffData);

        _tasks = new TaskManager(null);
        Install(_tasks);
        Install(new EffectTaskManager(_tasks));
    }

    [After(Test)]
    public void TearDown()
    {
        foreach (var (field, previous) in _singletons)
            field.SetValue(null, previous);
    }

    [Test]
    public async Task Tolerance_FinalStepRefusesApplicationAndDoesNotAdvanceWhileImmune()
    {
        var owner = new TestUnit { ObjId = 101 };
        var caster = new TestUnit { ObjId = 102 };
        var crowdControl = Register(new BuffTemplate { Id = 1, Duration = 10000 });
        Register(new BuffTemplate { Id = 3 });

        var first = Add(owner, caster, crowdControl);
        await Assert.That(first.Duration).IsEqualTo(10000);
        owner.Buffs.RemoveBuff(1);

        var second = Add(owner, caster, crowdControl);
        await Assert.That(second.Duration).IsEqualTo(7500);
        owner.Buffs.RemoveBuff(1);

        Add(owner, caster, crowdControl);
        await Assert.That(owner.Buffs.CheckBuff(1)).IsFalse();
        await Assert.That(owner.Buffs.CheckBuff(3)).IsTrue();
        Add(owner, caster, crowdControl);
        await Assert.That(owner.Buffs.CheckBuff(1)).IsFalse();

        owner.Buffs.RemoveBuff(3);
        var next = Add(owner, caster, crowdControl);
        await Assert.That(next.Duration).IsEqualTo(10000);
    }

    [Test]
    public async Task Apply_RespectsEveryImmunityTagOnAnActiveBuff()
    {
        var owner = new TestUnit { ObjId = 101 };
        var caster = new TestUnit { ObjId = 102 };
        var crowdControl = Register(new BuffTemplate { Id = 1 });
        var otherControl = Register(new BuffTemplate { Id = 2 });
        Add(owner, owner, Register(new BuffTemplate { Id = 4 }));

        Apply(owner, caster, crowdControl);
        Apply(owner, caster, otherControl);

        await Assert.That(owner.Buffs.CheckBuff(1)).IsFalse();
        await Assert.That(owner.Buffs.CheckBuff(2)).IsFalse();
    }

    [Test]
    public async Task Apply_ImmunityAllowsItsCreatorAndAnExceptedSkillTag()
    {
        var owner = new TestUnit { ObjId = 101 };
        var creator = new TestUnit { ObjId = 102 };
        var stranger = new TestUnit { ObjId = 103 };
        var immunity = Register(new BuffTemplate { Id = 4, ImmuneExceptCreator = true, ImmuneExceptSkillTagId = 50 });
        var candidate = Register(new BuffTemplate { Id = 2 });
        Add(owner, creator, immunity);

        Apply(owner, stranger, candidate);
        await Assert.That(owner.Buffs.CheckBuff(2)).IsFalse();

        Apply(owner, creator, candidate);
        await Assert.That(owner.Buffs.CheckBuff(2)).IsTrue();
        owner.Buffs.RemoveBuff(2);

        Apply(owner, stranger, candidate, new Skill(new SkillTemplate { Id = 15 }));
        await Assert.That(owner.Buffs.CheckBuff(2)).IsTrue();
    }

    [Test]
    public async Task Apply_RequiresBothSpecificBuffAndTaggedBuff()
    {
        var owner = new TestUnit { ObjId = 101 };
        var caster = new TestUnit { ObjId = 102 };
        var candidate = Register(new BuffTemplate { Id = 5, RequireBuffId = 6 });
        var tagCarrier = Register(new BuffTemplate { Id = 2 });
        var namedPrerequisite = Register(new BuffTemplate { Id = 6 });

        Add(owner, caster, namedPrerequisite);
        Apply(owner, caster, candidate);
        await Assert.That(owner.Buffs.CheckBuff(5)).IsFalse();

        Add(owner, caster, tagCarrier);
        Apply(owner, caster, candidate);
        await Assert.That(owner.Buffs.CheckBuff(5)).IsTrue();
    }

    [Test]
    public async Task Refresh_PermanentBuffKeepsItsIndexWithoutSchedulingExpiry()
    {
        var owner = new TestUnit { ObjId = 101 };
        var caster = new TestUnit { ObjId = 102 };
        var template = Register(new BuffTemplate { Id = 6, StackRule = BuffStackRule.Refresh, MaxStack = 1 });

        var live = Add(owner, caster, template);
        Add(owner, caster, template);

        await Assert.That(owner.Buffs.GetEffectFromBuffId(6)).IsSameReferenceAs(live);
        await Assert.That(_tasks.GetQueueCount()).IsEqualTo(0);
    }

    [Test]
    public async Task Add_ChargeExtendCapsTheCombinedChargeAndPreservesTheTimer()
    {
        var owner = new TestUnit { ObjId = 101 };
        var caster = new TestUnit { ObjId = 102 };
        var template = Register(new BuffTemplate
        {
            Id = 6, StackRule = BuffStackRule.ChargeExtend, MaxStack = 1, Duration = 60000,
            InitMinCharge = 3, InitMaxCharge = 3, MaxCharge = 5
        });

        var live = Add(owner, caster, template, DateTime.UtcNow.AddSeconds(-1));
        var started = live.StartTime;
        Add(owner, caster, template);

        await Assert.That(live.Charge).IsEqualTo(5);
        await Assert.That(live.StartTime).IsEqualTo(started);
        await Assert.That(owner.Buffs.GetBuffCountById(6)).IsEqualTo(1);
        await Assert.That(owner.LastPacket is SCBuffUpdatedPacket).IsTrue();
    }

    [Test]
    public async Task Add_ExtendReusesTheLiveInstanceAndCapsItsDuration()
    {
        var owner = new TestUnit { ObjId = 101 };
        var caster = new TestUnit { ObjId = 102 };
        var template = Register(new BuffTemplate
        {
            Id = 6, StackRule = BuffStackRule.Extend, MaxStack = 1, Duration = 10000, MaxLifeTime = 15000
        });

        var live = Add(owner, caster, template);
        Add(owner, caster, template);

        await Assert.That(live.Duration).IsEqualTo(15000);
        await Assert.That(owner.Buffs.GetBuffCountById(6)).IsEqualTo(1);
        await Assert.That(_tasks.GetQueueCount()).IsEqualTo(1);
    }

    [Test]
    public async Task Add_IndependentBuffsFromDifferentCastersCoexist()
    {
        var owner = new TestUnit { ObjId = 101 };
        var firstCaster = new TestUnit { ObjId = 102 };
        var secondCaster = new TestUnit { ObjId = 103 };
        var template = Register(new BuffTemplate { Id = 6, StackRule = BuffStackRule.Independent, MaxStack = 1 });

        var first = Add(owner, firstCaster, template);
        var second = Add(owner, secondCaster, template);
        Add(owner, secondCaster, template);

        await Assert.That(owner.Buffs.GetBuffCountById(6)).IsEqualTo(2);
        await Assert.That(owner.Buffs.GetEffectByIndex(first.Index)).IsSameReferenceAs(first);
        await Assert.That(owner.Buffs.GetEffectByIndex(second.Index)).IsSameReferenceAs(second);
    }

    [Test]
    public async Task Add_MultipleBuffReachesItsTransformOnTheAuthoredStack()
    {
        var owner = new TestUnit { ObjId = 101 };
        var caster = new TestUnit { ObjId = 102 };
        var otherCaster = new TestUnit { ObjId = 103 };
        var template = Register(new BuffTemplate
        {
            Id = 6, StackRule = BuffStackRule.Multiple, MaxStack = 3, TransformBuffId = 7
        });
        template.Bonuses.Add(new BonusTemplate { Attribute = UnitAttribute.Str, Value = 10 });
        Register(new BuffTemplate { Id = 7 });

        var first = Add(owner, caster, template);
        Add(owner, caster, template);
        await Assert.That(first.Stack).IsEqualTo(2);
        var bonuses = owner.GetBonuses(UnitAttribute.Str);
        await Assert.That(bonuses.Count).IsEqualTo(1);
        await Assert.That(bonuses[0].Value).IsEqualTo(20);

        var other = Add(owner, otherCaster, template);
        Add(owner, caster, template);

        await Assert.That(owner.Buffs.GetEffectByIndex(first.Index)).IsNull();
        await Assert.That(owner.Buffs.GetEffectByIndex(other.Index)).IsSameReferenceAs(other);
        await Assert.That(owner.Buffs.CheckBuff(7)).IsTrue();
        bonuses = owner.GetBonuses(UnitAttribute.Str);
        await Assert.That(bonuses.Count).IsEqualTo(1);
        await Assert.That(bonuses[0].Value).IsEqualTo(10);
    }

    [Test]
    public async Task BuffEffect_AppliesTheAuthoredNumberOfStacks()
    {
        var owner = new TestUnit { ObjId = 101 };
        var caster = new TestUnit { ObjId = 102 };
        var template = Register(new BuffTemplate { Id = 6, StackRule = BuffStackRule.Multiple, MaxStack = 5 });
        var effect = new BuffEffect { Buff = template, Chance = 100, Stack = 3 };

        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), owner, new SkillCastUnitTarget(owner.ObjId),
            null, new EffectSource(), null, DateTime.UtcNow);

        var live = owner.Buffs.GetEffectFromBuffId(6);
        await Assert.That(live).IsNotNull();
        await Assert.That(live.Stack).IsEqualTo(3);
    }

    [Test]
    public async Task Refresh_AlreadyDispatchedExpiryCannotEndTheRefreshedBuff()
    {
        var owner = new TestUnit { ObjId = 101 };
        var caster = new TestUnit { ObjId = 102 };
        var template = Register(new BuffTemplate { Id = 6, StackRule = BuffStackRule.Refresh, Duration = 10000 });

        var live = Add(owner, caster, template, DateTime.UtcNow.AddSeconds(-1));
        var previousExpiry = GetQueuedDispelTask();
        Add(owner, caster, template);
        previousExpiry.Execute();

        await Assert.That(owner.Buffs.GetEffectByIndex(live.Index)).IsSameReferenceAs(live);
        await Assert.That(_tasks.GetQueueCount()).IsEqualTo(1);
    }

    [Test]
    public async Task RemoveBuff_CancelsTheTimerAndRaisesOneDispelWithoutATimeout()
    {
        var owner = new TestUnit { ObjId = 101 };
        var caster = new TestUnit { ObjId = 102 };
        var template = Register(new BuffTemplate { Id = 6, Duration = 10000 });
        var live = Add(owner, caster, template);
        var timeouts = 0;
        var dispels = 0;
        live.Events.OnTimeout += (_, _) => timeouts++;
        live.Events.OnDispelled += (_, _) => dispels++;

        owner.Buffs.RemoveBuff(6);

        await Assert.That(owner.PacketsSent).IsEqualTo(2); // create and remove
        await Assert.That(dispels).IsEqualTo(1);
        await Assert.That(timeouts).IsEqualTo(0);
        await Assert.That(_tasks.GetQueueCount()).IsEqualTo(0);
    }

    [Test]
    public async Task DamagedTrigger_WatchesTheOwnerAndUsesTheAuthoredSourceAndTarget()
    {
        var owner = new TestUnit { ObjId = 101 };
        var caster = new TestUnit { ObjId = 102 };
        var attacker = new TestUnit { ObjId = 103 };
        var template = Register(new BuffTemplate { Id = 6 });
        var effect = new RecordingEffect();
        _triggers[6] =
        [
            new BuffTriggerTemplate
            {
                Kind = BuffEventTriggerKind.Damaged, Effect = effect, SourceAgentId = 3, TargetAgentId = 1,
                UseDamageAmount = true
            }
        ];

        Add(owner, caster, template);
        caster.Events.OnDamaged(this, new OnDamagedArgs { Attacker = attacker, Amount = 135 });
        await Assert.That(effect.Applications).IsEqualTo(0);

        owner.Events.OnDamaged(this, new OnDamagedArgs { Attacker = attacker, Amount = 250 });
        await Assert.That(effect.Applications).IsEqualTo(1);
        await Assert.That(effect.Source).IsSameReferenceAs(caster);
        await Assert.That(effect.Target).IsSameReferenceAs(attacker);
        await Assert.That(effect.Amount).IsEqualTo(250);

        owner.Buffs.RemoveBuff(6);
        owner.Events.OnDamaged(this, new OnDamagedArgs { Attacker = attacker, Amount = 250 });
        await Assert.That(effect.Applications).IsEqualTo(1);
    }

    [Test]
    public async Task ChannelBuffsBelongToTheirAuthoredUnitsAndOnlyTheOwningCastEndsThem()
    {
        var caster = new TestUnit { ObjId = 101, Hp = 100 };
        var target = new TestUnit { ObjId = 102, Hp = 100 };
        var sourceBuff = Register(new BuffTemplate { Id = 399 });
        var targetBuff = Register(new BuffTemplate { Id = 398, StackRule = BuffStackRule.Multiple, MaxStack = 5 });
        var template = new SkillTemplate
        {
            Id = 11068, ChannelingTime = 7000, ChannelingBuffId = sourceBuff.Id, ChannelingTargetBuffId = targetBuff.Id
        };
        var channel = new Skill(template) { InitialTarget = target };
        var casterObject = new SkillCasterUnit(caster.ObjId);
        channel.StartChanneling(caster, casterObject, target, new SkillCastUnitTarget(target.ObjId), new SkillObject());

        await Assert.That(caster.Buffs.CheckBuff(sourceBuff.Id)).IsTrue();
        await Assert.That(target.Buffs.CheckBuff(sourceBuff.Id)).IsFalse();
        await Assert.That(target.Buffs.GetEffectFromBuffId(targetBuff.Id).Skill).IsSameReferenceAs(channel);

        var otherCaster = new TestUnit { ObjId = 103, Hp = 100 };
        var other = new Buff(target, otherCaster, new SkillCasterUnit(otherCaster.ObjId), targetBuff, new Skill(template), DateTime.UtcNow);
        target.Buffs.AddBuff(other);
        caster.InterruptSkills();

        await Assert.That(caster.Buffs.CheckBuff(sourceBuff.Id)).IsFalse();
        await Assert.That(target.Buffs.GetBuffCountById(targetBuff.Id)).IsEqualTo(1);
        await Assert.That(target.Buffs.GetEffectFromBuffId(targetBuff.Id)).IsSameReferenceAs(other);
    }

    [Test]
    public async Task NaturalChannelEndRunsDueBuffTimeoutEvenIfTimerHasNotExecuted()
    {
        var caster = new TestUnit { ObjId = 101, Hp = 100 };
        var target = new TestUnit { ObjId = 102, Hp = 100 };
        var template = Register(new BuffTemplate { Id = 938, Duration = 3000 });
        var skill = new Skill(new SkillTemplate { Id = 12222, ChannelingTargetBuffId = template.Id });
        var casterObject = new SkillCasterUnit(caster.ObjId);
        var buff = new Buff(target, caster, casterObject, template, skill, DateTime.UtcNow.AddSeconds(-4));
        var timeouts = 0;
        var dispels = 0;
        buff.Events.OnTimeout += (_, _) => timeouts++;
        buff.Events.OnDispelled += (_, _) => dispels++;
        target.Buffs.AddBuff(buff);

        skill.EndChanneling(caster, target, null, casterObject);

        await Assert.That(timeouts).IsEqualTo(1);
        await Assert.That(dispels).IsEqualTo(0);
    }

    [Test]
    public async Task StartedEventCanCallOtherUnitsWithoutHoldingTheBuffOwnerLock()
    {
        var owner = new TestUnit { ObjId = 101 };
        var caster = new TestUnit { ObjId = 102 };
        var template = Register(new BuffTemplate { Id = 6 });
        var buff = new Buff(owner, caster, new SkillCasterUnit(caster.ObjId), template, null, DateTime.UtcNow);
        var ownerLockHeld = true;
        buff.Events.OnBuffStarted += (_, _) => ownerLockHeld = Monitor.IsEntered(((Buffs)owner.Buffs).SyncRoot);

        owner.Buffs.AddBuff(buff);

        await Assert.That(ownerLockHeld).IsFalse();
    }

    [Test]
    [Arguments(0u, true)]
    [Arguments(45u, false)]
    [Arguments(46u, true)]
    public async Task ImmunityFamilyExceptionChecksTheActualFamily(uint family, bool blocked)
    {
        var owner = new Character(new UnitCustomModelParams()) { ObjId = 101, Family = 45 };
        var caster = new Character(new UnitCustomModelParams()) { ObjId = 102, Family = family };
        var candidate = Register(new BuffTemplate { Id = 1 });
        var immunity = Register(new BuffTemplate
        {
            Id = 4, ImmuneExceptCreatorRelationCheck = true, ImmuneExceptCreatorRelationId = 8
        });
        owner.Buffs.AddBuff(new Buff(owner, owner, new SkillCasterUnit(owner.ObjId), immunity, null, DateTime.UtcNow));

        var refused = owner.Buffs.CheckBuffImmune(candidate, caster);

        await Assert.That(refused).IsEqualTo(blocked);
    }

    [Test]
    public async Task DelayedStartTriggerIsCancelledWhenTheBuffIsDispelled()
    {
        var owner = new TestUnit { ObjId = 101 };
        var caster = new TestUnit { ObjId = 102 };
        var effect = new RecordingEffect();
        var template = Register(new BuffTemplate { Id = 6 });
        _triggers[6] = [new BuffTriggerTemplate { Kind = BuffEventTriggerKind.Started, DelayTime = 2000, Effect = effect }];

        Add(owner, caster, template);

        await Assert.That(effect.Applications).IsEqualTo(0);
        var delayed = GetQueuedTasks().Single(task => task is not DispelTask);
        owner.Buffs.RemoveBuff(template.Id);
        delayed.Execute();

        await Assert.That(effect.Applications).IsEqualTo(0);
        await Assert.That(_tasks.GetQueueCount()).IsEqualTo(0);
    }

    [Test]
    public async Task DelayedTimeoutRunsAfterRemovalWithTheOriginalSource()
    {
        var owner = new TestUnit { ObjId = 101 };
        var caster = new TestUnit { ObjId = 102 };
        var effect = new RecordingEffect();
        var template = Register(new BuffTemplate { Id = 6, Duration = 3000 });
        _triggers[6] = [new BuffTriggerTemplate
        {
            Kind = BuffEventTriggerKind.Timeout, DelayTime = 2000, SourceAgentId = 3, TargetAgentId = 3, Effect = effect
        }];
        Add(owner, caster, template);

        GetQueuedDispelTask().Execute();

        await Assert.That(owner.Buffs.CheckBuff(template.Id)).IsFalse();
        await Assert.That(effect.Applications).IsEqualTo(0);
        GetQueuedTasks().Single(task => task is not DispelTask).Execute();
        await Assert.That(effect.Applications).IsEqualTo(1);
        await Assert.That(effect.Source).IsSameReferenceAs(caster);
        await Assert.That(effect.Target).IsSameReferenceAs(caster);
    }

    [Test]
    public async Task NegativeTimeOffsetIsMeasuredFromTheBuffEndAndRearmsOnRefresh()
    {
        var owner = new TestUnit { ObjId = 101 };
        var original = new TestUnit { ObjId = 102 };
        var replacement = new TestUnit { ObjId = 103 };
        var effect = new RecordingEffect();
        var template = Register(new BuffTemplate { Id = 6, Duration = 300_000, StackRule = BuffStackRule.Refresh });
        _triggers[6] = [new BuffTriggerTemplate
        {
            Kind = BuffEventTriggerKind.Time, DelayTime = -10000, SourceAgentId = 3, Effect = effect
        }];
        var buff = Add(owner, original, template);
        var obsolete = GetQueuedTasks().SingleOrDefault(task => task is not DispelTask);
        await Assert.That(obsolete).IsNotNull();
        await Assert.That(Math.Abs((buff.EndTime - obsolete!.TriggerTime).TotalMilliseconds - 10000) < 250).IsTrue();

        Add(owner, replacement, template);

        var current = GetQueuedTasks().Single(task => task is not DispelTask);
        await Assert.That(current).IsNotSameReferenceAs(obsolete);
        obsolete.Execute();
        await Assert.That(effect.Applications).IsEqualTo(0);
        current.Execute();
        await Assert.That(effect.Applications).IsEqualTo(1);
        await Assert.That(effect.Source).IsSameReferenceAs(replacement);
    }

    [Test]
    public async Task SourceDeathRemovesOnlyTheApplicationFromTheCurrentCaster()
    {
        var owner = new TestUnit { ObjId = 101 };
        var original = new TestUnit { ObjId = 102 };
        var replacement = new TestUnit { ObjId = 103 };
        var template = Register(new BuffTemplate { Id = 6, Duration = 30000, StackRule = BuffStackRule.Refresh, RemoveOnSourceDead = true });

        Add(owner, original, template);
        Add(owner, replacement, template);
        original.Events.RaiseDeath(original, new OnDeathArgs { Victim = original });
        await Assert.That(owner.Buffs.CheckBuff(template.Id)).IsTrue();

        replacement.Events.RaiseDeath(replacement, new OnDeathArgs { Victim = replacement });

        await Assert.That(owner.Buffs.CheckBuff(template.Id)).IsFalse();
    }

    [Test]
    public async Task DeathEventKeepsConcurrentSubscribers()
    {
        const int subscribers = 128;
        var victim = new TestUnit { ObjId = 101 };
        var calls = 0;

        Parallel.For(0, subscribers, _ =>
        {
            victim.Events.OnDeath += (_, _) => Interlocked.Increment(ref calls);
        });
        victim.Events.RaiseDeath(victim, new OnDeathArgs { Victim = victim });

        await Assert.That(calls).IsEqualTo(subscribers);
    }

    [Test]
    [Arguments(BuffEventTriggerKind.Landing)]
    [Arguments(BuffEventTriggerKind.RemoveOnMove)]
    [Arguments(BuffEventTriggerKind.RemoveOnDamage)]
    public async Task AuthoredEventTriggerListensToTheBuffOwner(BuffEventTriggerKind kind)
    {
        var owner = new TestUnit { ObjId = 101 };
        var caster = new TestUnit { ObjId = 102 };
        var effect = new RecordingEffect();
        var template = Register(new BuffTemplate { Id = 6 });
        _triggers[6] = [new BuffTriggerTemplate { Kind = kind, Effect = effect }];
        Add(owner, caster, template);

        switch (kind)
        {
            case BuffEventTriggerKind.Landing:
                owner.Events.OnLanding(owner, new OnLandingArgs());
                break;
            case BuffEventTriggerKind.RemoveOnMove:
                owner.Events.OnMovement(owner, new OnMovementArgs());
                break;
            case BuffEventTriggerKind.RemoveOnDamage:
                owner.Events.OnDamaged(owner, new OnDamagedArgs { Attacker = caster, Receiver = owner });
                break;
        }

        await Assert.That(effect.Applications).IsEqualTo(1);
        await Assert.That(effect.Target).IsSameReferenceAs(owner);
    }

    [Test]
    public async Task BuffMovementEventsRequireTheSenderOrAnAttachedMateTheyControl()
    {
        var sender = new Character(new UnitCustomModelParams()) { ObjId = 101 };
        var other = new Character(new UnitCustomModelParams()) { ObjId = 102 };
        var mate = new Mate { ObjId = 103, OwnerObjId = other.ObjId };

        await Assert.That(CSMoveUnitPacket.CanRaiseBuffEvents(sender, sender)).IsTrue();
        await Assert.That(CSMoveUnitPacket.CanRaiseBuffEvents(sender, other)).IsFalse();
        await Assert.That(CSMoveUnitPacket.CanRaiseBuffEvents(sender, mate)).IsFalse();
        await Assert.That(CSMoveUnitPacket.CanRaiseBuffEvents(other, mate)).IsTrue();

        mate.Passengers[AttachPointKind.Driver]._objId = sender.ObjId;
        await Assert.That(CSMoveUnitPacket.CanRaiseBuffEvents(sender, mate)).IsTrue();
    }

    [Test]
    public async Task RelativeMovementOnACarrierDoesNotTriggerLandingBuffs()
    {
        var mover = new Unit { ObjId = 101 };
        var carrier = new Unit { ObjId = 102 };
        await Assert.That(CSMoveUnitPacket.IsFreeLanding(mover)).IsTrue();

        mover.Transform.Parent = carrier.Transform;
        await Assert.That(CSMoveUnitPacket.IsFreeLanding(mover)).IsFalse();

        mover.Transform.Parent = null;
        mover.Transform.StickyParent = carrier.Transform;
        await Assert.That(CSMoveUnitPacket.IsFreeLanding(mover)).IsFalse();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AnyEndTriggerRunsExactlyOnceForExpiryOrDispel(bool timesOut)
    {
        var owner = new TestUnit { ObjId = 101 };
        var caster = new TestUnit { ObjId = 102 };
        var template = Register(new BuffTemplate { Id = 6, Duration = 3000 });
        var effect = new RecordingEffect();
        _triggers[6] = [new BuffTriggerTemplate { Kind = BuffEventTriggerKind.Any, Effect = effect }];
        Add(owner, caster, template);
        var expiry = GetQueuedDispelTask();

        if (timesOut)
            expiry.Execute();
        else
            owner.Buffs.RemoveBuff(template.Id);
        expiry.Execute();

        await Assert.That(effect.Applications).IsEqualTo(1);
    }

    [Test]
    [Arguments(BuffEventTriggerKind.Kill)]
    [Arguments(BuffEventTriggerKind.KillAny)]
    public async Task KillTriggerIgnoresTheNotificationWithoutAVictim(BuffEventTriggerKind kind)
    {
        var owner = new TestUnit { ObjId = 101 };
        var other = new TestUnit { ObjId = 102 };
        var template = Register(new BuffTemplate { Id = 6 });
        var effect = new RecordingEffect();
        _triggers[6] = [new BuffTriggerTemplate { Kind = kind, SourceAgentId = 1, TargetAgentId = 2, Effect = effect }];
        Add(owner, owner, template);

        owner.Events.OnKill(owner, new OnKillArgs { Target = owner });
        await Assert.That(effect.Applications).IsEqualTo(0);
        owner.Events.OnKill(owner, new OnKillArgs { Killer = owner, Victim = other });

        await Assert.That(effect.Applications).IsEqualTo(1);
        await Assert.That(effect.Target).IsSameReferenceAs(other);
    }

    [Test]
    public async Task LogoutCancelsPendingTriggersAndSourceDeathWhileKeepingTheBuffForSave()
    {
        var owner = new TestUnit { ObjId = 101 };
        var caster = new TestUnit { ObjId = 102 };
        var template = Register(new BuffTemplate { Id = 6, Duration = 3000, RemoveOnSourceDead = true });
        var effect = new RecordingEffect();
        _triggers[6] = [new BuffTriggerTemplate { Kind = BuffEventTriggerKind.Started, DelayTime = 1000, Effect = effect }];
        Add(owner, caster, template);
        var pending = GetQueuedTasks().Single(task => task is not DispelTask);

        owner.Buffs.CancelAllEffectTasks();
        pending.Execute();
        caster.Events.RaiseDeath(caster, new OnDeathArgs { Victim = caster });

        await Assert.That(effect.Applications).IsEqualTo(0);
        await Assert.That(owner.Buffs.CheckBuff(template.Id)).IsTrue();
        await Assert.That(_tasks.GetQueueCount()).IsEqualTo(0);
    }

    [Test]
    public async Task ManaShieldSpendsManaAtTheAuthoredEfficiencyBeforeRemovingHealth()
    {
        var caster = new TestUnit { ObjId = 101, Hp = 1000 };
        var victim = new TestUnit { ObjId = 102, Hp = 1000, Mp = 40 };
        Add(victim, victim, Register(new BuffTemplate { Id = 609, ManaShieldRatio = 10000 }));
        Add(victim, victim, Register(new BuffTemplate { Id = 610, ManaShieldRatio = 15000 }));

        victim.ReduceCurrentHp(caster, 100, damageType: DamageType.Magic);

        await Assert.That(victim.Hp).IsEqualTo(960);
        await Assert.That(victim.Mp).IsEqualTo(0);
    }

    [Test]
    public async Task ReflectionMatchesTheDamageSchoolAndCannotBounceBetweenTwoShields()
    {
        var attacker = new TestUnit { ObjId = 101, Hp = 1000 };
        var defender = new TestUnit { ObjId = 102, Hp = 1000 };
        var shield = Register(new BuffTemplate
        {
            Id = 609, ReflectionTypeId = 1, ReflectionChance = 100, ReflectionRatio = 25, ReflectionTargetRatio = 50
        });
        Add(attacker, attacker, shield);
        Add(defender, defender, shield);

        defender.ReduceCurrentHp(attacker, 80, damageType: DamageType.Magic);
        await Assert.That(defender.Hp).IsEqualTo(920);
        await Assert.That(attacker.Hp).IsEqualTo(1000);

        defender.ReduceCurrentHp(attacker, 80, damageType: DamageType.Melee);
        await Assert.That(defender.Hp).IsEqualTo(900);
        await Assert.That(attacker.Hp).IsEqualTo(960);
    }

    [Test]
    public async Task NewestLiveTauntWinsAndThenReleasesTheForcedTarget()
    {
        var victim = new TestUnit { ObjId = 101, Hp = 100 };
        var first = new TestUnit { ObjId = 102, Hp = 100 };
        var second = new TestUnit { ObjId = 103, Hp = 100 };
        var buffs = (Buffs)victim.Buffs;
        var plain = Register(new BuffTemplate { Id = 609, Taunt = true });
        var strong = Register(new BuffTemplate { Id = 610, Taunt = true, TauntWithTopAggro = true, ImpossibleChangeTargeting = true });
        var older = Add(victim, first, plain);
        var newer = Add(victim, second, strong);

        await Assert.That(buffs.GetTauntSource()).IsSameReferenceAs(second);
        await Assert.That(buffs.HoldsCurrentTarget()).IsTrue();

        newer.Exit();
        await Assert.That(buffs.GetTauntSource()).IsSameReferenceAs(first);
        await Assert.That(buffs.HoldsCurrentTarget()).IsFalse();

        older.Exit();
        await Assert.That(buffs.GetTauntSource()).IsNull();
    }

    [Test]
    public async Task ThreePointFiveBuffStealTransfersOnlyOneBeneficialEffectWithItsChargesAndStacks()
    {
        var stealer = new TestUnit { ObjId = 101, Hp = 100 };
        var victim = new TestUnit { ObjId = 102, Hp = 100 };
        var benefit = Register(new BuffTemplate { Id = 609, Duration = 12000, Kind = BuffKind.Good, StackRule = BuffStackRule.Independent, MaxStack = 4 });
        var other = Register(new BuffTemplate { Id = 610, Kind = BuffKind.Good });
        var one = Add(victim, victim, benefit);
        Add(victim, victim, benefit);
        one.Charge = 7;
        Add(victim, victim, other);

        ApplySpecial(SpecialType.BuffSteal, stealer, victim, 1, 0, 0);

        var received = stealer.Buffs.GetEffectFromBuffId(609);
        await Assert.That(received).IsNotNull();
        await Assert.That(received!.Stack).IsEqualTo(2);
        await Assert.That(received.Charge).IsEqualTo(7);
        await Assert.That(received.Caster).IsSameReferenceAs(stealer);
        await Assert.That(victim.Buffs.CheckBuff(609)).IsFalse();
        await Assert.That(victim.Buffs.CheckBuff(610)).IsTrue();
    }

    [Test]
    public async Task ExplodingBeneficialBuffsDealsDamageOnlyForEffectsActuallyRemoved()
    {
        var caster = new TestUnit { ObjId = 101, Hp = 1000 };
        var victim = new TestUnit { ObjId = 102, Hp = 1000 };
        Add(victim, victim, Register(new BuffTemplate { Id = 609, Kind = BuffKind.Good }));
        Add(victim, victim, Register(new BuffTemplate { Id = 610, Kind = BuffKind.Good, System = true }));
        Add(victim, caster, Register(new BuffTemplate { Id = 611, Kind = BuffKind.Bad }));

        ApplySpecial(SpecialType.ExplodeBuff, caster, victim, 100, 0, 0);
        ApplySpecial(SpecialType.ExplodeBuff, caster, victim, 100, 0, 0);

        await Assert.That(victim.Hp).IsEqualTo(900);
        await Assert.That(victim.Buffs.CheckBuff(609)).IsFalse();
        await Assert.That(victim.Buffs.CheckBuff(610)).IsTrue();
        await Assert.That(victim.Buffs.CheckBuff(611)).IsTrue();
    }

    [Test]
    public async Task NpcInnerShockBurnsOneBuffAndRespectsMagicResistance()
    {
        var caster = new TestUnit { ObjId = 101, Hp = 5000 };
        var victim = new TestUnit { ObjId = 102, Hp = 5000, MagicResistance = 5300 };
        Add(victim, victim, Register(new BuffTemplate { Id = 609, Kind = BuffKind.Good }));
        Add(victim, victim, Register(new BuffTemplate { Id = 610, Kind = BuffKind.Good }));

        ApplySpecial(SpecialType.ExplodeBuff, caster, victim, 1000, 20, 20);

        await Assert.That(victim.Buffs.CheckBuff(609) ^ victim.Buffs.CheckBuff(610)).IsTrue();
        await Assert.That(victim.Hp).IsGreaterThanOrEqualTo(4400);
        await Assert.That(victim.Hp).IsLessThanOrEqualTo(4600);
    }

    [Test]
    public async Task InnerShockCannotDealDamageThroughRemainingSpellImmunity()
    {
        var caster = new TestUnit { ObjId = 101, Hp = 5000 };
        var victim = new TestUnit { ObjId = 102, Hp = 5000 };
        Add(victim, victim, Register(new BuffTemplate { Id = 609, Kind = BuffKind.Good }));
        Add(victim, victim, Register(new BuffTemplate { Id = 610, Kind = BuffKind.Good, System = true, SpellImmune = true }));

        ApplySpecial(SpecialType.ExplodeBuff, caster, victim, 1000, 20, 20);

        await Assert.That(victim.Hp).IsEqualTo(5000);
        await Assert.That(victim.Buffs.CheckBuff(609)).IsFalse();
        await Assert.That(victim.Buffs.CheckBuff(610)).IsTrue();
        await Assert.That(victim.LastPacket).IsTypeOf<SCUnitDamagedPacket>();
    }

    [Test]
    public async Task PlayerInnerShockTimeoutUsesTheOriginalCasterForSpellDamage()
    {
        var caster = new TestUnit { ObjId = 101, Hp = 5000 };
        var victim = new TestUnit { ObjId = 102, Hp = 5000 };
        Unit attacker = null;
        victim.Events.OnDamagedSpell += (_, damage) => attacker = damage.Attacker;
        var innerShock = Register(new BuffTemplate { Id = 449, Kind = BuffKind.Bad, Duration = 1500 });
        _triggers[449] =
        [
            new BuffTriggerTemplate
            {
                Id = 2194,
                Kind = BuffEventTriggerKind.Timeout,
                SourceAgentId = 3,
                TargetAgentId = 0,
                Effect = new SpecialEffect
                {
                    Id = 6411, SpecialEffectTypeId = SpecialType.ExplodeBuff, Value1 = 2000, Value2 = 50, Value3 = 50
                }
            }
        ];
        Add(victim, victim, Register(new BuffTemplate { Id = 609, Kind = BuffKind.Good }));
        Add(victim, caster, innerShock);

        GetQueuedDispelTask().Execute();

        await Assert.That(attacker).IsSameReferenceAs(caster);
        await Assert.That(victim.Hp).IsGreaterThanOrEqualTo(2000);
        await Assert.That(victim.Hp).IsLessThanOrEqualTo(4000);
        await Assert.That(victim.Buffs.CheckBuff(609)).IsFalse();
        await Assert.That(victim.Buffs.CheckBuff(449)).IsFalse();
    }

    [Test]
    public async Task ConcurrentInnerShocksCannotBothBurnTheSameBuff()
    {
        var first = new TestUnit { ObjId = 101, Hp = 5000 };
        var second = new TestUnit { ObjId = 102, Hp = 5000 };
        var victim = new TestUnit { ObjId = 103, Hp = 5000 };
        var spellHits = 0;
        victim.Events.OnDamagedSpell += (_, _) => Interlocked.Increment(ref spellHits);
        Add(victim, victim, Register(new BuffTemplate { Id = 609, Kind = BuffKind.Good }));

        Parallel.Invoke(() => ApplySpecial(SpecialType.ExplodeBuff, first, victim, 1000, 20, 20),
                        () => ApplySpecial(SpecialType.ExplodeBuff, second, victim, 1000, 20, 20));

        await Assert.That(spellHits).IsEqualTo(1);
        await Assert.That(victim.Hp).IsGreaterThanOrEqualTo(3800);
        await Assert.That(victim.Hp).IsLessThanOrEqualTo(4200);
        await Assert.That(victim.Buffs.CheckBuff(609)).IsFalse();
    }

    [Test]
    public async Task FakeDeathRemovesOnlyTheAffectedUnitsThreat()
    {
        var affected = new TestUnit { ObjId = 101 };
        var other = new TestUnit { ObjId = 102 };
        var world = new WorldInstance(new WorldTemplate(), 0, true, affected.Transform.InstanceId);
        affected.ParentWorld = world;
        var npc = new Npc { ObjId = 103, Template = new NpcTemplate(), ParentWorld = world, IsInBattle = true };
        world.AddObject(npc);
        npc.AggroTable.TryAdd(affected.ObjId, new Aggro(affected));
        npc.AggroTable.TryAdd(other.ObjId, new Aggro(other));

        ApplySpecial(SpecialType.FakeDeath, affected, affected, 700, 70, 0);

        await Assert.That(npc.AggroTable.ContainsKey(affected.ObjId)).IsFalse();
        await Assert.That(npc.AggroTable.ContainsKey(other.ObjId)).IsTrue();
        await Assert.That(npc.IsInBattle).IsTrue();
    }

    [Test]
    public async Task LoseTargetingClearsObserversOnlyWhileATargetingRestrictionApplies()
    {
        var affected = new TestUnit { ObjId = 101, Hp = 100 };
        var observer = new TestUnit { ObjId = 102, Hp = 100 };
        var world = new WorldInstance(new WorldTemplate(), 0, true, affected.Transform.InstanceId);
        affected.ParentWorld = world;
        observer.ParentWorld = world;
        world.AddObject(observer);
        observer.CurrentTarget = affected;
        affected.CurrentTarget = observer;

        ApplySpecial(SpecialType.LoseTargetingTheTarget, affected, affected, 0, 0, 0);
        await Assert.That(affected.CurrentTarget).IsNull();
        await Assert.That(observer.CurrentTarget).IsSameReferenceAs(affected);

        Add(affected, affected, Register(new BuffTemplate { Id = 609, ImpossibleTargeting = true, TargetingRelationId = 0 }));
        await Assert.That(SkillTargetingUtil.IsTargetRestricted(observer, affected)).IsTrue();
        await Assert.That(SkillTargetingUtil.IsTargetRestricted(affected, affected)).IsFalse();

        ApplySpecial(SpecialType.LoseTargetingTheTarget, affected, affected, 0, 0, 0);
        await Assert.That(observer.CurrentTarget).IsNull();
    }

    [Test]
    public async Task CombatBuffUsesTheAuthoredSideAndOnlyRequiresOneLiveCopy()
    {
        var attacker = new TestUnit { ObjId = 101 };
        var defender = new TestUnit { ObjId = 102 };
        var other = new TestUnit { ObjId = 103 };
        var condition = Register(new BuffTemplate { Id = 601, StackRule = BuffStackRule.Independent });
        Register(new BuffTemplate { Id = 602 });
        _combatBuffs[601] = [new CombatBuffTemplate
        {
            ReqBuffId = 601, BuffId = 602, HitType = SkillHitType.MeleeCritical, BuffFromSource = true, BuffToSource = false
        }];
        var first = Add(defender, attacker, condition);
        var second = Add(defender, other, condition);
        first.Exit();

        defender.CombatBuffs.TriggerCombatBuffs(defender, attacker, SkillHitType.MeleeCritical, false);
        defender.CombatBuffs.TriggerCombatBuffs(attacker, defender, SkillHitType.MeleeHit, false);
        await Assert.That(defender.Buffs.CheckBuff(602)).IsFalse();

        defender.CombatBuffs.TriggerCombatBuffs(attacker, defender, SkillHitType.MeleeCritical, false);
        var granted = defender.Buffs.GetEffectFromBuffId(602);
        await Assert.That(granted).IsNotNull();
        await Assert.That(granted!.Caster).IsSameReferenceAs(defender);

        granted.Exit();
        second.Exit();
        defender.CombatBuffs.TriggerCombatBuffs(attacker, defender, SkillHitType.MeleeCritical, false);
        await Assert.That(defender.Buffs.CheckBuff(602)).IsFalse();
    }

    [Test]
    public async Task StackDispelTakesOnlyTheAuthoredNumberOfApplications()
    {
        var owner = new TestUnit { ObjId = 101 };
        var template = Register(new BuffTemplate { Id = 2, StackRule = BuffStackRule.Independent, MaxStack = 5 });
        var buff = Add(owner, owner, template);
        Add(owner, owner, template);
        Add(owner, owner, template);
        var effect = new DispelEffect { BuffTagId = 11, Stack = 1 };

        effect.Apply(owner, null, owner, null, null, null, null, DateTime.UtcNow);

        await Assert.That(buff.Stack).IsEqualTo(2);
        await Assert.That(buff.InUse).IsTrue();

        effect.Stack = 2;
        effect.Apply(owner, null, owner, null, null, null, null, DateTime.UtcNow);

        await Assert.That(owner.Buffs.CheckBuff(2)).IsFalse();
    }

    [Test]
    public async Task CollisionDamageTriggerUsesTheOwnerAndActualDamage()
    {
        var owner = new TestUnit { ObjId = 101 };
        var other = new TestUnit { ObjId = 102 };
        var template = Register(new BuffTemplate { Id = 6 });
        var effect = new RecordingEffect();
        _triggers[6] = [new BuffTriggerTemplate
        {
            Kind = BuffEventTriggerKind.DamagedCollision, SourceAgentId = 1, TargetAgentId = 2,
            UseDamageAmount = true, Effect = effect
        }];
        Add(owner, other, template);

        other.Events.OnDamagedCollision(other, new OnDamagedCollisionArgs { Receiver = other, Amount = 14 });
        await Assert.That(effect.Applications).IsEqualTo(0);
        owner.Events.OnDamagedCollision(owner, new OnDamagedCollisionArgs { Attacker = other, Receiver = owner, Amount = 37 });
        await Assert.That(effect.Applications).IsEqualTo(1);
        await Assert.That(effect.Amount).IsEqualTo(37);
        await Assert.That(effect.Source).IsSameReferenceAs(other);
        await Assert.That(effect.Target).IsSameReferenceAs(owner);
    }

    [Test]
    public async Task BreakerRemovesEveryInstanceAndFiresOncePerVictimAfterLanding()
    {
        var owner = new TestUnit { ObjId = 101 };
        var caster = new TestUnit { ObjId = 102 };
        var victim = Register(new BuffTemplate { Id = 906, StackRule = BuffStackRule.Independent });
        var ordinary = Register(new BuffTemplate { Id = 7 });
        var otherVictim = Register(new BuffTemplate { Id = 908 });
        var breaker = Register(new BuffTemplate { Id = 2 });
        var effect = new RecordingEffect { OnApply = () => owner.Buffs.CheckBuff(2) && !owner.Buffs.CheckBuff(906) };
        _triggers[906] = [new BuffTriggerTemplate
        {
            Kind = BuffEventTriggerKind.Breaker, SourceAgentId = 1, TargetAgentId = 2, Effect = effect
        }];
        Add(owner, owner, victim);
        Add(owner, caster, victim);
        Add(owner, owner, ordinary);
        Add(owner, owner, otherVictim);

        Add(owner, caster, breaker);

        await Assert.That(effect.Applications).IsEqualTo(2);
        await Assert.That(effect.AcceptedApplications).IsEqualTo(2);
        await Assert.That(effect.Source).IsSameReferenceAs(caster);
        await Assert.That(effect.Target).IsSameReferenceAs(owner);
        await Assert.That(owner.Buffs.CheckBuff(7)).IsTrue();
        await Assert.That(owner.Buffs.CheckBuff(908)).IsFalse();
    }

    [Test]
    public async Task OrdinaryDispelDoesNotRunBreakerTrigger()
    {
        var owner = new TestUnit { ObjId = 101 };
        var template = Register(new BuffTemplate { Id = 6 });
        var effect = new RecordingEffect();
        _triggers[6] = [new BuffTriggerTemplate { Kind = BuffEventTriggerKind.Breaker, Effect = effect }];
        Add(owner, owner, template);

        owner.Buffs.RemoveBuff(6);

        await Assert.That(effect.Applications).IsEqualTo(0);
    }

    [Test]
    public async Task OneTimeImmortalityOnlyFiresOnFatalDamageOfAnEnabledType()
    {
        var owner = new TestUnit { ObjId = 101, Hp = 100 };
        var caster = new TestUnit { ObjId = 102 };
        var attacker = new TestUnit { ObjId = 103 };
        var template = Register(new BuffTemplate { Id = 6, OneTimeImmortality = true, MeleeImmortality = true });
        var effect = new RecordingEffect { OnApply = () => owner.Hp == 1 && !owner.Buffs.CheckBuff(6) };
        _triggers[6] = [new BuffTriggerTemplate
        {
            Kind = BuffEventTriggerKind.Immortality, SourceAgentId = 3, TargetAgentId = 2, Effect = effect
        }];
        Add(owner, caster, template);
        owner.ReduceCurrentHp(attacker, 20, damageType: DamageType.Melee);

        await Assert.That(owner.Hp).IsEqualTo(80);
        await Assert.That(effect.Applications).IsEqualTo(0);

        owner.ReduceCurrentHp(attacker, 80, damageType: DamageType.Melee);

        await Assert.That(owner.Hp).IsEqualTo(1);
        await Assert.That(effect.AcceptedApplications).IsEqualTo(1);
        await Assert.That(effect.Source).IsSameReferenceAs(caster);
        await Assert.That(effect.Target).IsSameReferenceAs(owner);
    }

    [Test]
    public async Task FallImmortalitySurvivesRepeatedLethalFalls()
    {
        var owner = new TestUnit { ObjId = 101, Hp = 100, MaxHp = 100 };
        var template = Register(new BuffTemplate { Id = 6, FallDamageImmortality = true });
        var effect = new RecordingEffect();
        _triggers[6] = [new BuffTriggerTemplate { Kind = BuffEventTriggerKind.Immortality, Effect = effect }];
        Add(owner, owner, template);

        owner.DoFallDamage(32000);
        owner.DoFallDamage(32000);

        await Assert.That(owner.Hp).IsEqualTo(1);
        await Assert.That(owner.Buffs.CheckBuff(6)).IsTrue();
        await Assert.That(effect.Applications).IsEqualTo(2);
    }

    [Test]
    public async Task ResumeAfterLogoutReleasesTheOldBuffAndAllowsItToBeRestored()
    {
        var owner = new TestUnit { ObjId = 101 };
        var template = Register(new BuffTemplate { Id = 96, Duration = 80000 });
        var previousBuff = Add(owner, owner, template);
        var collection = (Buffs)owner.Buffs;
        collection.CancelAllEffectTasks();

        collection.ResumeAfterLogout();
        var currentBuff = Add(owner, owner, template);

        await Assert.That(previousBuff.IsEnded()).IsTrue();
        await Assert.That(collection.GetEffectFromBuffId(96)).IsSameReferenceAs(currentBuff);
    }

    [Test]
    public async Task RestoredBuffSubscribesToItsCasterDeathWithoutReplayingStartEffects()
    {
        var owner = new TestCharacter { Id = 3, ObjId = 101, Hp = 100 };
        var caster = new TestCharacter { Id = 7, ObjId = 102, Hp = 100 };
        var template = Register(new BuffTemplate
        {
            Id = 97,
            SaveRuleId = (uint)BuffSaveRuleType.CharacterPersistent,
            Duration = 80000,
            RemoveOnSourceDead = true
        });
        var started = new RecordingEffect();
        _triggers[template.Id] = [new BuffTriggerTemplate { Kind = BuffEventTriggerKind.Started, Effect = started }];
        var now = DateTime.UtcNow;
        var original = new Buff(owner, caster, new SkillCasterUnit(caster.ObjId), template, null, now)
        {
            State = EffectState.Acting,
            Duration = template.Duration
        };
        var row = PersistedBuff.Capture(original, owner.Id, now)!.Value;

        owner.Buffs.AddBuff(row.CreateBuff(owner, template, null, _ => caster, now));
        await Assert.That(owner.Buffs.CheckBuff(template.Id)).IsTrue();
        await Assert.That(started.Applications).IsEqualTo(0);

        caster.Events.RaiseDeath(caster, new OnDeathArgs { Victim = caster });

        await Assert.That(owner.Buffs.CheckBuff(template.Id)).IsFalse();
    }

    [Test]
    public async Task CancelOngoingBuffRemovesTheSelectedTagAndBuffFromTarget()
    {
        var caster = new TestUnit { ObjId = 101 };
        var target = new TestUnit { ObjId = 102 };
        var tagged = Register(new BuffTemplate { Id = 1 });
        var specific = Register(new BuffTemplate { Id = 2 });
        Add(target, caster, tagged);
        Add(target, caster, specific);
        Add(caster, caster, tagged);

        ApplySpecial(SpecialType.CancelOngoingBuff, caster, target, 10, 2, 0);

        await Assert.That(target.Buffs.CheckBuff(1)).IsFalse();
        await Assert.That(target.Buffs.CheckBuff(2)).IsFalse();
        await Assert.That(caster.Buffs.CheckBuff(1)).IsTrue();
    }

    [Test]
    public async Task CancelOngoingBuffIgnoresUnknownOrNegativeIds()
    {
        var owner = new TestUnit { ObjId = 101 };
        var template = Register(new BuffTemplate { Id = 1 });
        Add(owner, owner, template);

        ApplySpecial(SpecialType.CancelOngoingBuff, owner, owner, 999, -1, 0);
        ApplySpecial(SpecialType.CancelOngoingBuff, owner, owner, -1, -1, 0);

        await Assert.That(owner.Buffs.CheckBuff(1)).IsTrue();
    }

    private GameTask[] GetQueuedTasks()
    {
        var field = typeof(TaskManager).GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var queue = (ConcurrentDictionary<uint, GameTask>)field.GetValue(_tasks)!;
        return queue.Values.ToArray();
    }

    private DispelTask GetQueuedDispelTask() => (DispelTask)GetQueuedTasks().Single(task => task is DispelTask);

    [Test]
    [Arguments(9101u, 9102u, 0.7f)]
    [Arguments(9103u, 2u, 1.2f)]
    public async Task ConditionalUnitModifierTracksBothBuffs(uint providerId, uint requiredId, float expected)
    {
        var owner = new TestUnit { ObjId = 1 };
        var provider = Register(new BuffTemplate { Id = providerId });
        var required = Register(new BuffTemplate { Id = requiredId });
        var providingBuff = Add(owner, owner, provider);
        await Assert.That(owner.MeleeAntiMissMul).IsEqualTo(1f);

        var requiredBuff = Add(owner, owner, required);
        await Assert.That(owner.MeleeAntiMissMul).IsEqualTo(expected);
        requiredBuff.Exit();
        await Assert.That(owner.MeleeAntiMissMul).IsEqualTo(1f);

        Add(owner, owner, required);
        await Assert.That(owner.MeleeAntiMissMul).IsEqualTo(expected);
        providingBuff.Exit();
        await Assert.That(owner.MeleeAntiMissMul).IsEqualTo(1f);
    }

    [Test]
    [Arguments(0, false, 25, 7500)]
    [Arguments(4, false, 4, 5400)]
    [Arguments(4, true, 1, 5100)]
    public async Task BuffUsesEffectiveSkillRankOrItsExplicitLevel(int explicitLevel, bool fixedToOne, int level, int duration)
    {
        var caster = new TestUnit { ObjId = 1 };
        var target = new TestUnit { ObjId = 2 };
        var template = Register(new BuffTemplate { Id = 9201, Duration = 5000, LevelDuration = 100, FixAbilityLevelToOne = fixedToOne });
        var skill = new Skill(new SkillTemplate { Id = 12026, AbilityLevel = 20, LevelStep = 5 });
        caster.SkillModifiersCache.AddModifier(new SkillModifier
        {
            SkillId = skill.Id, SkillAttribute = SkillAttribute.SkillLevel,
            UnitModifierType = UnitModifierType.Value, Value = 1
        });
        var effect = new BuffEffect { Buff = template, Chance = 100, AbLevel = explicitLevel };

        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), target, new SkillCastUnitTarget(target.ObjId),
            new CastSkill(skill.Id, 1), new EffectSource(skill), null, DateTime.UtcNow);

        var applied = target.Buffs.GetEffectFromBuffId(template.Id);
        await Assert.That(applied).IsNotNull();
        await Assert.That(applied!.AbLevel).IsEqualTo((ushort)level);
        await Assert.That(applied.Duration).IsEqualTo(duration);
        await Assert.That(skill.Level).IsEqualTo((byte)1);
    }

    [Test]
    public async Task NpcLevelScalesNegativeBaseDurationIntoAFiniteFear()
    {
        var caster = new Npc { ObjId = 1, Level = 35 };
        var target = new TestUnit { ObjId = 2 };
        var template = Register(new BuffTemplate { Id = 5738, Duration = -1200, LevelDuration = 100 });
        var skill = new Skill(new SkillTemplate { Id = 1, AbilityLevel = 1, LevelStep = 5 });
        var effect = new BuffEffect { Buff = template, Chance = 100 };

        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), target, new SkillCastUnitTarget(target.ObjId),
            new CastSkill(skill.Id, 1), new EffectSource(skill), null, DateTime.UtcNow);

        var applied = target.Buffs.GetEffectFromBuffId(template.Id);
        await Assert.That(applied).IsNotNull();
        await Assert.That(applied!.Duration).IsEqualTo(2300);
    }

    [Test]
    public async Task BuffChanceModifierCanPreventAnOtherwiseGuaranteedApplication()
    {
        var caster = new TestUnit { ObjId = 1 };
        var target = new TestUnit { ObjId = 2 };
        var template = Register(new BuffTemplate { Id = 173 });
        var effect = new BuffEffect { Buff = template, Chance = 100 };
        caster.BuffModifiersCache.AddModifier(new BuffModifier
        {
            BuffId = template.Id, BuffAttribute = BuffAttribute.Chance,
            UnitModifierType = UnitModifierType.Percent, Value = -100
        });

        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), target, new SkillCastUnitTarget(target.ObjId),
            new CastSkill(1, 1), new EffectSource(), null, DateTime.UtcNow);

        await Assert.That(target.Buffs.CheckBuff(template.Id)).IsFalse();
        await Assert.That(caster.ConditionChance).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OnlyTheCastersOwnMarkSatisfiesSourceScopedSkillTags(bool blockingTag)
    {
        var manager = SkillManager.Instance;
        SetField(manager, "_skillReagents", new Dictionary<uint, SkillReagent>());
        SetField(manager, "_skillProducts", new Dictionary<uint, SkillProduct>());
        var caster = new TestUnit { ObjId = 1 };
        var other = new TestUnit { ObjId = 2 };
        var target = new TestUnit { ObjId = 3 };
        var mark = Register(new BuffTemplate { Id = 2 }); // Tag 11.
        var otherMark = Add(target, other, mark);
        var recorded = new RecordingEffect();
        var skill = new Skill(new SkillTemplate { Id = 35717, TargetType = SkillTargetType.AnyUnit });
        skill.Template.Effects.Add(new SkillEffect
        {
            ApplicationMethod = SkillEffectApplicationMethod.Target, EndLevel = 99, Chance = 100, Template = recorded,
            TargetBuffTagId = blockingTag ? 0u : 11u, CheckTargetTagSrc = !blockingTag,
            TargetNoBuffTagId = blockingTag ? 11u : 0u, CheckNoTargetTagSrc = blockingTag
        });
        var casterObject = new SkillCasterUnit(caster.ObjId);
        var targetObject = new SkillCastUnitTarget(target.ObjId);

        skill.ApplyEffects(caster, casterObject, target, targetObject, null);
        await Assert.That(recorded.Applications).IsEqualTo(blockingTag ? 1 : 0);
        otherMark.Exit();
        Add(target, caster, mark);
        skill.ApplyEffects(caster, casterObject, target, targetObject, null);
        await Assert.That(recorded.Applications).IsEqualTo(1);
    }

    [Test]
    public async Task APeriodicEffectOnlyRejectsItsCastSourcesOwnBlockingTag()
    {
        var manager = SkillManager.Instance;
        var recorded = new RecordingEffect();
        SetField(manager, "_types", new Dictionary<uint, EffectType> { [1] = new() { Id = 1, Type = "Test", ActualId = 1 } });
        SetField(manager, "_effects", new Dictionary<string, Dictionary<uint, EffectTemplate>> { ["Test"] = new() { [1] = recorded } });
        var caster = new TestUnit { ObjId = 1 };
        var other = new TestUnit { ObjId = 2 };
        var target = new TestUnit { ObjId = 3 };
        var mark = Register(new BuffTemplate { Id = 2 }); // Tag 11.
        var otherMark = Add(target, other, mark);
        var template = Register(new BuffTemplate { Id = 9202 });
        template.TickEffects.Add(new TickEffect { EffectId = 1, TargetNoBuffTagId = 11, CheckNoTargetTagSrc = true });
        var casterObject = new SkillCasterUnit(caster.ObjId);
        var periodic = new Buff(target, caster, casterObject, template, null, DateTime.UtcNow);

        template.TimeToTimeApply(caster, target, periodic, casterObject);
        await Assert.That(recorded.Applications).IsEqualTo(1);
        otherMark.Exit();
        Add(target, caster, mark);
        template.TimeToTimeApply(caster, target, periodic, casterObject);
        await Assert.That(recorded.Applications).IsEqualTo(1);
    }

    private BuffTemplate Register(BuffTemplate template)
    {
        _templates.Add(template.Id, template);
        return template;
    }

    private static Buff Add(TestUnit owner, TestUnit caster, BuffTemplate template, DateTime? time = null)
    {
        var buff = new Buff(owner, caster, new SkillCasterUnit(caster.ObjId), template, null, time ?? DateTime.UtcNow);
        owner.Buffs.AddBuff(buff);
        return buff;
    }

    private static void Apply(TestUnit owner, TestUnit caster, BuffTemplate template, Skill skill = null)
    {
        template.Apply(caster, new SkillCasterUnit(caster.ObjId), owner, new SkillCastUnitTarget(owner.ObjId),
            null, skill == null ? new EffectSource(template) : new EffectSource(skill), null, DateTime.UtcNow);
    }

    private static void ApplySpecial(SpecialType type, Unit caster, Unit target, int first, int second, int third)
    {
        var effect = new SpecialEffect { SpecialEffectTypeId = type, Value1 = first, Value2 = second, Value3 = third };
        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), target, new SkillCastUnitTarget(target.ObjId),
            new CastSkill(1, 1), new EffectSource(), null, DateTime.UtcNow);
    }

    private void Install<T>(T instance) where T : class
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _singletons.Add((field, field.GetValue(null)));
        field.SetValue(null, instance);
    }

    private static void SetField<T>(T instance, string name, object value)
    {
        typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
    }

    private sealed class TestUnit : Unit
    {
        public GamePacket LastPacket { get; private set; }
        public int PacketsSent { get; private set; }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            LastPacket = packet;
            PacketsSent++;
        }
    }

    private sealed class TestCharacter() : Character(null)
    {
        public override void BroadcastPacket(GamePacket packet, bool self) { }
    }

    private sealed class RecordingEffect : EffectTemplate
    {
        public override bool OnActionTime => false;
        public int Applications { get; private set; }
        public BaseUnit Source { get; private set; }
        public BaseUnit Target { get; private set; }
        public int Amount { get; private set; }
        public Func<bool> OnApply { get; init; }
        public int AcceptedApplications { get; private set; }

        public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
            CompressedGamePackets packetBuilder = null)
        {
            Applications++;
            Source = caster;
            Target = target;
            Amount = source.Amount;
            if (OnApply?.Invoke() == true)
                AcceptedApplications++;
        }
    }
}
