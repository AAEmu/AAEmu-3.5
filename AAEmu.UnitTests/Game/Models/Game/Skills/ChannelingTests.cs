using System.Collections.Concurrent;
using System.Reflection;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;
using GameTask = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

[NotInParallel]
public class ChannelingTests
{
    private static readonly FieldInfo s_taskManagerInstance = typeof(Singleton<TaskManager>)
        .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo s_taskQueue = typeof(TaskManager)
        .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private readonly TaskManager _tasks = new(null);
    private object _previousTaskManager;

    [Before(Test)]
    public void Setup()
    {
        _previousTaskManager = s_taskManagerInstance.GetValue(null);
        s_taskManagerInstance.SetValue(null, _tasks);
    }

    [After(Test)]
    public void TearDown() => s_taskManagerInstance.SetValue(null, _previousTaskManager);

    [Test]
    public async Task ChannelDurationModifierAlsoExtendsTheManaTickBudget()
    {
        var caster = new TestUnit { ObjId = 101, Hp = 100, Mp = 50 };
        caster.SkillModifiersCache.AddModifier(new SkillModifier
        {
            SkillId = 10714, SkillAttribute = SkillAttribute.ChannelingTime, UnitModifierType = UnitModifierType.Value, Value = 2000
        });
        var startedAt = DateTime.UtcNow;
        Start(caster, duration: 2500, interval: 1000, cost: 10, skillId: 10714);
        var end = caster.SkillTask;
        var scheduled = end.TriggerTime - startedAt;
        end.Execute();

        await Assert.That(scheduled.TotalMilliseconds >= 4500).IsTrue();
        await Assert.That(caster.Mp).IsEqualTo(10);
        await Assert.That(caster.AdvertisedChannelTime).IsEqualTo(4500);
    }

