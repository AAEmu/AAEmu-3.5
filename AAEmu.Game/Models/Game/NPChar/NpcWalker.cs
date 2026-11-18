using System.Numerics;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Physics.Static;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.NPChar;

public partial class Npc
{
    private DateTime? _walkerBlockedSinceUtc;

    /// <summary>Высота «невидимого пола», если юнит привязан к ней при спавне.</summary>
    public float? UnseenFloorZ { get; private set; }

    /// <summary>Позиция из будущего <c>WZNpcState</c> (World relay); для <see cref="WorldSpawnHeightRules"/>.</summary>
    public Vector3? WorldStreamPosition { get; set; }

    /// <summary>Домой по A* после боя: Zones не режет шаг стеной на planned return (<c>!_legHomeStraight</c>).</summary>
    public bool ReturningOnPlannedRoute { get; set; }

    internal void BindUnseenFloor(float level) => UnseenFloorZ = level;

    /// <summary>
    /// Обрезка шага стеной для roam, straight return-home, combat chase (Zones <c>WalkerClip</c>); не патруль, не planned return.
    /// </summary>
    public bool AppliesWalkerCollision() =>
        !CanFly && WalkerCollision.Enabled && !IsInPatrol && !ReturningOnPlannedRoute;

    /// <summary>Последний шаг укоротила стена (<see cref="WalkerCollision"/>).</summary>
    public bool LastWalkerStepBlocked { get; private set; }

    /// <summary>Горизонтальное смещение последнего успешного <see cref="MoveTowards"/> (м).</summary>
    public float LastWalkerStepMeters { get; private set; }

    public void ResetWalkerProgress()
    {
        _walkerBlockedSinceUtc = null;
        LastWalkerStepBlocked = false;
        LastWalkerStepMeters = 0f;
    }

    /// <summary>Высота шага по слою P4; иначе навмеш GeoData или текущие ноги.</summary>
    public float ResolveWalkHeight(float x, float y)
    {
        var footZ = Transform.Local.Position.Z;
        if (Ai != null)
        {
            var zoneKey = ParentWorld?.Template is { } template
                ? WorldManager.Instance.GetZoneId(template, x, y)
                : Transform.ZoneId;
            var ground = WorldManager.Instance.GetReferenceHeight(Ai, x, y, footZ, zoneKey);
            return NpcFootingRules.KeepUnseenFloor(UnseenFloorZ, footZ, ground) ?? ground;
        }
        var geo = ParentWorld?.Template?.GeoData;
        if (geo != null)
        {
            try
            {
                var h = geo.GetHeight(new Vector3(x, y, footZ));
                if (NpcGroundResolver.IsUsableHeight(h))
                    return h;
            }
            catch
            {
                // Fallback to feet.
            }
        }

        return footZ;
    }

    /// <summary>Конечная точка импульса (knockback, blink): P4 в пределах 5 м от исходного Z.</summary>
    public Vector3 SnapDisplacementPosition(Vector3 end)
    {
        var groundZ = ResolveWalkHeight(end.X, end.Y);
        if (NpcGroundResolver.IsUsableHeight(groundZ) && MathF.Abs(end.Z - groundZ) < 5f)
            return new Vector3(end.X, end.Y, groundZ);
        return end;
    }

    /// <summary>Горизонтальный шаг skill controller / dash: ClipWalk + P4, как <see cref="MoveTowards"/>.</summary>
    public Vector3 StepAlongGround(Vector3 current, Vector3 target, float travelDist, float targetDist, out bool blockedByWall)
    {
        var (newX, newY, _) = World.Transform.PositionAndRotation.AddDistanceToFront(travelDist, targetDist, current, target);
        blockedByWall = false;
        if (AppliesWalkerCollision() && WorldManager.Instance.NpcGroundResolver is { } walls && ParentWorld?.Template is { } template)
        {
            var clipped = walls.ClipWalk(
                template,
                Transform.ZoneId,
                current,
                new Vector3(newX, newY, current.Z),
                WalkerCollision.Radius(ModelSize),
                _walkerProbe ??= new WalkerCollision.Probe(),
                out blockedByWall);
            if (blockedByWall)
            {
                newX = clipped.X;
                newY = clipped.Y;
            }
        }

        var groundZ = ResolveWalkHeight(newX, newY);
        if (!NpcGroundResolver.IsUsableHeight(groundZ))
            groundZ = current.Z;
        return new Vector3(newX, newY, groundZ);
    }

    /// <summary>
    /// Roam/return leg с упором в стену или без смещения дольше <see cref="WalkerCollision.StuckEndMs"/> — по правилам зоны.
    /// </summary>
    public bool IsWalkerStuck()
    {
        // Погоня режется правилами боя (дистанция, CombatApproachMoving), а не хвостом стены: заморозка посреди
        // погони оставила бы юнита стоять с целью в агро. Её путь сокращает BaseCombatBehavior.
        if (Ai?.CombatApproachMoving == true)
            return false;
        if (!_walkerBlockedSinceUtc.HasValue)
            return false;
        if ((DateTime.UtcNow - _walkerBlockedSinceUtc.Value).TotalMilliseconds < WalkerCollision.StuckEndMs)
            return false;
        // Patrol / planned return: no P6 clip, stuck = нет прогресса (Zones follow_path / routed run-home).
        if (IsInPatrol || ReturningOnPlannedRoute)
            return LastWalkerStepMeters < WalkerProgressMinMetres;
        return AppliesWalkerCollision();
    }

    internal const float WalkerProgressMinMetres = 0.02f;

    /// <summary>Не слать полный run-vel клиенту при шаге без прогресса (Zones velocity ∝ motion).</summary>
    internal static float MovementAnimSpeedQuant(float horizontalStep, bool blockedByWall)
    {
        if (horizontalStep < WalkerProgressMinMetres)
            return 0f;
        if (blockedByWall && horizontalStep < WalkerCollision.StuckSpeed * 0.1f)
            return 0f;
        return 4000f;
    }

    internal void RecordWalkerStep(bool blockedByWall, float horizontalMeters, float attemptedMeters)
    {
        LastWalkerStepBlocked = blockedByWall;
        LastWalkerStepMeters = horizontalMeters;
        // ~100 ms AI/sim tick: при упоре в стену зачёт только если сдвиг ≥ StuckSpeed·Δt (Zones WalkerCollision).
        const float StepSeconds = 0.1f;
        var minProgress = blockedByWall
            ? WalkerCollision.StuckSpeed * StepSeconds
            : WalkerProgressMinMetres;
        if (horizontalMeters >= minProgress)
        {
            _walkerBlockedSinceUtc = null;
            return;
        }

        if (!blockedByWall && attemptedMeters < 0.01f)
            return;

        // Patrol / planned return: no wall clip, negligible progress → stuck timer.
        if ((IsInPatrol || ReturningOnPlannedRoute) && !blockedByWall && horizontalMeters < minProgress)
        {
            _walkerBlockedSinceUtc ??= DateTime.UtcNow;
            return;
        }

        _walkerBlockedSinceUtc ??= DateTime.UtcNow;
    }
}