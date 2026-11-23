using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

[NotInParallel]
public sealed class StandaloneCastReleaseTests
{
    private readonly List<(FieldInfo Field, object Previous)> _previous = [];
    private readonly TestCharacter _caster = new() { ObjId = 201, Level = 55 };

    [Before(Test)]
    public void Setup()
    {
        var skills = new SkillManager(null, null);
        SetField(skills, "_skillReagents", new Dictionary<uint, SkillReagent>());
        SetField(skills, "_skillProducts", new Dictionary<uint, SkillProduct>());
        Install(skills);
        Install(new TaskManager(null));
        _caster.Hp = 100;
        _caster.Actability = new CharacterActability(_caster);
        var world = new WorldInstance(new WorldTemplate(), 0, true, 0);
        var worlds = new WorldManager(null, null, null, null, null);
        SetField(worlds, "_worlds", new ConcurrentDictionary<uint, WorldInstance> { [world.Id] = world });
        Install(worlds);
        _caster.ParentWorld = world;
        world.AddObject(_caster);
    }

    [After(Test)]
    public void Cleanup()
    {
        _caster.InterruptSkills();
        foreach (var (field, previous) in _previous)
            field.SetValue(null, previous);
    }

    [Test]
    public async Task UseQueuesTheStandaloneCastAndTheNativePacketFiresItsOwnTimeline()
    {
        var effect = new RecordedEffect();
        var template = CreateTemplate(5_000);
        template.Effects.Add(Band(effect, 1, 100));
        var skill = new Skill(template);
        var starts = skill.Use(_caster, new SkillCasterUnit(_caster.ObjId), new SkillCastUnitTarget(_caster.ObjId), null, true, out _);
        await Assert.That(starts).IsEqualTo(SkillResult.Success);
        var task = (CastTask)_caster.SkillTask;
        SendRelease((ushort)(skill.TlId + 1));
        await Assert.That(_caster.SkillTask).IsSameReferenceAs(task);

        // The cast is scheduled by the real Use path. Read the packet after the first percent.
        await Task.Delay(75);
        SendRelease(skill.TlId);
        task.Execute();

        await Assert.That(effect.Applications).IsEqualTo(1);
        await Assert.That(effect.Progress).IsBetween(1, 99);
        await Assert.That(_caster.SkillTask).IsNull();
    }

    [Test]
    public async Task FixedResourceCostAllowsCastingWithMoreResourceThanItsCost()
    {
        _caster.HighAbilityRsc = 300;
        var template = CreateTemplate(5_000);
        template.MinHighAbilityResource = 2;
        template.MaxHighAbilityResource = 2;
        var skill = new Skill(template);

        var result = skill.Use(_caster, new SkillCasterUnit(_caster.ObjId), new SkillCastUnitTarget(_caster.ObjId),
            null, true, out _);

        await Assert.That(result).IsEqualTo(SkillResult.Success);
    }

    [Test]
    public async Task ReleaseBeforeOnePercentOrBetweenBandsLeavesTheTimerAndOnlyMatchingEffectsApply()
    {
        var early = new RecordedEffect();
        var late = new RecordedEffect();
        var (skill, cast) = MakeCast(1_000, createEffects: [Band(early, 1, 39), Band(late, 70, 100)]);

        await Assert.That(cast.TryFireEarly(_caster, skill.TlId, 1_049)).IsFalse();
        await Assert.That(cast.TryFireEarly(_caster, skill.TlId, 3_500)).IsFalse();
        await Assert.That(cast.TryFireEarly(_caster, skill.TlId, 4_500)).IsTrue();
        cast.Execute();

        await Assert.That(early.Applications).IsEqualTo(0);
        await Assert.That(late.Applications).IsEqualTo(1);
        await Assert.That(late.Progress).IsEqualTo(70);
    }

    [Test]
    public async Task TimerAndReleaseCanOnlyFireOnce()
    {
        var effect = new RecordedEffect();
        var (skill, cast) = MakeCast(1_000, createEffects: [Band(effect, 1, 100)]);
        var tlId = skill.TlId;

        await Task.WhenAll(Task.Run(cast.Execute), Task.Run(() => cast.TryFireEarly(_caster, tlId, 3_500)));
        cast.Execute();

        await Assert.That(effect.Applications).IsEqualTo(1);
        await Assert.That(skill.TlId).IsEqualTo((ushort)0);
    }

    [Test]
    public async Task StopAndReleaseOnlyEndOnce()
    {
        var effect = new RecordedEffect();
        var (skill, cast) = MakeCast(1_000, createEffects: [Band(effect, 1, 100)]);
        var completions = 0;
        skill.Callback = () => Interlocked.Increment(ref completions);
        var tlId = skill.TlId;

        await Task.WhenAll(Task.Run(() => skill.Stop(_caster)), Task.Run(() => cast.TryFireEarly(_caster, tlId, 3_500)));
        cast.Execute();

        await Assert.That(completions).IsEqualTo(1);
        await Assert.That(effect.Applications).IsEqualTo(skill.Cancelled ? 0 : 1);
        await Assert.That(_caster.SkillTask).IsNull();
    }

