using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public sealed class PlotCastingUseableTests
{
    [Test]
    [Arguments(0L, 0, 39, 0)]
    [Arguments(1_599L, 0, 39, 39)]
    [Arguments(1_600L, 40, 69, 40)]
    [Arguments(2_799L, 40, 69, 69)]
    [Arguments(2_800L, 70, 99, 70)]
    [Arguments(3_999L, 70, 99, 99)]
    public async Task ReleaseFreezesTheInclusiveBandAtTheRequest(long elapsed, int min, int max, int progress)
    {
        var (caster, skill, state, exit) = CreateCast();
        state.ArmCastingUse(exit, 4_000, 1_000);

        await Assert.That(state.TryRequestCastingUse(skill.TlId, 1_000 + elapsed)).IsTrue();
        await Assert.That(state.TryRequestCastingUse(skill.TlId, 4_999)).IsFalse();
        await Assert.That(state.TryCompleteCasting(exit, true, out var captured)).IsTrue();
        state.CastingUsePercent = captured;

        await Assert.That(captured).IsEqualTo(progress);
        await Assert.That(Band(caster, skill, min, max)).IsTrue();
        await Assert.That(Band(caster, skill, 100, 100)).IsFalse();
    }

    [Test]
    public async Task FullOrZeroDurationCastReachesOnlyTheFullChargeBand()
    {
        var (caster, skill, state, exit) = CreateCast();
        state.ArmCastingUse(exit, 4_000, 1_000);
        await Assert.That(state.TryRequestCastingUse(skill.TlId, 5_000)).IsFalse();
        await Assert.That(state.TryCompleteCasting(exit, true, out var full)).IsTrue();
        state.CastingUsePercent = full;

        await Assert.That(full).IsEqualTo(100);
        await Assert.That(Band(caster, skill, 100, 100)).IsTrue();
        await Assert.That(Band(caster, skill, 0, 99)).IsFalse();

        state.ArmCastingUse(exit, 0, 5_000);
        await Assert.That(state.TryRequestCastingUse(skill.TlId, 5_000)).IsFalse();
        await Assert.That(state.TryCompleteCasting(exit, true, out var instant)).IsTrue();
        await Assert.That(instant).IsEqualTo(100);
    }

    [Test]
    public async Task CannotReleaseAnotherTimelineNonReleasableEdgeOrCancelledCast()
    {
        var (_, skill, state, exit) = CreateCast();
        state.ArmCastingUse(exit, 4_000, 1_000);

        await Assert.That(state.TryRequestCastingUse(8, 2_000)).IsFalse();
        var otherExit = new PlotNode { ParentNextEvent = new PlotNextEvent { Casting = true, CastingUseable = true } };
        await Assert.That(state.TryCompleteCasting(otherExit, false, out _)).IsFalse();
        state.RequestCancellation();
        await Assert.That(state.TryRequestCastingUse(skill.TlId, 2_000)).IsFalse();

        var (_, otherSkill, otherState, nonReleasableExit) = CreateCast();
        nonReleasableExit.ParentNextEvent.CastingUseable = false;
        otherState.ArmCastingUse(nonReleasableExit, 4_000, 1_000);
        await Assert.That(otherState.TryRequestCastingUse(otherSkill.TlId, 2_000)).IsFalse();
        await Assert.That(otherState.TryCompleteCasting(nonReleasableExit, true, out var full)).IsTrue();
        await Assert.That(full).IsEqualTo(100);
    }

    [Test]
    public async Task AConditionWithoutACapturedCastCannotBorrowAnotherPlotsProgress()
    {
        var (caster, skill, state, _) = CreateCast();
        var moreRecent = new Skill(new SkillTemplate { Id = 36635, CastingUseable = true });
        caster.ActivePlotState = new PlotState(caster, null, caster, null, null, moreRecent) { CastingUsePercent = 50 };

        await Assert.That(Band(caster, skill, 0, 100)).IsFalse();
        skill.ActivePlotState = null;
        await Assert.That(Band(caster, skill, 0, 100)).IsFalse();
        state.CastingUsePercent = 50;
        skill.ActivePlotState = state;
        await Assert.That(Band(caster, skill, 40, 69)).IsTrue();
    }

    [Test]
    public async Task NativePacketUsesUnitThenSkillAndPlotTimelines()
    {
        var (caster, skill, state, exit) = CreateCast();
        var packet = new CSFireCastingSkillPacket { Connection = new GameConnection(null) { ActiveChar = caster } };
        state.ArmCastingUse(exit, 60_000, Environment.TickCount64);
        var foreign = new PacketStream().WriteBc(caster.ObjId + 1).Write((ushort)99).Write(skill.TlId);
        foreign.Rollback();
        packet.Read(foreign);
        await Assert.That(state.TryCompleteCasting(exit, false, out _)).IsFalse();

        var request = new PacketStream().WriteBc(caster.ObjId).Write((ushort)99).Write(skill.TlId);
        request.Rollback();
        packet.Read(request);

        await Assert.That(packet.TypeId).IsEqualTo((ushort)0x119);
        await Assert.That(request.Pos).IsEqualTo(request.Count);
        await Assert.That(state.TryCompleteCasting(exit, false, out var percent)).IsTrue();
        await Assert.That(percent).IsBetween(0, 99);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CastProgressPassesThroughAnIntermediateEventToTheCorrectBand(bool releaseEarly)
    {
        var caster = new PlotTestUnit { ObjId = 1, Hp = 100 };
        var skill = new Skill(new SkillTemplate { Id = 34120, CastingUseable = true }) { TlId = SkillTlIdManager.GetNextId(caster) };
        var state = new PlotState(caster, new SkillCasterUnit(caster.ObjId), caster,
            new SkillCastUnitTarget(caster.ObjId), null, skill);
        caster.ActivePlotState = state;
        skill.ActivePlotState = state;

        var root = CreateNode(1);
        var castExit = Connect(root, 2, new PlotNextEvent { Casting = true, CastingUseable = true, Delay = releaseEarly ? 4_000 : 0 });
        var full = Connect(castExit, 3, new PlotNextEvent());
        full.Event.Conditions.AddLast(CreateBand(100, 100));
        var fullSuccess = Connect(full, 4, new PlotNextEvent());
        var early = Connect(full, 5, new PlotNextEvent { Fail = true });
        early.Event.Conditions.AddLast(CreateBand(0, 99));
        var earlySuccess = Connect(early, 6, new PlotNextEvent());

        var releaseRequested = false;
        if (releaseEarly)
        {
            caster.OnBroadcast = packet =>
            {
                if (packet is not CompressedGamePackets)
                    return;
                caster.OnBroadcast = null;
                releaseRequested = state.TryRequestCastingUse(skill.TlId, Environment.TickCount64);
            };
        }

        await new PlotTree(2559) { RootNode = root }.ExecuteAsync(state).WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(releaseRequested).IsEqualTo(releaseEarly);
        await Assert.That(state.Tickets.ContainsKey(fullSuccess.Event.Id)).IsEqualTo(!releaseEarly);
        await Assert.That(state.Tickets.ContainsKey(earlySuccess.Event.Id)).IsEqualTo(releaseEarly);
        await Assert.That(caster.ActivePlotState).IsNull();
    }

    private static PlotEventCondition CreateBand(int min, int max) => new()
    {
        SourceId = PlotEffectSource.OriginalSource,
        TargetId = PlotEffectTarget.OriginalSource,
        Condition = new PlotCondition { Kind = PlotConditionType.CastingUseable, Param1 = min, Param2 = max }
    };

    private static PlotNode CreateNode(uint id) => new()
    {
        Event = new PlotEventTemplate
        {
            Id = id,
            SourceUpdateMethodId = (uint)PlotSourceUpdateMethodType.OriginalSource,
            TargetUpdateMethodId = (uint)PlotTargetUpdateMethodType.OriginalSource
        }
    };

    private static PlotNode Connect(PlotNode parent, uint id, PlotNextEvent edge)
    {
        var child = CreateNode(id);
        child.Parent = parent;
        child.ParentNextEvent = edge;
        edge.Event = child.Event;
        parent.Children.Add(child);
        parent.Event.NextEvents.AddLast(edge);
        return child;
    }

    private sealed class PlotTestUnit : Unit
    {
        public Action<GamePacket> OnBroadcast { get; set; }

        public override void BroadcastPacket(GamePacket packet, bool self) => OnBroadcast?.Invoke(packet);
    }

    private static (Character Caster, Skill Skill, PlotState State, PlotNode Exit) CreateCast()
    {
        var caster = new Character(null) { Id = 1, ObjId = 1 };
        var skill = new Skill(new SkillTemplate { Id = 34120, CastingUseable = true }) { TlId = 7 };
        var state = new PlotState(caster, null, caster, null, null, skill);
        caster.ActivePlotState = state;
        skill.ActivePlotState = state;
        var exit = new PlotNode { ParentNextEvent = new PlotNextEvent { Casting = true, CastingUseable = true } };
        return (caster, skill, state, exit);
    }

    private static bool Band(Character caster, Skill skill, int min, int max) =>
        new PlotCondition { Kind = PlotConditionType.CastingUseable, Param1 = min, Param2 = max }
            .Check(caster, null, caster, null, null, skill);
}
