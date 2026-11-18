using System.Numerics;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.AI;
using AAEmu.Game.Models.Game.AI.Utils;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class ReturnStateBehavior : BaseCombatBehavior
{
    private DateTime _timeoutTime;
    private bool _enter;
    private int _returnPlanAttempts;
    /// <summary>Straight run home ended short; wait for <see cref="ReturnMovementRules.ReturnTimeoutMs"/> (Zones <c>EndShortOfHome</c>).</summary>
    private bool _returnStoppedShort;

    public override void Enter()
    {
        // TODO : Autodisable
        Ai.Owner.ResetWalkerProgress();
        Ai.CombatApproachMoving = false;

        if (!Ai.Owner.AggroTable.IsEmpty)
            Ai.Owner.ClearAllAggro();

        Ai.Owner.SetTarget(null);
        // TODO: Ai.Owner.DisableAggro();

        Ai.Owner.IsInBattle = false;
        Ai.Owner.CurrentGameStance = GameStanceType.Relaxed;
        Ai.Owner.CurrentAlertness = MoveTypeAlertness.Idle;
        Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, Ai.Owner.AnimActionId, false), false);

        // Ai.AiPathPointsRemaining.Clear(); // Remove whatever path we're on
        // Ai.Owner.Simulation.TargetPosition = Vector3.Zero; // And reset expected target

        //var needRestorationOnReturn = true; // TODO: Use params & alertness values
        //if (needRestorationOnReturn)
        // StartSkill RETURN SKILL TYPE
        Ai.Owner.Buffs.AddBuff((uint)BuffConstants.NpcReturn, Ai.Owner);
        if (Ai.Param == null || Ai.Param.RestorationOnReturn)
        {
            Ai.Owner.PostUpdateCurrentHp(Ai.Owner, Ai.Owner.Hp, Ai.Owner.MaxHp, KillReason.Unknown);
            Ai.Owner.Hp = Ai.Owner.MaxHp;
            Ai.Owner.Mp = Ai.Owner.MaxMp;
            Ai.Owner.BroadcastPacket(new SCUnitPointsPacket(Ai.Owner.ObjId, Ai.Owner.Hp, Ai.Owner.Mp, Ai.Owner.HighAbilityRsc), true);
        }

        //var alwaysTeleportOnReturn = false; // TODO: get from params
        //if (alwaysTeleportOnReturn)
        if (Ai.Param is { AlwaysTeleportOnReturn: true })
        {
            OnCompletedReturn();
            return;
        }

        //var goReturnState = true; // TODO: get from params
        //if (!goReturnState)
        if (Ai.Param is { GoReturnState: false })
        {
            OnCompletedReturnNoTeleport();
            return;
        }

        var home = AiUtils.SnapPathPoint(Ai, Ai.HomePosition);
        if (ReturnMovementRules.ReturnStopsShort
            && MathUtil.CalculateDistance(home, Ai.Owner.Transform.World.Position, true) <= ReturnMovementRules.HomeRadiusMetres)
        {
            OnCompletedReturnNoTeleport();
            return;
        }

        _returnStoppedShort = false;
        if (!TryPlanReturnRoute()
            && PathPlanningRules.UseNavPathPlanner
            && !ReturnMovementRules.ReturnWithoutRouteEnabled)
        {
            OnCompletedReturn();
            return;
        }

        if (!Ai.Owner.ReturningOnPlannedRoute && ReturnMovementRules.ReturnWithoutRouteEnabled)
            _returnStoppedShort = true;

        _timeoutTime = DateTime.UtcNow.AddMilliseconds(ReturnMovementRules.ReturnTimeoutMs);
        _enter = true;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!_enter)
            return; // not initialized yet Enter()

        if (ReturnMovementRules.ReturnKeepsNewHostiles && CheckAggression())
            return;

        if (Ai.Owner.IsWalkerStuck())
        {
            Ai.Owner.StopMovement();
            Ai.Owner.ResetWalkerProgress();
            if (Ai.Owner.ReturningOnPlannedRoute && Ai.PathNode is { FoundPath.Count: > 0 })
            {
                Ai.PathNode.FoundPath.Dequeue();
                if (Ai.PathNode.FoundPath.Count == 0)
                    Ai.Owner.ReturningOnPlannedRoute = false;
            }
            else if (Ai.Owner.ReturningOnPlannedRoute)
            {
                Ai.Owner.ReturningOnPlannedRoute = false;
                Ai.PathNode?.FoundPath.Clear();
            }
            else
                _returnStoppedShort = true;

            return;
        }

        if (!_returnStoppedShort
            && !Ai.Owner.ReturningOnPlannedRoute
            && PathPlanningRules.UseNavPathPlanner
            && _returnPlanAttempts < ReturnMovementRules.ReturnPlanRetries)
            TryPlanReturnRoute();

        var moveSpeed = Ai.GetRealMovementSpeed(Ai.Owner.BaseMoveSpeed);
        var moveFlags = Ai.GetRealMovementFlags(moveSpeed);
        moveSpeed *= delta.Milliseconds / 1000.0;
        var home = AiUtils.SnapPathPoint(Ai, Ai.HomePosition);
        if (!Ai.Owner.ReturningOnPlannedRoute)
            home = ReturnStraightRules.ClipStraightReturn(Ai, Ai.Owner.Transform.World.Position, home);
        var stopRadius = ReturnMovementRules.ReturnStopsShort
            ? ReturnMovementRules.HomeRadiusMetres
            : 0.5f;

        var distanceToHome = MathUtil.CalculateDistance(home, Ai.Owner.Transform.World.Position, true);
        if (distanceToHome <= stopRadius)
        {
            OnCompletedReturnNoTeleport();
            return;
        }

        if (Ai.Owner.ReturningOnPlannedRoute && Ai.PathNode is { FoundPath.Count: > 0 })
        {
            var next = AiUtils.SnapPathPoint(Ai, Ai.PathNode.FoundPath.Peek());
            var distToNext = MathUtil.CalculateDistance(Ai.Owner.Transform.World.Position, next, true);
            if (distToNext < ReturnMovementRules.ReturnWaypointArrivalMetres)
            {
                Ai.PathNode.FoundPath.Dequeue();
                if (Ai.PathNode.FoundPath.Count > 0)
                    next = AiUtils.SnapPathPoint(Ai, Ai.PathNode.FoundPath.Peek());
                else
                    next = home;
            }

            Ai.Owner.MoveTowards(next, (float)moveSpeed, moveFlags);
        }
        else
            Ai.Owner.MoveTowards(home, (float)moveSpeed, moveFlags);

        if (DateTime.UtcNow > _timeoutTime)
            OnCompletedReturn();
    }

    private bool TryPlanReturnRoute()
    {
        Ai.Owner.ReturningOnPlannedRoute = false;
        Ai.PathNode?.FoundPath.Clear();

        if (!PathPlanningRules.UseNavPathPlanner || Ai.Owner.ParentWorld is not { } world || Ai.PathNode == null)
            return false;

        var from = Ai.Owner.Transform.World.Position;
        var home = AiUtils.SnapPathPoint(Ai, Ai.HomePosition);
        Ai.PathNode.ZoneKey = Ai.Owner.ParentWorld?.Template is { } tpl
            ? WorldManager.Instance.GetZoneId(tpl, from.X, from.Y)
            : Ai.Owner.Transform.ZoneId;
        var path = Ai.PathNode.FindPath(world, from, home);
        if (path.Count < 2)
        {
            _returnPlanAttempts++;
            return false;
        }

        _returnPlanAttempts = 0;
        _returnStoppedShort = false;
        Ai.Owner.ReturningOnPlannedRoute = true;
        Ai.PathNode.FoundPath = AiUtils.BuildPathQueue(Ai, world, path, home);
        if (Ai.PathNode.FoundPath.TryPeek(out var first)
            && MathUtil.CalculateDistance(from, first, true) < 0.5f)
            Ai.PathNode.FoundPath.Dequeue();
        return true;
    }

    private void OnCompletedReturn()
    {
        var destination = Ai.HomePosition;
        if (!ReturnMovementRules.TeleportHomeHeightEnabled && !Ai.Owner.CanFly)
            destination = destination with { Z = Ai.Owner.Transform.World.Position.Z };

        if (ReturnMovementRules.TeleportEndsWalkEnabled)
        {
            Ai.Owner.IsInPatrol = false;
            Ai.Owner.ReturningOnPlannedRoute = false;
        }

        var distanceHome = MathUtil.CalculateDistance(destination, Ai.Owner.Transform.World.Position, true);
        if (distanceHome > ReturnMovementRules.HomeRadiusMetres)
        {
            Ai.Owner.MoveTowards(destination, 1000000.0f);
            Ai.Owner.StopMovement();
        }
        else if (ReturnMovementRules.TeleportEndsWalkEnabled)
            Ai.Owner.StopMovement();

        OnCompletedReturnNoTeleport();
    }

    public void OnCompletedReturnNoTeleport()
    {
        var here = AiUtils.SnapWalkTarget(Ai, Ai.Owner.Transform.World.Position);
        var flatHome = MathUtil.CalculateDistance(
            new Vector3(Ai.HomePosition.X, Ai.HomePosition.Y, here.Z), here, true);
        if (flatHome <= ReturnMovementRules.HomeRadiusMetres * 2f)
        {
            Ai.HomePosition = Ai.HomePosition with { Z = here.Z };
            Ai.IdlePosition = here;
        }

        if (ReturnMovementRules.ReturnKeepsNewHostiles && !Ai.Owner.AggroTable.IsEmpty)
        {
            var top = Ai.Owner.AggroTable.Values
                .OrderByDescending(o => o.TotalAggro)
                .Select(o => o.Owner)
                .FirstOrDefault(o => o is Unit { Hp: > 0, IsDead: false });
            if (top is Unit reserved)
            {
                Ai.Owner.SetTarget(reserved);
                Ai.GoToCombat();
                return;
            }

            Ai.Owner.ClearAllAggro();
        }

        if (Ai.PathHandler.HasPathMovementData() && PathWalkerRules.ShouldEnterFollowPath(Ai.Owner))
        {
            Ai.GoToDefaultBehavior();
            return;
        }

        Ai.GoToIdle();
    }

    public override void Exit()
    {
        // TODO: Ai.Owner.EnableAggro();
        Ai.Owner.ReturningOnPlannedRoute = false;
        Ai.PathNode?.FoundPath.Clear();
        Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, Ai.Owner.AnimActionId, true), false);
        Ai.Owner.Buffs.RemoveBuff((uint)BuffConstants.NpcReturn);
        _returnStoppedShort = false;
        _enter = false;
    }
}