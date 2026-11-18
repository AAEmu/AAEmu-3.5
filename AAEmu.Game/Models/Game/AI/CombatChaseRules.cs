using System.Numerics;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI;

/// <summary>
/// Погоня в бою — порт <c>CombatRules.ChaseReplan*</c> из ArcheageZones.
/// Перепланирование A* не чаще раза в <see cref="ChaseReplanMs"/> и только если цель ушла дальше
/// <see cref="ChaseReplanMetres"/> от точки последнего плана.
/// </summary>
public static class CombatChaseRules
{
    public const long ChaseReplanMs = 500;
    public const float ChaseReplanMetres = 1.5f;

    /// <summary>Гистерезис: стоячий боец начинает сближение дальше, чем останавливается.</summary>
    public const float ChaseStartExtraMetres = 0.5f;

    /// <summary>Промежуточная точка chase-маршрута (как return / patrol advance).</summary>
    public const float ChaseWaypointArrivalMetres = 0.8f;

    /// <summary>Нужен ли новый <c>FindPath</c> к <paramref name="goal"/>.</summary>
    public static bool ShouldReplanPath(bool approachMoving, DateTime lastPlanUtc, Vector3 lastPlanTarget, Vector3 goal)
    {
        if (!approachMoving)
            return true;
        if ((DateTime.UtcNow - lastPlanUtc).TotalMilliseconds < ChaseReplanMs)
            return false;
        return MathUtil.CalculateDistance(lastPlanTarget, goal, true) > ChaseReplanMetres;
    }

    /// <summary>Дистанция, ниже которой погоню можно прекращать (уже подбегаем).</summary>
    public static float ChaseStopRange(float attackStartRangeScale) => attackStartRangeScale;

    /// <summary>Дистанция, выше которой нужно идти к цели (с гистерезисом, если уже бежим).</summary>
    public static float ChaseStartRange(float attackStartRangeScale, bool approachMoving) =>
        approachMoving ? attackStartRangeScale : attackStartRangeScale + ChaseStartExtraMetres;
}