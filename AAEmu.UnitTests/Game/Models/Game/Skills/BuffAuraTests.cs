using System.Collections.Concurrent;
using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.Skills;
using GameTask = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

[NotInParallel]
public class BuffAuraTests
{
    private readonly Dictionary<uint, BuffTemplate> _templates = [];
    private readonly List<(FieldInfo Field, object Previous)> _singletons = [];
    private ConcurrentDictionary<uint, GameTask> _queue;

    [Before(Test)]
    public void Setup()
    {
        var skills = new SkillManager(null, null);
        SetField(skills, "_buffs", _templates);
        SetField(skills, "_buffTags", new Dictionary<uint, List<uint>>());
        SetField(skills, "_buffTriggers", new Dictionary<uint, List<BuffTriggerTemplate>>());
        SetField(skills, "_skillModifiers", new Dictionary<uint, List<SkillModifier>>());
        SetField(skills, "_combatBuffs", new Dictionary<uint, List<CombatBuffTemplate>>());
        Install(skills);
        var buffData = new BuffGameData();
        SetField(buffData, "_buffModifiers", new Dictionary<uint, List<BuffModifier>>());
        Install(buffData);

        var tasks = new TaskManager(null);
        Install(tasks);
        Install(new EffectTaskManager(tasks));
        _queue = (ConcurrentDictionary<uint, GameTask>)GetField<TaskManager>("_queue").GetValue(tasks)!;

        const int regionCount = WorldManager.SECTORS_PER_CELL;
        var template = new WorldTemplate { CellX = 1, CellY = 1, ZoneKeyByRegions = new uint[regionCount, regionCount] };
        var world = new WorldInstance(template, 0, true, 1) { Regions = new Region[regionCount, regionCount] };
        world.MateManager = new MateManager(world);
        var worlds = new WorldManager(null, null, null, null, null);
        SetField(worlds, "_worlds", new ConcurrentDictionary<uint, WorldInstance> { [world.Id] = world });
        Install(worlds);
        Install(new TeamManager(worlds, null, null));
    }

    [After(Test)]
    public void TearDown()
    {
        foreach (var (field, previous) in _singletons)
            field.SetValue(null, previous);
    }

    [Test]
    public async Task AuraWithoutAnAuthoredTickFollowsMovementAndReleasesItsBuffOnDispel()
    {
        var source = PlaceUnit(1, 24);
        var target = PlaceUnit(2, 38);
        var child = Register(new BuffTemplate { Id = 4001 });
        var aura = Add(source, source, Register(new BuffTemplate { Id = 4002, AuraSlaveBuffId = child.Id, AuraRadius = 8 }));

        RunAuraPulse();
        await Assert.That(target.Buffs.CheckBuff(child.Id)).IsFalse();
        Move(target, 29);
        RunAuraPulse();
        await Assert.That(target.Buffs.CheckBuff(child.Id)).IsTrue();

        Move(target, 38);
        RunAuraPulse();
        await Assert.That(target.Buffs.CheckBuff(child.Id)).IsFalse();
        Move(target, 29);
        RunAuraPulse();
        await Assert.That(target.Buffs.CheckBuff(child.Id)).IsTrue();

        var queuedPulse = GetAuraPulse();
        aura.Exit();
        await Assert.That(target.Buffs.CheckBuff(child.Id)).IsFalse();
        await Assert.That(_queue.IsEmpty).IsTrue();
        queuedPulse?.Execute();
        await Assert.That(target.Buffs.CheckBuff(child.Id)).IsFalse();
    }

    [Test]
    public async Task CreatorOnlyAuraUsesItsCasterAsTheRecipientAndItsHolderAsTheCenter()
    {
        var caster = PlaceUnit(1, 36);
        var holder = PlaceUnit(2, 24);
        var child = Register(new BuffTemplate { Id = 4001 });
        Add(holder, caster, Register(new BuffTemplate
        {
            Id = 4002, AuraRadius = 8, AuraSlaveBuffId = child.Id, AuraCreatorOnly = true
        }));

        RunAuraPulse();
        await Assert.That(caster.Buffs.CheckBuff(child.Id)).IsFalse();
        Move(caster, 28);
        RunAuraPulse();
        await Assert.That(caster.Buffs.CheckBuff(child.Id)).IsTrue();
        await Assert.That(holder.Buffs.CheckBuff(child.Id)).IsFalse();
    }

