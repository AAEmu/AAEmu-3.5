using System.Numerics;

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI;
using AAEmu.Game.Models.Game.AI.Utils;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class RoamingBehavior : BaseCombatBehavior
{
    private Vector3 _targetRoamPosition = Vector3.Zero;
    private readonly List<Vector3> _roamLeg = [];
    private int _roamLegIndex;
    private DateTime _nextRoaming;
    private DateTime? _roamLegCutUtc;
    private int? _pendingNextLegDelayMs;
    private bool _enter;
    private bool _roamAnimStarted;

    public override void Enter()
    {
        Ai.Owner.InterruptSkills();
        Ai.Owner.ResetWalkerProgress();
        //Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, BaseUnitType.Npc, ModelPostureType.ActorModelState, Ai.Owner.Template.AnimActionId, false), false); // fixed animated
        //UpdateRoaming();
        Ai.Owner.CurrentGameStance = GameStanceType.Relaxed;
        Ai.Owner.CurrentAlertness = MoveTypeAlertness.Idle;
        _enter = true;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!_enter)
            return; // not initialized yet Enter()

        if (RoamLeashRules.ShouldReturnHome(Ai))
        {
            EndRoamLeg(idlePosture: false, pauseSeconds: 0);
            Ai.GoToReturn();
            return;
        }

        if (!CheckAggression())
            CheckAlert();

        if (_targetRoamPosition.Equals(Vector3.Zero) && DateTime.UtcNow > _nextRoaming)
        {
            UpdateRoaming();
            _roamAnimStarted = false;
        }

        if (_targetRoamPosition.Equals(Vector3.Zero))
            return;

        if (_roamLegCutUtc is { } cutAt && DateTime.UtcNow >= cutAt)
        {
            EndRoamLeg(idlePosture: true, pauseSeconds: 0, usePipeDelay: true);
            return;
        }

        if (Ai.Owner.IsWalkerStuck())
        {
            EndRoamLeg(idlePosture: true, pauseSeconds: Random.Shared.Next(1, 4));
            return;
        }

        var legTarget = CurrentRoamStepTarget();
        var moveSpeed = Ai.GetRealMovementSpeed(Ai.Owner.BaseMoveSpeed);
        var moveFlags = Ai.GetRealMovementFlags(moveSpeed);
        moveSpeed *= delta.Milliseconds / 1000.0;
        Ai.Owner.MoveTowards(legTarget, (float)moveSpeed, moveFlags);
        if (!_roamAnimStarted && Ai.Owner.LastWalkerStepMeters >= 0.02f)
        {
            _roamAnimStarted = true;
            Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, Ai.Owner.AnimActionId, false), false);
        }

        var here = Ai.Owner.Transform.World.Position;
        var stepArrival = MathF.Max(Ai.Owner.Template.Scale, RoamMovementRules.RoamWaypointArrivalMetres);
        if (MathUtil.CalculateDistance(here, legTarget) < stepArrival)
            AdvanceRoamLeg();

        var goalArrival = MathF.Max(Ai.Owner.Template.Scale, RoamMovementRules.RoamGoalArrivalMetres);
        if (_roamLegIndex >= _roamLeg.Count
            && MathUtil.CalculateDistance(here, _targetRoamPosition) < goalArrival)
            EndRoamLeg(idlePosture: true, pauseSeconds: 0, usePipeDelay: RoamPipeRules.RoamAiTicksEnabled);
    }

    public override void Exit()
    {
        _enter = false;
        Ai.Owner.ResetWalkerProgress();
    }

    private void EndRoamLeg(bool idlePosture, int pauseSeconds, bool usePipeDelay = false)
    {
        Ai.Owner.StopMovement();
        Ai.Owner.ResetWalkerProgress();
        _targetRoamPosition = Vector3.Zero;
        _roamLeg.Clear();
        _roamLegIndex = 0;
        _roamAnimStarted = false;
        _roamLegCutUtc = null;
        if (usePipeDelay && _pendingNextLegDelayMs is { } pipeMs)
            _nextRoaming = DateTime.UtcNow.AddMilliseconds(pipeMs);
        else if (pauseSeconds > 0)
            _nextRoaming = DateTime.UtcNow.AddSeconds(pauseSeconds);
        else
            _nextRoaming = DateTime.UtcNow.AddSeconds(Random.Shared.Next(3, 6));
        _pendingNextLegDelayMs = null;
        if (idlePosture)
        {
            var pos = Ai.Owner.Transform.World.Position;
            var snapped = AiUtils.SnapWalkTarget(Ai, pos);
            if (MathF.Abs(snapped.Z - pos.Z) > 0.02f)
            {
                Ai.Owner.Transform.Local.SetPosition(pos.X, pos.Y, snapped.Z);
                Ai.Owner.StopMovement();
            }

            Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, Ai.Owner.AnimActionId, true), false);
        }
    }

    private void UpdateRoaming()
    {
        if (!RoamEligibilityRules.ShouldRoam(Ai.Owner))
        {
            _nextRoaming = DateTime.UtcNow.AddSeconds(30);
            return;
        }

        if (!AiUtils.TryPlanRoamLeg(Ai, out var goal, out var leg))
        {
            _nextRoaming = DateTime.UtcNow.AddSeconds(Random.Shared.Next(2, 5));
            return;
        }

        _targetRoamPosition = goal;
        _roamLeg.Clear();
        for (var i = 1; i < leg.Count; i++)
            _roamLeg.Add(leg[i]);
        if (_roamLeg.Count == 0)
            _roamLeg.Add(goal);
        _roamLegIndex = 0;

        if (RoamPipeRules.RoamAiTicksEnabled)
        {
            var (min, _) = RoamTargetRules.ResolveRange(Ai);
            var tickMs = RoamPipeRules.AiTickMsForNpc(Ai.Owner);
            var (cut, next) = RoamPipeRules.RoamPipeTiming(Random.Shared, tickMs, min);
            _roamLegCutUtc = DateTime.UtcNow.AddMilliseconds(cut);
            _pendingNextLegDelayMs = next;
        }
    }

    private Vector3 CurrentRoamStepTarget() =>
        _roamLegIndex < _roamLeg.Count ? _roamLeg[_roamLegIndex] : _targetRoamPosition;

    private void AdvanceRoamLeg()
    {
        if (_roamLegIndex < _roamLeg.Count)
        {
            _roamLegIndex++;
            Ai.Owner.ResetWalkerProgress();
        }
    }
}