    [Test]
    [Arguments(0)]
    [Arguments(100)]
    public async Task DisturbCastingStopsOrdinaryCastAndScheduledFireCannotApplyItsEffect(int chance)
    {
        var effect = new RecordedEffect();
        var (skill, cast) = MakeCast(1_000, [Band(effect, 1, 100)], canReleaseEarly: false);
        var completions = 0;
        skill.Callback = () => Interlocked.Increment(ref completions);

        new DisturbCasting().Execute(_caster, null, _caster, null, null, null, null, DateTime.UtcNow, chance, 0, 0, 0);
        cast.Execute();

        await Assert.That(skill.Cancelled).IsTrue();
        await Assert.That(effect.Applications).IsEqualTo(0);
        await Assert.That(completions).IsEqualTo(1);
        await Assert.That(_caster.SkillTask).IsNull();
    }

    [Test]
    public async Task OrdinaryCastTimerAndInterruptionOnlyCompleteOnce()
    {
        var effect = new RecordedEffect();
        var (skill, cast) = MakeCast(1_000, [Band(effect, 1, 100)], canReleaseEarly: false);
        var completions = 0;
        skill.Callback = () => Interlocked.Increment(ref completions);

        await Task.WhenAll(Task.Run(cast.Execute), Task.Run(() =>
            new DisturbCasting().Execute(_caster, null, _caster, null, null, null, null, DateTime.UtcNow, 100, 0, 0, 0)));
        cast.Execute();

        await Assert.That(completions).IsEqualTo(1);
        await Assert.That(effect.Applications).IsEqualTo(skill.Cancelled ? 0 : 1);
        await Assert.That(_caster.SkillTask).IsNull();
    }

    [Test]
    public async Task ReplacementDiscardsTheOldReleaseAndScheduledFire()
    {
        var oldEffect = new RecordedEffect();
        var (oldSkill, oldTask) = MakeCast(1_000, createEffects: [Band(oldEffect, 1, 100)]);
        var oldTimeline = oldSkill.TlId;
        var newEffect = new RecordedEffect();
        var (newSkill, newTask) = MakeCast(1_200, createEffects: [Band(newEffect, 1, 100)]);

        SendRelease(oldTimeline);
        oldTask.Execute();

        await Assert.That(oldEffect.Applications).IsEqualTo(0);
        await Assert.That(oldSkill.TlId).IsEqualTo((ushort)0);
        await Assert.That(_caster.SkillTask).IsSameReferenceAs(newTask);
        await Assert.That(newTask.TryFireEarly(_caster, newSkill.TlId, 3_700)).IsTrue();
        await Assert.That(newEffect.Applications).IsEqualTo(1);
    }

    private void Install<T>(T instance) where T : Singleton<T>
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _previous.Add((field, field.GetValue(null)));
        field.SetValue(null, instance);
    }

    private static void SetField<T>(T instance, string name, object value) =>
        typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private void SendRelease(ushort tlId)
    {
        var packet = new CSFireCastingSkillPacket { Connection = new GameConnection(null) { ActiveChar = _caster } };
        var contents = new PacketStream().WriteBc(_caster.ObjId).Write(tlId).Write((ushort)0);
        contents.Rollback();
        packet.Read(contents);
    }

    private (Skill Skill, CastTask Task) MakeCast(long startedAtMs, SkillEffect[] createEffects, bool canReleaseEarly = true)
    {
        var template = CreateTemplate(5_000);
        template.CastingUseable = canReleaseEarly;
        template.Effects.AddRange(createEffects);
        var skill = new Skill(template) { TlId = SkillTlIdManager.GetNextId(_caster) };
        var task = new CastTask(skill, _caster, new SkillCasterUnit(_caster.ObjId), _caster,
            new SkillCastUnitTarget(_caster.ObjId), new SkillObject(), template.CastingTime, startedAtMs);
        _caster.SkillTask = task;
        return (skill, task);
    }

    private static SkillTemplate CreateTemplate(int castTime) =>
        new() { Id = 9001524, AbilityId = AbilityType.General, CastingUseable = true, CastingTime = castTime };

    private static SkillEffect Band(EffectTemplate effect, int low, int high) => new()
    {
        ApplicationMethod = SkillEffectApplicationMethod.Target, StartCastingUseChance = low, EndCastingUseChance = high,
        EndLevel = 99, Chance = 100, Front = true, Back = true, Template = effect
    };

    private sealed class TestCharacter() : Character(null)
    {
        public override int MaxHp => 100;
        public override int MaxMp => 100;
        public override void BroadcastPacket(GamePacket packet, bool self) { }
    }

    private sealed class RecordedEffect : EffectTemplate
    {
        public int Applications { get; private set; }
        public int Progress { get; private set; }
        public override bool OnActionTime => false;

        public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
            CompressedGamePackets packetBuilder = null)
        {
            Applications++;
            Progress = source.Skill.EarlyCastingProgress ?? 100;
        }
    }
}
