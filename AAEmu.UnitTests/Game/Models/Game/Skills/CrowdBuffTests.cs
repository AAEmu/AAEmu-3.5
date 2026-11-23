using System.Collections.Concurrent;
using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

[NotInParallel]
public class CrowdBuffTests
{
    private readonly Dictionary<uint, BuffTemplate> _templates = [];
    private readonly Dictionary<uint, List<uint>> _tags = [];
    private readonly Dictionary<uint, List<BuffTriggerTemplate>> _triggers = [];
    private readonly List<(FieldInfo Field, object Previous)> _singletons = [];
    private readonly List<Unit> _units = [];
    private WorldInstance _world;
    private WorldInstance _otherWorld;

    [Before(Test)]
    public void Setup()
    {
        var skills = new SkillManager(null, null);
        SetField(skills, "_buffs", _templates);
        SetField(skills, "_buffTags", _tags);
        SetField(skills, "_taggedBuffs", new Dictionary<uint, List<uint>>());
        SetField(skills, "_buffTriggers", _triggers);
        SetField(skills, "_skillModifiers", new Dictionary<uint, List<SkillModifier>>());
        SetField(skills, "_combatBuffs", new Dictionary<uint, List<CombatBuffTemplate>>());
        Install(skills);
        var buffData = new BuffGameData();
        SetField(buffData, "_buffModifiers", new Dictionary<uint, List<BuffModifier>>());
        SetField(buffData, "_buffTolerances", new Dictionary<uint, BuffTolerance>());
        Install(buffData);

        var tasks = new TaskManager(null);
        Install(tasks);
        Install(new EffectTaskManager(tasks));

        const int regionCount = WorldManager.SECTORS_PER_CELL;
        var template = new WorldTemplate { CellX = 1, CellY = 1, ZoneKeyByRegions = new uint[regionCount, regionCount] };
        _world = new WorldInstance(template, 0, true, 1) { Regions = new Region[regionCount, regionCount] };
        _otherWorld = new WorldInstance(template, 0, true, 2) { Regions = new Region[regionCount, regionCount] };
        var worlds = new WorldManager(null, null, null, null, null);
        SetField(worlds, "_worlds", new ConcurrentDictionary<uint, WorldInstance> { [1] = _world, [2] = _otherWorld });
        Install(worlds);
    }

    [After(Test)]
    public void TearDown()
    {
        foreach (var unit in _units)
            unit.Buffs.CancelAllEffectTasks();

        foreach (var (field, previous) in _singletons)
            field.SetValue(null, previous);
    }

    [Test]
    public async Task MixedCargoCountsEachCarrierOnceAndIgnoresTagOnlyBuffs()
    {
        Register(new BuffTemplate { Id = 1857 });
        var firstCargo = RegisterSource(1454, 1857, 3, 5);
        var secondCargo = RegisterSource(5695, 1857, 3, 5);
        var thirdCargo = RegisterSource(6059, 1857, 3, 5);
        var tagOnly = Register(new BuffTemplate { Id = 6256 });
        _tags[firstCargo.Id] = [831];
        _tags[secondCargo.Id] = [831];
        _tags[thirdCargo.Id] = [831];
        _tags[tagOnly.Id] = [831];

        var first = Place(24);
        var second = Place(25);
        var third = Place(26);
        Add(first, firstCargo);
        Add(second, secondCargo);
        Add(second, thirdCargo);
        Add(third, tagOnly);
        RecheckCrowds();
        await Assert.That(first.Buffs.CheckBuff(1857)).IsFalse();

        Add(third, thirdCargo);
        RecheckCrowds();
        await Assert.That(first.Buffs.CheckBuff(1857)).IsTrue();
        await Assert.That(second.Buffs.CheckBuff(1857)).IsTrue();
        await Assert.That(third.Buffs.CheckBuff(1857)).IsTrue();

        third.Transform.Local.SetPosition(31, 24, 100);
        RecheckCrowds();
        await Assert.That(first.Buffs.CheckBuff(1857)).IsFalse();
        await Assert.That(second.Buffs.CheckBuff(1857)).IsFalse();
        await Assert.That(third.Buffs.CheckBuff(1857)).IsFalse();
    }

