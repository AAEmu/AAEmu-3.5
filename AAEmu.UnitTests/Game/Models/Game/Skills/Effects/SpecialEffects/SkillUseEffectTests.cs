using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;
using GameTask = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

[NotInParallel]
public class SkillUseEffectTests
{
    private readonly List<(FieldInfo Field, object Previous)> _singletons = [];
    private readonly Dictionary<uint, List<BuffTriggerTemplate>> _triggers = [];
    private readonly Unit _dancer = new TestUnit { ObjId = 12, Hp = 1 };
    private readonly TaskManager _tasks = new(null);

    [Before(Test)]
    public void Setup()
    {
        var skills = new SkillManager(null, null);
        SetField(skills, "_skills", new Dictionary<uint, SkillTemplate>
        {
            [21439] = new() { Id = 21439, TargetType = SkillTargetType.Self },
            [24459] = new() { Id = 24459 },
            [35250] = new() { Id = 35250, TargetType = SkillTargetType.Self }
        });
        SetField(skills, "_buffTags", new Dictionary<uint, List<uint>>());
        SetField(skills, "_buffTriggers", _triggers);
        SetField(skills, "_skillModifiers", new Dictionary<uint, List<SkillModifier>>());
        SetField(skills, "_combatBuffs", new Dictionary<uint, List<CombatBuffTemplate>>());
        Install(skills);
        var buffData = new BuffGameData();
        SetField(buffData, "_buffModifiers", new Dictionary<uint, List<BuffModifier>>());
        Install(buffData);
        Install(_tasks);
        Install(new EffectTaskManager(_tasks));
    }

    [After(Test)]
    public void TearDown()
    {
        _dancer.Buffs.CancelAllEffectTasks();
        foreach (var (field, previous) in _singletons)
            field.SetValue(null, previous);
    }

    [Test]
    public async Task StartingYataDanceKeepsTheBuffAndQueuesItsDelayedSpread()
    {
        _triggers[7271] =
        [
            new BuffTriggerTemplate { Id = 4427, Kind = BuffEventTriggerKind.Started, Effect = SpreadEffect() }
        ];
        Add(new BuffTemplate { Id = 7270 });
        var start = DateTime.UtcNow;

        Add(new BuffTemplate { Id = 7271, Duration = 6630, RequireBuffId = 7270, RemoveOnUseSkill = true });

        await Assert.That(_dancer.Buffs.CheckBuff(7271)).IsTrue();
        var spread = QueuedCast();
        await Assert.That(spread.TriggerTime).IsGreaterThanOrEqualTo(start.AddSeconds(2));
        await Assert.That(spread.TriggerTime).IsLessThanOrEqualTo(DateTime.UtcNow.AddSeconds(2));
    }

    [Test]
    public async Task SkillUseFromAPlotStillRemovesBuffsMarkedToEndOnSkillUse()
    {
        var caster = new SkillCasterUnit(_dancer.ObjId);
        var plot = new Skill(new SkillTemplate { Id = 24459 });
        Add(new BuffTemplate { Id = 7271, RemoveOnUseSkill = true });

        SpreadEffect().Apply(_dancer, caster, _dancer, new SkillCastUnitTarget(_dancer.ObjId),
            new CastPlot(1, 1, 1, plot.Id), new EffectSource(plot), null, DateTime.UtcNow);

        await Assert.That(_dancer.Buffs.CheckBuff(7271)).IsFalse();
        await Assert.That(QueuedCast()).IsNotNull();
    }

    [Test]
    [Arguments(0, 99, true)]
    [Arguments(1, 0, true)]
    [Arguments(1, 1, false)]
    [Arguments(10, 9, true)]
    [Arguments(10, 10, false)]
    [Arguments(100, 99, true)]
    [Arguments(1000, 99, true)]
    public async Task FollowUpChanceUsesExactlyTheAuthoredPercentage(int chance, int roll, bool expected)
    {
        await Assert.That(SkillUse.PassesChanceRoll(chance, roll)).IsEqualTo(expected);
    }

    [Test]
    public async Task MissingFollowUpSkillDoesNotQueueADeadCastOrRemoveBuffs()
    {
        Add(new BuffTemplate { Id = 7271, RemoveOnUseSkill = true });
        _dancer.ConditionChance = true;
        var effect = new SpecialEffect { Id = 19237, SpecialEffectTypeId = SpecialType.SkillUse, Value1 = 29809 };

        effect.Apply(_dancer, new SkillCasterUnit(_dancer.ObjId), _dancer, new SkillCastUnitTarget(_dancer.ObjId),
            new CastSkill(24459, 1), new EffectSource(), null, DateTime.UtcNow);

        await Assert.That(_dancer.Buffs.CheckBuff(7271)).IsTrue();
        await Assert.That(_dancer.ConditionChance).IsFalse();
        await Assert.That(QueuedCasts().Any()).IsFalse();
    }

