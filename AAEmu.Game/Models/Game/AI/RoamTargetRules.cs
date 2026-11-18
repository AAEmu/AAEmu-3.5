using System.Numerics;

using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Game.AI;

/// <summary>Порт <c>MovementRules.ClientRoamingRange</c> / <c>ClientRoamingOffset</c> (x2ai.lua).</summary>
public static class RoamTargetRules
{
    public const float DefaultRoamingMinDist = 2f;
    public const float DefaultRoamingMaxDist = 5f;

    public static bool ClientRoamTargetsEnabled { get; } =
        Environment.GetEnvironmentVariable("AAEMU_ZONE_CLIENT_ROAM_TARGETS") != "0";

    public static (float Min, float Max) ClientRoamingRange(float? min, float? max)
    {
        var lo = MathF.Max(1f, min ?? 1f);
        var hi = MathF.Max(lo + 1f, max ?? 6f);
        return (lo, hi);
    }

    /// <summary><c>X2AI:GetRoamingDistance</c> — смещение по одной оси.</summary>
    public static float ClientRoamingOffset(Random random, float min, float max)
    {
        var d = (float)(random.NextDouble() - 0.5) * (max - min);
        return d < 0f ? d - min : d + min;
    }

    public static (float Min, float Max) ResolveRange(NpcAi ai) =>
        ResolveRange(ai.Owner?.Template);

    public static (float Min, float Max) ResolveRange(NpcTemplate? template)
    {
        if (template is { NpcAiParamId: > 0 })
        {
            var stored = AiGameData.Instance.GetRoamingDistances((uint)template.NpcAiParamId);
            return ClientRoamingRange(stored?.Min, stored?.Max);
        }

        return ClientRoamingRange(null, null);
    }

    public static Vector3 PickHorizontalTarget(NpcAi ai, Vector3 anchor, float footZ)
    {
        var (min, max) = ResolveRange(ai);
        if (ClientRoamTargetsEnabled)
        {
            return new Vector3(
                anchor.X + ClientRoamingOffset(Random.Shared, min, max),
                anchor.Y + ClientRoamingOffset(Random.Shared, min, max),
                footZ);
        }

        var angle = Random.Shared.NextSingle() * MathF.PI * 2f;
        var distance = min + Random.Shared.NextSingle() * (max - min);
        return new Vector3(
            anchor.X + MathF.Cos(angle) * distance,
            anchor.Y + MathF.Sin(angle) * distance,
            footZ);
    }
}