    [Test]
    public async Task CursedDestinyRequiresEightBearersIncludingTheHolder()
    {
        Register(new BuffTemplate { Id = 6006 });
        var curse = RegisterSource(6012, 6006, 8, 10, duration: 15000);
        var otherSource = RegisterSource(9001, 6006, 8, 10);
        var holder = Place(24);
        Add(holder, curse);
        for (var i = 0; i < 6; i++)
            Add(Place(25), curse);
        var other = Place(25);
        Add(other, otherSource);
        RecheckCrowds();
        await Assert.That(holder.Buffs.CheckBuff(6006)).IsFalse();

        Add(other, curse);
        RecheckCrowds();
        await Assert.That(holder.Buffs.CheckBuff(6006)).IsTrue();
        await Assert.That(other.Buffs.CheckBuff(6006)).IsTrue();
    }

    [Test]
    public async Task AGrantThatDispelsItsSourceDoesNotStarveTheOtherBearers()
    {
        var source = RegisterSource(15963, 15965, 3, 5, hostile: false, duration: 20000);
        source.Kind = BuffKind.Bad;
        var grant = Register(new BuffTemplate { Id = 15965 });
        _triggers[grant.Id] =
        [
            new BuffTriggerTemplate { Kind = BuffEventTriggerKind.Started, Effect = new DispelEffect { CureCount = 1 } }
        ];
        var first = Place(24);
        var second = Place(25);
        var third = Place(26);
        Add(first, source);
        Add(second, source);
        Add(third, source);

        RecheckCrowds();

        await Assert.That(first.Buffs.CheckBuff(grant.Id)).IsTrue();
        await Assert.That(second.Buffs.CheckBuff(grant.Id)).IsTrue();
        await Assert.That(third.Buffs.CheckBuff(grant.Id)).IsTrue();
        await Assert.That(first.Buffs.CheckBuff(source.Id)).IsFalse();
        await Assert.That(second.Buffs.CheckBuff(source.Id)).IsFalse();
        await Assert.That(third.Buffs.CheckBuff(source.Id)).IsFalse();
    }

    [Test]
    public async Task FishFormationNeedsFriendlyBearersWithTheSameSource()
    {
        Register(new BuffTemplate { Id = 6790 });
        var source = RegisterSource(6789, 6790, 3, 10, hostile: false);
        var holder = Place(24);
        Add(holder, source);
        Add(Place(25), source);
        var neighbor = Place(26);
        neighbor.Faction = new SystemFaction { Id = FactionsEnum.Hostile };
        Add(neighbor, source);
        RecheckCrowds();
        await Assert.That(holder.Buffs.CheckBuff(6790)).IsFalse();

        neighbor.Faction = holder.Faction;
        RecheckCrowds();
        await Assert.That(holder.Buffs.CheckBuff(6790)).IsTrue();
    }

    [Test]
    public async Task PermanentGrantIsRemovedAfterWorldTransferAndDetach()
    {
        Register(new BuffTemplate { Id = 4891 });
        var source = RegisterSource(4890, 4891, 2, 5);
        var holder = Place(24);
        var neighbor = Place(25);
        Add(holder, source);
        Add(neighbor, source);
        RecheckCrowds();
        await Assert.That(holder.Buffs.CheckBuff(4891)).IsTrue();

        // A transfer can temporarily leave the unit in the old region's object snapshot.
        neighbor.Transform.InstanceId = _otherWorld.Id;
        RecheckCrowds();
        await Assert.That(holder.Buffs.CheckBuff(4891)).IsFalse();
        await Assert.That(neighbor.Buffs.CheckBuff(4891)).IsFalse();

        neighbor.Transform.InstanceId = _world.Id;
        RecheckCrowds();
        await Assert.That(holder.Buffs.CheckBuff(4891)).IsTrue();
        holder.Buffs.CancelAllEffectTasks();
        await Assert.That(holder.Buffs.CheckBuff(4891)).IsFalse();
        await Assert.That(holder.Buffs.CheckBuff(4890)).IsTrue();
        RecheckCrowds();
        await Assert.That(neighbor.Buffs.CheckBuff(4891)).IsFalse();
    }