    [Test]
    public async Task ChildOnlyAuraChecksOwnershipAndRelation()
    {
        var caster = new Character(new UnitCustomModelParams()) { ObjId = 1, Hp = 100 };
        var holder = PlaceUnit(2, 24);
        var ownedUnit = PlaceUnit(3, 27, caster);
        var unrelatedUnit = PlaceUnit(4, 28);
        var child = Register(new BuffTemplate { Id = 4001 });
        Add(holder, caster, Register(new BuffTemplate
        {
            Id = 4002, AuraRadius = 8, AuraSlaveBuffId = child.Id, AuraChildOnly = true,
            AuraRelationId = (uint)SkillTargetRelation.Others
        }));

        RunAuraPulse();
        await Assert.That(ownedUnit.Buffs.CheckBuff(child.Id)).IsTrue();
        await Assert.That(unrelatedUnit.Buffs.CheckBuff(child.Id)).IsFalse();
        await Assert.That(holder.Buffs.CheckBuff(child.Id)).IsFalse();
    }

    [Test]
    public async Task PartyAuraReachesTheCasterAndSameTeamOnly()
    {
        var source = Place(new TestCharacter { ObjId = 1 }, 24);
        var member = Place(new TestCharacter { ObjId = 2 }, 27);
        var outsider = Place(new TestCharacter { ObjId = 3 }, 28);
        var team = new Team { Id = 1, IsParty = true, Members = [new TeamMember(source), new TeamMember(member)] };
        var activeTeams = (ConcurrentDictionary<uint, Team>)GetField<TeamManager>("_activeTeams").GetValue(TeamManager.Instance)!;
        activeTeams[team.Id] = team;

        var child = Register(new BuffTemplate { Id = 4001 });
        Add(source, source, Register(new BuffTemplate
        {
            Id = 4002, AuraRadius = 8, AuraSlaveBuffId = child.Id, AuraRelationId = (uint)SkillTargetRelation.Party
        }));

        RunAuraPulse();
        await Assert.That(source.Buffs.CheckBuff(child.Id)).IsTrue();
        await Assert.That(member.Buffs.CheckBuff(child.Id)).IsTrue();
        await Assert.That(outsider.Buffs.CheckBuff(child.Id)).IsFalse();
    }

    [Test]
    public async Task PartyChildAuraReachesAnAttachedRiderEvenWithoutAGroup()
    {
        var mount = PlaceUnit(1, 24);
        var rider = Place(new TestCharacter { ObjId = 2 }, 24);
        var config = AppConfiguration.Instance;
        var debugInfo = config.DebugInfo;
        try
        {
            config.DebugInfo = false;
            rider.Transform.Parent = mount.Transform;
            var outsider = PlaceUnit(3, 25);
            var child = Register(new BuffTemplate { Id = 4001 });
            Add(mount, mount, Register(new BuffTemplate
            {
                Id = 4002, AuraRadius = 8, AuraSlaveBuffId = child.Id, AuraChildOnly = true,
                AuraRelationId = (uint)SkillTargetRelation.Party
            }));

            RunAuraPulse();
            await Assert.That(rider.Buffs.CheckBuff(child.Id)).IsTrue();
            await Assert.That(outsider.Buffs.CheckBuff(child.Id)).IsFalse();
        }
        finally
        {
            rider.Transform.Parent = null;
            config.DebugInfo = debugInfo;
        }
    }

    [Test]
    public async Task AuraRetriesWhenARequiredBuffAppearsOrItsGrantIsDispelled()
    {
        var source = PlaceUnit(1, 24);
        var target = PlaceUnit(2, 27);
        var requirement = Register(new BuffTemplate { Id = 4003 });
        var child = Register(new BuffTemplate { Id = 4001, RequireBuffId = requirement.Id });
        Add(source, source, Register(new BuffTemplate { Id = 4002, AuraSlaveBuffId = child.Id, AuraRadius = 8 }));

        RunAuraPulse();
        await Assert.That(target.Buffs.CheckBuff(child.Id)).IsFalse();
        Add(target, target, requirement);
        RunAuraPulse();
        await Assert.That(target.Buffs.CheckBuff(child.Id)).IsTrue();

        target.Buffs.RemoveBuff(child.Id);
        RunAuraPulse();
        await Assert.That(target.Buffs.CheckBuff(child.Id)).IsTrue();
    }