    [Test]
    public async Task SoupBuffTickCastsManaRecoveryWithoutReusingTheConsumedItem()
    {
        const uint soupTemplateId = 8505;
        const uint tickEffectId = 60733;
        const uint skillUseId = 26849;
        var skills = SkillManager.Instance;
        SetField(skills, "_types", new Dictionary<uint, EffectType>
        {
            [tickEffectId] = new() { Id = tickEffectId, Type = "SpecialEffect", ActualId = skillUseId }
        });
        SetField(skills, "_effects", new Dictionary<string, Dictionary<uint, EffectTemplate>>
        {
            ["SpecialEffect"] = new()
            {
                [skillUseId] = new SpecialEffect { Id = skillUseId, SpecialEffectTypeId = SpecialType.SkillUse, Value1 = 35250 }
            }
        });
        var sourceBytes = new PacketStream();
        sourceBytes.WriteBc(_dancer.ObjId).Write(851uL).Write(soupTemplateId).Write((byte)0).Write(0u);
        var consumedItem = new SkillItem { Type = SkillCasterType.Item };
        consumedItem.Read(new PacketStream(sourceBytes.GetBytes()));
        var soup = new BuffTemplate { Id = 20629, Kind = BuffKind.Good, Duration = 5000, Tick = 1000 };
        soup.TickEffects.Add(new TickEffect { EffectId = tickEffectId });
        _dancer.Buffs.AddBuff(new Buff(_dancer, _dancer, consumedItem, soup, null, DateTime.UtcNow));

        var queue = GetQueue();
        var tick = queue.Values.OfType<DispelTask>().Single();
        queue.TryRemove(tick.Id, out _);
        tick.Execute();

        var cast = QueuedCast();
        var caster = GetQueuedCaster(cast);
        await Assert.That(caster).IsTypeOf<SkillCasterUnit>();
        await Assert.That(caster.ObjId).IsEqualTo(_dancer.ObjId);
        await Assert.That(_dancer.Buffs.GetEffectFromBuffId(soup.Id).SkillCaster).IsSameReferenceAs(consumedItem);
    }

    [Test]
    public async Task LanguageLessonPlotCastsTheTeachingAnimationFromTheTutor()
    {
        const uint tutorEffectId = 10807;
        SetField(SkillManager.Instance, "_effects", new Dictionary<string, Dictionary<uint, EffectTemplate>>
        {
            ["SpecialEffect"] = new()
            {
                [tutorEffectId] = new SpecialEffect
                {
                    Id = tutorEffectId, SpecialEffectTypeId = SpecialType.SkillUse, Value1 = 21439, Value3 = 1000
                }
            }
        });
        var tutor = new Npc { ObjId = 42, Hp = 100 };
        var studentSource = new SkillCasterUnit(_dancer.ObjId);
        var lesson = new Skill(new SkillTemplate { Id = 21520, TargetType = SkillTargetType.AnyUnit }) { TlId = 1 };
        var state = new PlotState(_dancer, studentSource, tutor, new SkillCastUnitTarget(tutor.ObjId), new SkillObject(), lesson);
        var teachingEvent = new PlotEventTemplate
        {
            Id = 6722,
            PlotId = 805,
            SourceUpdateMethodId = (uint)PlotSourceUpdateMethodType.PreviousSource,
            TargetUpdateMethodId = (uint)PlotTargetUpdateMethodType.OriginalTarget
        };
        var teachingEffect = new PlotEventEffect
        {
            Id = 10338,
            EventId = teachingEvent.Id,
            ActualId = tutorEffectId,
            ActualType = "SpecialEffect",
            SourceId = PlotEffectSource.Target,
            TargetId = PlotEffectTarget.Source
        };
        var targetInfo = new PlotTargetInfo(state);
        targetInfo.UpdateTargetInfo(teachingEvent, state);
        byte flag = 0;

        teachingEffect.ApplyEffect(state, targetInfo, teachingEvent, ref flag);

        var cast = QueuedCast();
        var caster = GetQueuedCaster(cast);
        await Assert.That(caster).IsTypeOf<SkillCasterUnit>();
        await Assert.That(caster.ObjId).IsEqualTo(tutor.ObjId);
        await Assert.That(state.CasterCaster).IsSameReferenceAs(studentSource);
        await Assert.That(state.CasterCaster.ObjId).IsEqualTo(_dancer.ObjId);
    }

    private static SpecialEffect SpreadEffect() =>
        new() { Id = 15253, SpecialEffectTypeId = SpecialType.SkillUse, Value1 = 24459, Value2 = 2000 };

    private void Add(BuffTemplate template) => _dancer.Buffs.AddBuff(new Buff(_dancer, _dancer,
        new SkillCasterUnit(_dancer.ObjId), template, null, DateTime.UtcNow));

    private UseSkillTask QueuedCast() => QueuedCasts().Single();

    private static SkillCaster GetQueuedCaster(UseSkillTask cast) =>
        (SkillCaster)typeof(UseSkillTask).GetField("_casterCaster", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cast)!;

    private IEnumerable<UseSkillTask> QueuedCasts() => GetQueue().Values.OfType<UseSkillTask>();

    private ConcurrentDictionary<uint, GameTask> GetQueue() => (ConcurrentDictionary<uint, GameTask>)typeof(TaskManager)
        .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_tasks)!;

    private void Install<T>(T instance) where T : class
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _singletons.Add((field, field.GetValue(null)));
        field.SetValue(null, instance);
    }

    private static void SetField<T>(T target, string name, object value) =>
        typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private sealed class TestUnit : Unit
    {
        public override void BroadcastPacket(GamePacket packet, bool self)
        {
        }
    }
}
