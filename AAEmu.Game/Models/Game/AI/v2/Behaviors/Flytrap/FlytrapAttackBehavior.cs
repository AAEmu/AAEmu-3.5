using System.Numerics;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI;
using AAEmu.Game.Models.Game.AI.Utils;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.AI.v2.Params.Flytrap;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Flytrap;

public class FlytrapAttackBehavior : Behavior
{
    private FlytrapAiParams _aiParams;
    private bool _enter;

    public override void Enter()
    {
        Ai.Owner.InterruptSkills();
        Ai.Owner.CurrentGameStance = GameStanceType.Combat;
        Ai.Owner.CurrentAlertness = MoveTypeAlertness.Combat;
        Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, Ai.Owner.AnimActionId, false), false);

        if (Ai.Owner is { } npc)
        {
            npc.Events.OnCombatStarted(this, new OnCombatStartedArgs { Owner = npc, Target = npc });
        }
        Ai.Param = Ai.Owner.Template.AiParams;
        _enter = true;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!_enter)
            return; // not initialized yet Enter()

        Ai.Param ??= new FlytrapAiParams("");

        if (Ai.Param is not FlytrapAiParams aiParams)
            return;

        _aiParams = aiParams;

        if (!UpdateTarget())
        {
            Ai.OnNoAggroTarget();
            return;
        }

        if (Ai.Owner.CurrentTarget == null)
            return;

        if (Ai.Owner.Gimmick?.CurrentTarget != null)
            MoveInRange(Ai.Owner.Gimmick.CurrentTarget, delta);

        Ai.Owner.IsInBattle = true;
        var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);
        PickSkillAndUseIt(SkillUseConditionKind.InCombat, Ai.Owner.CurrentTarget, targetDist);

        Update();
    }

    public override void Exit()
    {
        _enter = false;
    }

    #region Gimmick
    private void MoveInRange(BaseUnit target, TimeSpan delta)
    {
        if (Ai?.Owner?.Gimmick == null)
            return;

        var gimmick = Ai.Owner.Gimmick;
        var gimmickPosition = Ai.Owner.Gimmick.Transform.World.Position;
        if (gimmick.Target == Vector3.Zero)
        {
            gimmick.Target = target.Transform.World.Position;
        }
        var finalPoint = gimmick.Target;

        var range = 0.1f;
        var moveDistance = gimmick.BaseMoveSpeed * (delta.Milliseconds / 1000.0f) + 1f;
        var moveDistanceZ = gimmick.Template.Gravity * (delta.Milliseconds / 1000.0f);
        var distanceToTarget = MathUtil.CalculateDistance(gimmickPosition, gimmick.Target, true);

        if (PathPlanningRules.UseNavPathPlanner)
        {
            var chaseGoal = target.Transform.World.Position;
            if (Ai.PathNode != null && target is Unit unitTarget)
            {
                if (CombatChaseRules.ShouldReplanPath(Ai.CombatApproachMoving, Ai.LastChasePlanUtc, Ai.LastChasePlanTarget, chaseGoal))
                {
                    Ai.Owner.FindPath(unitTarget);
                    Ai.LastChasePlanUtc = DateTime.UtcNow;
                    Ai.LastChasePlanTarget = chaseGoal;
                    gimmick.Target = chaseGoal;
                }
            }

            if (Ai.Owner.IsWalkerStuck())
            {
                gimmick.StopMovement();
                Ai.CombatApproachMoving = false;
                Ai.Owner.ResetWalkerProgress();
                if (Ai.PathNode is { FoundPath.Count: > 0 })
                    Ai.PathNode.FoundPath.Dequeue();
            }

            if (Ai.PathNode is { FoundPath.Count: > 0 } && !Ai.PathNode.FoundPath.Peek().Equals(Vector3.Zero))
            {
                var nextPathPoint = AiUtils.SnapPathPoint(Ai, Ai.PathNode.FoundPath.Peek());
                var distToWaypoint = MathUtil.CalculateDistance(gimmickPosition, nextPathPoint, true);
                if (distToWaypoint < CombatChaseRules.ChaseWaypointArrivalMetres)
                {
                    Ai.PathNode.FoundPath.Dequeue();
                    if (Ai.PathNode.FoundPath.TryPeek(out var peek) && !peek.Equals(Vector3.Zero))
                        nextPathPoint = AiUtils.SnapPathPoint(Ai, peek);
                }

                var distanceToPoint = MathUtil.CalculateDistance(gimmickPosition, nextPathPoint, true);
                if (distanceToPoint > range)
                {
                    gimmick.MoveTowards(nextPathPoint, moveDistance, moveDistanceZ);
                    Ai.CombatApproachMoving = true;
                }
                else if (Ai.PathNode.FoundPath.Count <= 1)
                {
                    Ai.PathNode.FoundPath.Dequeue();
                    gimmick.StopMovement();
                    Ai.CombatApproachMoving = false;
                }
                else
                {
                    Ai.PathNode.CurrentTargetPos = AiUtils.SnapPathPoint(Ai, Ai.PathNode.FoundPath.Dequeue());
                }
            }
            else
            {
                if (distanceToTarget > range)
                {
                    gimmick.MoveTowards(finalPoint, moveDistance, moveDistanceZ);
                    Ai.CombatApproachMoving = true;
                }
                else
                {
                    gimmick.StopMovement();
                    Ai.CombatApproachMoving = false;
                }
            }
        }
        else // we move straight to the final point
        {
            if (distanceToTarget > range)
                gimmick.MoveTowards(finalPoint, moveDistance, moveDistanceZ);
            else
                gimmick.StopMovement();
        }
    }

    private bool UpdateTarget()
    {
        if (Ai.Owner.TryFocusTaunt())
            return true;

        // We might want to optimize this somehow...
        var aggroList = Ai.Owner.AggroTable.Values;
        var abusers = aggroList.OrderByDescending(o => o.TotalAggro).Select(o => o.Owner).ToList();

        foreach (var abuser in abusers)
        {
            Ai.Owner.LookTowards(abuser.Transform.World.Position);
            if (Ai.AlreadyTargeted)
                return true;

            if (PathPlanningRules.UseNavPathPlanner)
            {
                // включена геодата и не основной мир
                // geodata enabled and not the main world
                if (Ai.Owner.UnitIsVisible(abuser) && !abuser.IsDead)
                {
                    Ai.Owner.CurrentAggroTarget = abuser;
                    Ai.Owner.SetTarget(abuser);
                    UpdateAggroHelp(abuser);
                    Ai.Owner.FindPath(abuser);
                    return true;
                }
            }
            else
            {
                if (Ai.Owner.UnitIsVisible(abuser) && !abuser.IsDead)
                {
                    Ai.Owner.CurrentAggroTarget = abuser;
                    Ai.Owner.SetTarget(abuser);
                    UpdateAggroHelp(abuser);
                    return true;
                }
            }
            Ai.Owner.ClearAggroOfUnit(abuser);
        }

        // Only remove CurrentTarget is either no unit selected, or if target is already dead
        if (Ai.Owner.CurrentTarget is not Unit currentTargetUnit)
            Ai.Owner.SetTarget(null);
        else if (currentTargetUnit.Hp <= 0 || currentTargetUnit.IsDead)
            Ai.Owner.SetTarget(null);

        return false;
    }
    #endregion

    public void Update()
    {
        var abuser = (Unit)Ai.Owner.CurrentTarget;
        var abuserPos = Ai.Owner.CurrentTarget.Transform.World.Position;
        var currentPos = Ai.Owner.Transform.World.Position;
        var idlePos = Ai.IdlePosition;
        // Check out of idle pos
        if (Ai.Param.AlwaysTeleportOnReturn && MathUtil.DistanceSqVectors(currentPos, idlePos) > 3 * 3)
        {
            // NpcTeleportTo(entity.AI.idlePos);
            Ai.Owner.ClearAggroOfUnit(abuser);
            Ai.OnNoAggroTarget();
            return;
        }

        // Check that some target was gone out from attack end distance
        if (MathUtil.DistanceSqVectors(abuserPos, idlePos) > _aiParams.AttackEndDistance * _aiParams.AttackEndDistance)
        {
            // entity.unit:NpcRemoveAggroOutOfRange(entity.AI.param.attackEndDistance);
            Ai.Owner.ClearAggroOfUnit(abuser);
            Ai.OnNoAggroTarget();
        }
    }
}
