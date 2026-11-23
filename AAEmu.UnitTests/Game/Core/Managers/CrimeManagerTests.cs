using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crime;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class CrimeManagerTests
{
    [Test]
    public async Task CrimeReductionSkillClampsCurrentPointsAndInfamyIndependently()
    {
        _criminal.CrimePoint = 40;
        _criminal.CrimeRecord = 150;

        new GiveCrimePoint().Execute(_criminal, null, _criminal, null, null, null, null, DateTime.UtcNow, -100, 0, 0, 0);

        await Assert.That(_criminal.CrimePoint).IsEqualTo((short)0);
        await Assert.That(_criminal.CrimeRecord).IsEqualTo(50);
    }

    [Test]
    public async Task CrimeGrantSkillDoesNotOverflowCurrentPointsOrInfamy()
    {
        var skills = new SkillManager(null, null);
        SetField(skills, "_buffs", new Dictionary<uint, BuffTemplate>());
        Install(skills);
        _criminal.Faction = new SystemFaction { Id = FactionsEnum.Pirate };
        _criminal.CrimePoint = short.MaxValue - 30;
        _criminal.CrimeRecord = int.MaxValue - 30;

        new GiveCrimePoint().Execute(_criminal, null, _criminal, null, null, null, null, DateTime.UtcNow, 100, 0, 0, 0);

        await Assert.That(_criminal.CrimePoint).IsEqualTo(short.MaxValue);
        await Assert.That(_criminal.CrimeRecord).IsEqualTo(int.MaxValue);
    }

    private const uint EvidencePhase = 974;
    private const uint ReportedPhase = 1043;
    private readonly DoodadFuncEvidenceItemLoot _evidenceFunc = new() { Id = 1, SkillId = 11672, CrimeKindId = 3, CrimeValue = 10 };
    private readonly List<(FieldInfo Field, object Previous)> _singletons = [];
    private CrimeManager _manager;
    private WorldInstance _world;
    private Character _reporter;
    private Character _criminal;
    private Doodad _evidence;

    [Before(Test)]
    public void SetUp()
    {
        var doodads = new DoodadManager(null, null, null, null, null);
        Install(doodads);
        SetField(doodads, "_funcsByGroups", new Dictionary<uint, List<DoodadFunc>>
        {
            [EvidencePhase] =
            [
                new DoodadFunc { FuncId = 1, FuncType = nameof(DoodadFuncEvidenceItemLoot), NextPhase = (int)ReportedPhase }
            ]
        });
        SetField(doodads, "_phaseFuncs", new Dictionary<uint, List<DoodadPhaseFunc>>());
        SetField(doodads, "_funcTemplates", new Dictionary<string, Dictionary<uint, DoodadFuncTemplate>>
        {
            [nameof(DoodadFuncEvidenceItemLoot)] = new() { [1] = _evidenceFunc }
        });
        var models = new ModelManager();
        SetField(models, "_modelTypes", new Dictionary<uint, ModelType>());
        Install(models);

        _reporter = new Character(null) { Id = 10, ObjId = 100 };
        _world = new WorldInstance(new WorldTemplate(), 0, true, _reporter.Transform.InstanceId);
        _reporter.ParentWorld = _world;
        _criminal = new Character(null) { Id = 20, ObjId = 200, ParentWorld = _world };
        SetField(_criminal, "_isOnline", true);
        _evidence = new TestEvidence
        {
            ObjId = 300, ParentWorld = _world, IsVisible = true, OwnerType = DoodadOwnerType.Character,
            OwnerId = _criminal.Id, Data = 30, FuncGroupId = EvidencePhase
        };
        _world.AddObject(_evidence);

        var world = Mock.Of<IWorldManager>();
        world.GetCharacterById(_criminal.Id).Returns(_criminal);
        var zones = Mock.Of<IZoneManager>();
        zones.GetZoneByKey(_evidence.Transform.ZoneId).Returns((Zone)null);
        _manager = new CrimeManager(world.Object, new ReportIds(), zones.Object, doodads);
    }

    [After(Test)]
    public void TearDown()
    {
        _world.RemoveObject(_evidence);
        foreach (var (field, previous) in _singletons)
            field.SetValue(null, previous);
    }

    [Test]
    public async Task AReportUsesTheEvidenceValuesAndKeepsTheSubmittedMessage()
    {
        var report = _manager.ReportCrime(_reporter, _evidence, 4, 5, 6, "testimony");
        var duplicate = _manager.ReportCrime(_reporter, _evidence, 44, 55, 66, "duplicate");

        await Assert.That(report.Msg).IsEqualTo("testimony");
        await Assert.That(report.Reporter).IsEqualTo(_reporter.Id);
        await Assert.That(report.Victim).IsEqualTo(30u);
        await Assert.That(report.Arg1).IsEqualTo(4u);
        await Assert.That(report.Arg2).IsEqualTo(5u);
        await Assert.That(report.Arg3).IsEqualTo(6u);
        await Assert.That(report.CrimeKind).IsEqualTo(CrimeKind.Murder);
        await Assert.That(_criminal.CrimePoint).IsEqualTo((short)10);
        await Assert.That(_evidence.FuncGroupId).IsEqualTo(ReportedPhase);
        await Assert.That(duplicate).IsNull();
        await Assert.That(_manager.GetCrimesOfPlayer(_criminal.Id).Count).IsEqualTo(1);
    }

    [Test]
    public async Task OutOfRangeReportLeavesTheEvidenceAvailable()
    {
        _reporter.Transform.Local.SetPosition(20, 0, 0);

        var report = _manager.ReportCrime(_reporter, _evidence, 0, 0, 0, "too far");

        await Assert.That(report).IsNull();
        await Assert.That(_evidence.FuncGroupId).IsEqualTo(EvidencePhase);
        await Assert.That(_criminal.CrimePoint).IsEqualTo((short)0);

        _reporter.Transform.Local.SetPosition(0, 0, 0);
        var nearbyReport = _manager.ReportCrime(_reporter, _evidence, 0, 0, 0, "nearby");
        await Assert.That(nearbyReport).IsNotNull();
    }

    [Test]
    public async Task RejectsEvidenceAfterItsCurrentWorldOrReportPhaseChanges()
    {
        var otherWorld = new WorldInstance(new WorldTemplate(), 0, true, _reporter.Transform.InstanceId);
        _reporter.ParentWorld = otherWorld;
        var wrongWorldReport = _manager.ReportCrime(_reporter, _evidence, 0, 0, 0, "wrong world");
        _reporter.ParentWorld = _world;
        _evidence.FuncGroupId = ReportedPhase;
        var staleReport = _manager.ReportCrime(_reporter, _evidence, 0, 0, 0, "wrong phase");

        await Assert.That(wrongWorldReport).IsNull();
        await Assert.That(staleReport).IsNull();
        await Assert.That(_manager.GetCrimesOfPlayer(_criminal.Id).Count).IsEqualTo(0);
    }

    [Test]
    public async Task RejectsEvidenceWithoutACharacterOwner()
    {
        _evidence.OwnerType = DoodadOwnerType.System;
        var report = _manager.ReportCrime(_reporter, _evidence, 0, 0, 0, "system object");

        await Assert.That(report).IsNull();
        await Assert.That(_evidence.FuncGroupId).IsEqualTo(EvidencePhase);
        await Assert.That(_criminal.CrimePoint).IsEqualTo((short)0);
    }

    [Test]
    public async Task AuthoredZeroPointPhaseStillAllowsTheReport()
    {
        _evidenceFunc.CrimeKindId = 0;
        _evidenceFunc.CrimeValue = 0;

        var report = _manager.ReportCrime(_reporter, _evidence, 0, 0, 0, "tampered evidence");

        await Assert.That(report.CrimeKind).IsEqualTo(CrimeKind.None);
        await Assert.That(_criminal.CrimePoint).IsEqualTo((short)0);
        await Assert.That(_evidence.FuncGroupId).IsEqualTo(ReportedPhase);
    }

    [Test]
    public async Task OnlyOneConcurrentReportConsumesTheEvidence()
    {
        var otherReporter = new Character(null) { Id = 11, ObjId = 101, ParentWorld = _world };
        var firstReport = Task.Run(() => _manager.ReportCrime(_reporter, _evidence, 0, 0, 0, "first"));
        var otherReport = Task.Run(() => _manager.ReportCrime(otherReporter, _evidence, 0, 0, 0, "second"));

        var reports = await Task.WhenAll(firstReport, otherReport);
        var reportCount = (reports[0] != null ? 1 : 0) + (reports[1] != null ? 1 : 0);

        await Assert.That(reportCount).IsEqualTo(1);
        await Assert.That(_criminal.CrimePoint).IsEqualTo((short)10);
        await Assert.That(_manager.GetCrimesOfPlayer(_criminal.Id).Count).IsEqualTo(1);
    }

    private static void SetField<T>(T owner, string field, object value) =>
        typeof(T).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);

    private void Install<T>(T manager) where T : class
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _singletons.Add((field, field.GetValue(null)));
        field.SetValue(null, manager);
    }

    private sealed class TestEvidence : Doodad
    {
        public override void BroadcastPacket(GamePacket packet, bool self) { }
    }

    private sealed class ReportIds : ICrimeIdManager
    {
        private uint _lastId;

        public uint GetNextId() => Interlocked.Increment(ref _lastId);
        public bool Initialize(bool forceReset = false) => throw new NotSupportedException();
        public void Load() => throw new NotSupportedException();
        public uint[] GetNextId(int count) => throw new NotSupportedException();
        public void ReleaseId(uint usedObjectId) => throw new NotSupportedException();
        public void ReleaseId(IEnumerable<uint> usedObjectIds) => throw new NotSupportedException();
    }
}