    [Test]
    public async Task FiredPacketEncodesLongChannelingInTenMillisecondUnits()
    {
        var caster = new SkillCasterUnit(1);
        var target = new SkillCastUnitTarget(2);
        var skillObject = new SkillObject();
        var skill = new Skill(new SkillTemplate { ChannelingTime = 180_000 });
        var packet = new SCSkillFiredPacket(1, 1, caster, target, skill, skillObject);
        var prefix = new PacketStream();
        prefix.Write((ushort)1);
        prefix.Write(caster);
        prefix.Write(target);
        prefix.Write(skillObject);
        prefix.Write((ushort)0); // effect delay

        var bytes = packet.Write(new PacketStream()).GetBytes();
        var wireChannelTime = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(prefix.GetBytes().Length));
        await Assert.That(wireChannelTime).IsEqualTo((ushort)18_000);
    }

    [Test]
    public async Task FirstTickChargesTheAuthoredAmountAndNaturalEndSettlesTheLastTick()
    {
        var caster = new TestUnit { ObjId = 101, Hp = 100, Mp = 50 };
        var skill = Start(caster, duration: 2500, interval: 1000, cost: 14);
        var end = caster.SkillTask;

        await Assert.That(caster.Mp).IsEqualTo(50);

        var tick = GetTickTask();
        await Assert.That(tick).IsNotNull();
        await Assert.That(tick!.RepeatInterval).IsEqualTo(TimeSpan.FromSeconds(1));
        tick.Execute();
        await Assert.That(caster.Mp).IsEqualTo(36);

        end.Execute();
        tick.Execute(); // Callback dispatched immediately before the channel ended.

        await Assert.That(caster.Mp).IsEqualTo(22);
        await Assert.That(caster.SkillTask).IsNull();
        await Assert.That(caster.EndedTimelines.Count).IsEqualTo(1);
        await Assert.That(skill.Cancelled).IsFalse();
    }

    [Test]
    public async Task InterruptedChannelEndsOnceAndLateCallbacksLeaveTheReplacementCastAlone()
    {
        var caster = new TestUnit { ObjId = 101, Hp = 100, Mp = 50 };
        var skill = Start(caster);
        var timeline = skill.TlId;
        var end = caster.SkillTask;
        var tick = GetTickTask();
        var callbackCount = 0;
        skill.Callback = () => callbackCount++;

        caster.InterruptSkills();
        var replacement = Start(caster, cost: 0);
        var replacementTask = caster.SkillTask;
        end.Execute();
        tick?.Execute();

        await Assert.That(skill.Cancelled).IsTrue();
        await Assert.That(caster.Mp).IsEqualTo(50);
        await Assert.That(callbackCount).IsEqualTo(1);
        await Assert.That(caster.EndedTimelines).IsEquivalentTo(new[] { timeline });
        await Assert.That(caster.SkillTask).IsSameReferenceAs(replacementTask);
        await Assert.That(_tasks.GetQueueCount()).IsEqualTo(1);
        replacement.Stop(caster);
    }

    [Test]
    public async Task PendingEndOfAnOverwrittenChannelKeepsTheReplacementCastAndDoesNotDrainMana()
    {
        var caster = new TestUnit { ObjId = 101, Hp = 100, Mp = 50 };
        var first = Start(caster);
        var timeline = first.TlId;
        var firstEnd = caster.SkillTask;
        var tick = GetTickTask();
        var second = Start(caster, cost: 0);
        var secondEnd = caster.SkillTask;

        tick?.Execute();
        firstEnd.Execute();

        await Assert.That(first.Cancelled).IsTrue();
        await Assert.That(caster.Mp).IsEqualTo(50);
        await Assert.That(caster.EndedTimelines).IsEquivalentTo(new[] { timeline });
        await Assert.That(caster.SkillTask).IsSameReferenceAs(secondEnd);
        second.Stop(caster);
    }

    [Test]
    public async Task InstantSkillDoesNotClearTheRunningChannelsTask()
    {
        var caster = new TestUnit { ObjId = 101, Hp = 100, Mp = 50 };
        Start(caster);
        var channel = caster.SkillTask;
        var tick = GetTickTask();
        var instant = new Skill(new SkillTemplate { Id = 100, EffectDelay = 100 })
        {
            TlId = SkillTlIdManager.GetNextId(caster)
        };

        instant.Cast(caster, new SkillCasterUnit(caster.ObjId), caster, new SkillCastUnitTarget(caster.ObjId), new SkillObject());
        tick!.Execute();

        await Assert.That(caster.SkillTask).IsSameReferenceAs(channel);
        await Assert.That(caster.Mp).IsEqualTo(36);
        caster.InterruptSkills();
        SkillTlIdManager.ReleaseId(instant.TlId);
    }

    [Test]
    public async Task TickChargesOnlyTheRemainingMana()
    {
        var caster = new TestUnit { ObjId = 101, Hp = 100, Mp = 5 };
        var skill = Start(caster);

        var tick = GetTickTask();
        await Assert.That(tick).IsNotNull();
        tick!.Execute();
        tick.Execute();

        await Assert.That(caster.Mp).IsEqualTo(0);
        await Assert.That(caster.ManaUpdates).IsEqualTo(1);
        skill.Stop(caster);
    }

    [Test]
    public async Task StopChannelingEffectInterruptsTheTargetOnlyAndIgnoresLateCallbacks()
    {
        var source = new TestUnit { ObjId = 101, Hp = 100, Mp = 50 };
        var target = new TestUnit { ObjId = 102, Hp = 100, Mp = 50 };
        var sourceSkill = Start(source, cost: 0);
        var targetSkill = Start(target, cost: 0);
        var pending = target.SkillTask;
        var effect = new SpecialEffect { SpecialEffectTypeId = SpecialType.StopChanneling };

        effect.Apply(source, new SkillCasterUnit(source.ObjId), target, new SkillCastUnitTarget(target.ObjId),
            new CastSkill(1, 1), new EffectSource(), null, DateTime.UtcNow);
        pending.Execute();

        await Assert.That(targetSkill.Cancelled).IsTrue();
        await Assert.That(target.SkillTask).IsNull();
        await Assert.That(target.EndedTimelines.Count).IsEqualTo(1);
        await Assert.That(sourceSkill.Cancelled).IsFalse();
        await Assert.That(source.SkillTask).IsNotNull();
        source.InterruptSkills();
    }

    private GameTask GetTickTask()
    {
        var queue = (ConcurrentDictionary<uint, GameTask>)s_taskQueue.GetValue(_tasks)!;
        foreach (var task in queue.Values)
        {
            if (task is not EndChannelingTask)
                return task;
        }

        return null;
    }

    private static Skill Start(TestUnit caster, int duration = 2000, int interval = 1000, int cost = 14, uint skillId = 0)
    {
        var skill = new Skill(new SkillTemplate { Id = skillId, ChannelingTime = duration, ChannelingTick = interval, ChannelingMana = cost })
        {
            TlId = SkillTlIdManager.GetNextId(caster)
        };
        skill.StartChanneling(caster, new SkillCasterUnit(caster.ObjId), caster, new SkillCastUnitTarget(caster.ObjId), null);
        return skill;
    }

    private sealed class TestUnit : Unit
    {
        public List<ushort> EndedTimelines { get; } = [];
        public int ManaUpdates { get; private set; }
        public int AdvertisedChannelTime { get; private set; }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            if (packet is SCSkillEndedPacket ended)
                EndedTimelines.Add(ended.TlId);
            if (packet is SCUnitPointsPacket)
                ManaUpdates++;
            if (packet is SCSkillFiredPacket fired)
                AdvertisedChannelTime = fired.ChannelingTime;
        }
    }
}