    [Test]
    public async Task AuraCleanupPreservesAnIndependentlyRefreshedBuff()
    {
        var source = PlaceUnit(1, 24);
        var target = PlaceUnit(2, 27);
        var child = Register(new BuffTemplate { Id = 4001, Duration = 10000, StackRule = BuffStackRule.Refresh });
        var sourceBuff = Add(source, source, Register(new BuffTemplate { Id = 4002, AuraSlaveBuffId = child.Id, AuraRadius = 8 }));

        RunAuraPulse();
        await Assert.That(target.Buffs.CheckBuff(child.Id)).IsTrue();
        Add(target, target, child, forcedDuration: 20000);
        sourceBuff.Exit();

        var remaining = target.Buffs.GetEffectFromBuffId(child.Id);
        await Assert.That(remaining).IsNotNull();
        await Assert.That(remaining.Caster).IsSameReferenceAs(target);
        await Assert.That(remaining.Duration).IsEqualTo(20000);
    }

    [Test]
    public async Task RefreshedAuraUpdatesTheCasterAndLevelOfTheGrant()
    {
        var source = PlaceUnit(1, 24);
        var target = PlaceUnit(2, 27);
        var newCaster = PlaceUnit(3, 38);
        var child = Register(new BuffTemplate { Id = 4001, StackRule = BuffStackRule.Refresh });
        var aura = Register(new BuffTemplate
        {
            Id = 4002, AuraRadius = 8, AuraSlaveBuffId = child.Id, Duration = 10000, StackRule = BuffStackRule.Refresh
        });
        Add(source, source, aura);
        RunAuraPulse();
        await Assert.That(target.Buffs.GetEffectFromBuffId(child.Id).Caster).IsSameReferenceAs(source);

        var refreshed = new Buff(source, newCaster, new SkillCasterUnit(newCaster.ObjId), aura, null, DateTime.UtcNow)
        {
            AbLevel = 24
        };
        source.Buffs.AddBuff(refreshed, forcedDuration: 20000);
        RunAuraPulse();

        var active = target.Buffs.GetEffectFromBuffId(child.Id);
        await Assert.That(active.Caster).IsSameReferenceAs(newCaster);
        await Assert.That(active.AbLevel).IsEqualTo((ushort)24);
    }

    [Test]
    [Arguments(200u, 220f, 290f)]
    [Arguments(600u, 620f, 640f)]
    public async Task WideAuraUsesItsAuthoredRadiusBeyondTheNeighboringRegions(uint radius, float insideX, float outsideX)
    {
        var holder = PlaceUnit(1, 24);
        var near = PlaceUnit(2, insideX);
        var outside = PlaceUnit(3, outsideX);
        var child = Register(new BuffTemplate { Id = 4001 });
        Add(holder, holder, Register(new BuffTemplate { Id = 4002, AuraSlaveBuffId = child.Id, AuraRadius = radius }));

        RunAuraPulse();
        await Assert.That(near.Buffs.CheckBuff(child.Id)).IsTrue();
        await Assert.That(outside.Buffs.CheckBuff(child.Id)).IsFalse();
    }

    [Test]
    public async Task SelfReferencingAuraDoesNotSpreadFromTheFirstRecipient()
    {
        var source = PlaceUnit(1, 24);
        var target = PlaceUnit(2, 30);
        var unreachable = PlaceUnit(3, 36);
        var template = Register(new BuffTemplate { Id = 4002, AuraSlaveBuffId = 4002, AuraRadius = 8 });
        var aura = Add(source, source, template);

        RunAuraPulse();
        await Assert.That(target.Buffs.CheckBuff(template.Id)).IsTrue();
        RunAuraPulse();
        await Assert.That(unreachable.Buffs.CheckBuff(template.Id)).IsFalse();
        aura.Exit();
        await Assert.That(target.Buffs.CheckBuff(template.Id)).IsFalse();
    }

    [Test]
    public async Task MomentaryAuraGrantsLongLivedBuffThatRunsOnItsOwnTimer()
    {
        var source = PlaceUnit(1, 24);
        var target = PlaceUnit(2, 27);
        var child = Register(new BuffTemplate { Id = 4001, Duration = 120000 });
        Add(source, source, Register(new BuffTemplate
        {
            Id = 4002, AuraRadius = 8, AuraSlaveBuffId = child.Id, AuraRelationId = (uint)SkillTargetRelation.Others,
            Duration = 1000
        }));

        var sourceExpiry = _queue.Values.OfType<DispelTask>().Single();
        RunAuraPulse();
        await Assert.That(target.Buffs.CheckBuff(child.Id)).IsTrue();
        RunTask(sourceExpiry);
        await Assert.That(target.Buffs.CheckBuff(child.Id)).IsTrue();

        var grantExpiry = _queue.Values.OfType<DispelTask>().Single();
        RunTask(grantExpiry);
        await Assert.That(target.Buffs.CheckBuff(child.Id)).IsFalse();
    }