    [Test]
    public async Task AnExistingIndependentBuffSurvivesCrowdCleanup()
    {
        var grant = Register(new BuffTemplate { Id = 4891 });
        var source = RegisterSource(4890, 4891, 2, 5);
        var holder = Place(24);
        var neighbor = Place(25);
        var independent = Add(holder, grant);
        Add(holder, source);
        Add(neighbor, source);
        RecheckCrowds();
        await Assert.That(neighbor.Buffs.CheckBuff(4891)).IsTrue();

        neighbor.Buffs.RemoveBuff(source.Id);
        RecheckCrowds();
        await Assert.That(holder.Buffs.GetEffectFromBuffId(4891)).IsSameReferenceAs(independent);
        await Assert.That(neighbor.Buffs.CheckBuff(4891)).IsFalse();
    }

    [Test]
    public async Task TimedGrantKeepsItsOwnDurationWhenTheCrowdSplits()
    {
        Register(new BuffTemplate { Id = 20979, Duration = 600000, RequireBuffId = 20978 });
        var source = RegisterSource(20978, 20979, 2, 7, duration: 380000);
        var holder = Place(24);
        var neighbor = Place(25);
        Add(holder, source);
        Add(neighbor, source);
        RecheckCrowds();
        var grant = holder.Buffs.GetEffectFromBuffId(20979);
        await Assert.That(grant).IsNotNull();

        neighbor.Buffs.RemoveBuff(source.Id);
        RecheckCrowds();
        await Assert.That(holder.Buffs.GetEffectFromBuffId(20979)).IsSameReferenceAs(grant);
        await Assert.That(grant.Duration).IsEqualTo(600000);
    }

    private static void RecheckCrowds() => new CrowdBuffTask().Execute();

    private TestUnit Place(float x)
    {
        var unit = new TestUnit
        {
            ObjId = (uint)_units.Count + 1,
            Hp = 100,
            Faction = new SystemFaction { Id = FactionsEnum.Nuian },
            ParentWorld = _world
        };
        unit.Transform.Local.SetPosition(x, 24, 100);
        unit.Region = _world.GetRegionByPos(unit.Transform.World.Position);
        unit.Region.AddObject(unit);
        _world.AddObject(unit);
        _units.Add(unit);
        return unit;
    }

    private BuffTemplate RegisterSource(uint id, uint grant, uint count, float radius, bool hostile = true, int duration = 0) =>
        Register(new BuffTemplate
        {
            Id = id, CrowdBuffId = grant, CrowdNumber = count, CrowdRadius = radius, CrowdFriendly = true,
            CrowdHostile = hostile, Duration = duration
        });

    private BuffTemplate Register(BuffTemplate template)
    {
        _templates.Add(template.Id, template);
        return template;
    }

    private static Buff Add(Unit unit, BuffTemplate template)
    {
        var buff = new Buff(unit, unit, new SkillCasterUnit(unit.ObjId), template, null, DateTime.UtcNow);
        unit.Buffs.AddBuff(buff);
        return buff;
    }

    private void Install<T>(T instance) where T : class
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _singletons.Add((field, field.GetValue(null)));
        field.SetValue(null, instance);
    }

    private static void SetField<T>(T instance, string name, object value) =>
        typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private sealed class TestUnit : Unit
    {
        public override void BroadcastPacket(GamePacket packet, bool self)
        {
        }
    }
}
