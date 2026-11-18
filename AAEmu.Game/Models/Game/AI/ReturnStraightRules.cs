using System.Numerics;

using AAEmu.Game.Models.Game.AI.Utils;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI;

/// <summary>
/// Прямой run-home: обрезка на подъёме круче <see cref="BlinkMaxClimbDegrees"/> (Zones <c>AreaRules.BlinkStopOnLine</c>).
/// </summary>
public static class ReturnStraightRules
{
    public const float BlinkMaxClimbDegrees = 45f;

    public static bool BlinkClimbEnabled { get; } = Environment.GetEnvironmentVariable("AAEMU_ZONE_BLINK_CLIMB") != "0";

    /// <summary>Сэмплы вдоль хорды; останавливается у первого слишком крутого подъёма.</summary>
    public static Vector3 ClipStraightReturn(NpcAi ai, Vector3 from, Vector3 to)
    {
        if (!BlinkClimbEnabled)
            return to;

        var chord = MathUtil.CalculateDistance(from, to, true);
        if (chord < 0.5f)
            return to;

        const int steps = 8;
        var prev = from;
        var prevZ = from.Z;
        var maxRiseRad = BlinkMaxClimbDegrees * MathF.PI / 180f;

        for (var i = 1; i <= steps; i++)
        {
            var t = i / (float)steps;
            var x = from.X + (to.X - from.X) * t;
            var y = from.Y + (to.Y - from.Y) * t;
            var sample = AiUtils.SnapWalkTarget(ai, new Vector3(x, y, prevZ));
            var legFlat = MathUtil.CalculateDistance(prev, new Vector3(sample.X, sample.Y, prevZ), true);
            if (legFlat > 0.02f)
            {
                var rise = sample.Z - prevZ;
                if (MathF.Atan2(rise, legFlat) > maxRiseRad)
                    return prev with { Z = prevZ };
            }

            prev = sample;
            prevZ = sample.Z;
        }

        return to with { Z = prevZ };
    }
}