    [Test]
    public async Task LogoutKeepsTheSourceBuffForSaveButReleasesItsAuraGrants()
    {
        var source = PlaceUnit(1, 24);
        var target = PlaceUnit(2, 29);
        var child = Register(new BuffTemplate { Id = 4001 });
        var template = Register(new BuffTemplate { Id = 4002, AuraSlaveBuffId = child.Id, AuraRadius = 8 });
        Add(source, source, template);
        RunAuraPulse();
        var delayedPulse = GetAuraPulse();
        await Assert.That(target.Buffs.CheckBuff(child.Id)).IsTrue();

        source.Buffs.CancelAllEffectTasks();
        delayedPulse!.Execute();

        await Assert.That(source.Buffs.CheckBuff(template.Id)).IsTrue();
        await Assert.That(target.Buffs.CheckBuff(child.Id)).IsFalse();
        await Assert.That(_queue.IsEmpty).IsTrue();
    }

    [Test]
    [Arguments(20473u, 3000)]
    [Arguments(18506u, 5000)]
    public async Task SongOfImmortalityConsumesOneChargeAtEachAuthoredTick(uint buffId, int interval)
    {
        var source = PlaceUnit(1, 24);
        var target = PlaceUnit(2, 29);
        source.HighAbilityRsc = 250;
        var song = RegisterSong(buffId, interval);

        var before = DateTime.UtcNow;
        Add(source, source, song);
        var after = DateTime.UtcNow;
        RunAuraPulse();

        await Assert.That(target.Buffs.CheckBuff(song.AuraSlaveBuffId)).IsTrue();
        await Assert.That(source.HighAbilityRsc).IsEqualTo(250);
        var firstTick = _queue.Values.OfType<DispelTask>().Single();
        await Assert.That(firstTick.TriggerTime).IsGreaterThanOrEqualTo(before.AddMilliseconds(interval));
        await Assert.That(firstTick.TriggerTime).IsLessThanOrEqualTo(after.AddMilliseconds(interval));

        _queue.TryRemove(firstTick.Id, out _);
        Parallel.Invoke(firstTick.Execute, firstTick.Execute);

        await Assert.That(source.HighAbilityRsc).IsEqualTo(150);
        await Assert.That(source.HighAbilityPointsUpdates).IsEqualTo(1);
        await Assert.That(target.Buffs.CheckBuff(song.AuraSlaveBuffId)).IsTrue();

        RunTask(_queue.Values.OfType<DispelTask>().Single());

        await Assert.That(source.HighAbilityRsc).IsEqualTo(50);
        await Assert.That(source.HighAbilityPointsUpdates).IsEqualTo(2);
        await Assert.That(source.Buffs.CheckBuff(buffId)).IsFalse();
        await Assert.That(target.Buffs.CheckBuff(song.AuraSlaveBuffId)).IsFalse();
        await Assert.That(_queue.IsEmpty).IsTrue();
    }

    [Test]
    public async Task SongOfImmortalityWithInsufficientHatredEndsWithoutSpendingTheRemainder()
    {
        var source = PlaceUnit(1, 24);
        var target = PlaceUnit(2, 29);
        source.HighAbilityRsc = 99;
        var song = RegisterSong(20473, 3000);
        Add(source, source, song);
        RunAuraPulse();
        await Assert.That(target.Buffs.CheckBuff(song.AuraSlaveBuffId)).IsTrue();

        RunTask(_queue.Values.OfType<DispelTask>().Single());

        await Assert.That(source.HighAbilityRsc).IsEqualTo(99);
        await Assert.That(source.HighAbilityPointsUpdates).IsEqualTo(0);
        await Assert.That(source.Buffs.CheckBuff(song.Id)).IsFalse();
        await Assert.That(target.Buffs.CheckBuff(song.AuraSlaveBuffId)).IsFalse();
        await Assert.That(_queue.IsEmpty).IsTrue();
    }

    [Test]
    public async Task RemovedSongOfImmortalityCannotSpendHatredFromADelayedTick()
    {
        var source = PlaceUnit(1, 24);
        source.HighAbilityRsc = 200;
        var song = RegisterSong(18506, 5000);
        var original = Add(source, source, song);
        var staleTick = _queue.Values.OfType<DispelTask>().Single();

        original.Exit();
        Add(source, source, song);
        staleTick.Execute();

        await Assert.That(source.HighAbilityRsc).IsEqualTo(200);
        await Assert.That(source.HighAbilityPointsUpdates).IsEqualTo(0);
        await Assert.That(source.Buffs.CheckBuff(song.Id)).IsTrue();

        RunTask(_queue.Values.OfType<DispelTask>().Single());

        await Assert.That(source.HighAbilityRsc).IsEqualTo(100);
        await Assert.That(source.Buffs.CheckBuff(song.Id)).IsTrue();
    }

    [Test]
    public async Task AbsorbEffectConsumesOneSelfBuffAndRestoresTheAuthoredManaRange()
    {
        var source = Place(new TestCharacter { ObjId = 1, Mp = 100 }, 24);
        var first = Register(new BuffTemplate { Id = 4001, Kind = BuffKind.Good, Duration = 10000 });
        var second = Register(new BuffTemplate { Id = 4002, Kind = BuffKind.Good, Duration = 10000 });
        var negative = Register(new BuffTemplate { Id = 4003, Kind = BuffKind.Bad, Duration = 10000 });
        Add(source, source, first);
        Add(source, source, second);
        Add(source, source, negative);

        ApplyAbsorbEffect(source, source);

        await Assert.That(source.Buffs.CheckBuff(first.Id) ^ source.Buffs.CheckBuff(second.Id)).IsTrue();
        await Assert.That(source.Buffs.CheckBuff(negative.Id)).IsTrue();
        await Assert.That(source.Mp).IsGreaterThanOrEqualTo(500);
        await Assert.That(source.Mp).IsLessThanOrEqualTo(700);
        await Assert.That(source.PointsUpdates).IsEqualTo(1);
    }

    [Test]
    public async Task AbsorbEffectCanConsumeAnAllyBuffThatIsOnTheCaster()
    {
        var source = Place(new TestCharacter { ObjId = 1, Mp = 100 }, 24);
        var ally = PlaceUnit(2, 29);
        var good = Register(new BuffTemplate { Id = 4001, Kind = BuffKind.Good, Duration = 10000 });
        Add(source, ally, good);
        Add(ally, ally, good);

        ApplyAbsorbEffect(source, source);

        await Assert.That(source.Buffs.CheckBuff(good.Id)).IsFalse();
        await Assert.That(ally.Buffs.CheckBuff(good.Id)).IsTrue();
        await Assert.That(source.Mp).IsGreaterThanOrEqualTo(500);
    }

    [Test]
    public async Task AbsorbEffectLeavesAurasPassivesAndProtectedBuffsInPlace()
    {
        var source = Place(new TestCharacter { ObjId = 1, Mp = 100 }, 24);
        var passive = Register(new BuffTemplate { Id = 4002, Kind = BuffKind.Good });
        var exempt = Register(new BuffTemplate { Id = 4003, Kind = BuffKind.Good, Duration = 10000, Exempt = true });
        var system = Register(new BuffTemplate { Id = 4004, Kind = BuffKind.Good, Duration = 10000, System = true });
        var grant = Register(new BuffTemplate { Id = 4005, Kind = BuffKind.Good });
        var aura = Register(new BuffTemplate
        {
            Id = 4006, Kind = BuffKind.Good, AuraSlaveBuffId = grant.Id, AuraRadius = 15, Duration = 10000
        });
        source.Buffs.AddBuff(new Buff(source, source, new SkillCasterUnit(source.ObjId), passive, null, DateTime.UtcNow)
        {
            Passive = true
        });
        Add(source, source, exempt);
        Add(source, source, system);
        Add(source, source, aura);
        RunAuraPulse();

        ApplyAbsorbEffect(source, source);

        foreach (var template in new[] { passive, exempt, system, grant, aura })
            await Assert.That(source.Buffs.CheckBuff(template.Id)).IsTrue();
        await Assert.That(source.Mp).IsEqualTo(100);
        await Assert.That(source.PointsUpdates).IsEqualTo(0);
    }

    [Test]
    public async Task AbsorbEffectCannotRedeemAnotherUnitsBuff()
    {
        var source = Place(new TestCharacter { ObjId = 1, Mp = 100 }, 24);
        var other = Place(new TestCharacter { ObjId = 2, Mp = 100 }, 29);
        var good = Register(new BuffTemplate { Id = 4001, Kind = BuffKind.Good, Duration = 10000 });
        Add(source, source, good);
        Add(other, other, good);

        ApplyAbsorbEffect(source, other);

        await Assert.That(source.Buffs.CheckBuff(good.Id)).IsTrue();
        await Assert.That(other.Buffs.CheckBuff(good.Id)).IsTrue();
        await Assert.That(source.Mp).IsEqualTo(100);
        await Assert.That(other.Mp).IsEqualTo(100);
    }

    [Test]
    public async Task AbsorbEffectCanOnlySpendTheSameBuffOnceAndClampsMana()
    {
        var source = Place(new TestCharacter { ObjId = 1, Mp = 900 }, 24);
        var good = Register(new BuffTemplate { Id = 4001, Kind = BuffKind.Good, Duration = 10000 });
        Add(source, source, good);

        Parallel.Invoke(() => ApplyAbsorbEffect(source, source), () => ApplyAbsorbEffect(source, source));

        await Assert.That(source.Buffs.CheckBuff(good.Id)).IsFalse();
        await Assert.That(source.Mp).IsEqualTo(1000);
        await Assert.That(source.PointsUpdates).IsEqualTo(1);
    }

    private static void ApplyAbsorbEffect(Character source, Character target)
    {
        var effect = new SpecialEffect { SpecialEffectTypeId = SpecialType.RedeemBuff, Value1 = 4000, Value2 = 10, Value3 = 15 };
        effect.Apply(source, new SkillCasterUnit(source.ObjId), target, new SkillCastUnitTarget(target.ObjId),
            new CastSkill(11988, 1), new EffectSource(), null, DateTime.UtcNow);
    }

    private BuffTemplate RegisterSong(uint buffId, int tick)
    {
        var child = Register(new BuffTemplate { Id = 18348 });
        return Register(new BuffTemplate
        {
            Id = buffId,
            Tick = tick,
            AuraSlaveBuffId = child.Id,
            AuraRadius = 15,
            MinHighAbilityResource = 1,
            MaxHighAbilityResource = 1
        });
    }

    private GameTask GetAuraPulse() => _queue.Values.FirstOrDefault(task => task is not DispelTask);

    private void RunAuraPulse()
    {
        var pulse = GetAuraPulse();
        if (pulse != null)
            RunTask(pulse);
    }

    private void RunTask(GameTask task)
    {
        _queue.TryRemove(task.Id, out _);
        task.Execute();
    }

    private TestUnit PlaceUnit(uint objId, float x, Character characterOwner = null) =>
        Place(new TestUnit(characterOwner) { ObjId = objId }, x);

    private T Place<T>(T unit, float x) where T : Unit
    {
        unit.Hp = 100;
        Move(unit, x);
        var world = WorldManager.Instance.GetWorld(1);
        unit.Region = world.GetRegionByPos(unit.Transform.World.Position);
        unit.Region.AddObject(unit);
        world.AddObject(unit);
        return unit;
    }

    private static void Move(Unit unit, float x) => unit.Transform.Local.SetPosition(x, 24, 100);

    private BuffTemplate Register(BuffTemplate template)
    {
        _templates.Add(template.Id, template);
        return template;
    }

    private static Buff Add(Unit owner, Unit caster, BuffTemplate template, int forcedDuration = 0)
    {
        var buff = new Buff(owner, caster, new SkillCasterUnit(caster.ObjId), template, null, DateTime.UtcNow);
        owner.Buffs.AddBuff(buff, forcedDuration: forcedDuration);
        return buff;
    }

    private void Install<T>(T instance) where T : class
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _singletons.Add((field, field.GetValue(null)));
        field.SetValue(null, instance);
    }

    private static FieldInfo GetField<T>(string name) => typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static void SetField<T>(T instance, string name, object value) => GetField<T>(name).SetValue(instance, value);

    private sealed class TestUnit(Character characterOwner) : Unit
    {
        private int _highAbilityPointsUpdates;
        public int HighAbilityPointsUpdates => Volatile.Read(ref _highAbilityPointsUpdates);

        public override Character GetOwnerCharacter() => characterOwner;

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            if (packet is SCUnitPointsPacket)
                Interlocked.Increment(ref _highAbilityPointsUpdates);
        }
    }

    private sealed class TestCharacter() : Character(new UnitCustomModelParams())
    {
        private int _pointsUpdates;
        public int PointsUpdates => Volatile.Read(ref _pointsUpdates);
        public override int MaxMp => 1000;

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            if (packet is SCUnitPointsPacket)
                Interlocked.Increment(ref _pointsUpdates);
        }
    }
}
