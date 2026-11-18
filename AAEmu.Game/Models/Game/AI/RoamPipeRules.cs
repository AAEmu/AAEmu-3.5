using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Game.AI;

/// <summary>Тайминг roam-pipe (<c>Create_roaming</c> / <c>MovementRules.RoamPipeTiming</c>).</summary>
public static class RoamPipeRules
{
    public const int RoamPipeInstantGoals = 7;
    public const int DefaultAiTickMs = 100;

    public static bool RoamAiTicksEnabled { get; } =
        Environment.GetEnvironmentVariable("AAEMU_ZONE_ROAM_AI_TICKS") != "0";

    public static int RoamPreDelay(Random random) =>
        random.NextDouble() <= 0.70 ? random.Next(500, 2_000) : random.Next(5_000, 6_000);

    public static int RoamTimeoutMs(Random random, float clientMinDist)
    {
        var min = Math.Max(2_000, (int)(clientMinDist / 2f * 1_000f));
        return min >= 6_000 ? 6_000 : random.Next(min, 6_000);
    }

    public static int TimeoutTicks(int ms, int tickMs) => Math.Max(1, (ms + tickMs - 1) / tickMs);

    /// <summary>Когда обрезать текущую roam-ногу и задержка до следующей (мс).</summary>
    public static (int CutMs, int NextLegMs) RoamPipeTiming(Random random, int tickMs, float clientMinDist)
    {
        var timeout = TimeoutTicks(RoamTimeoutMs(random, clientMinDist), tickMs);
        var preDelay = TimeoutTicks(RoamPreDelay(random), tickMs);
        return ((timeout + 1) * tickMs, (RoamPipeInstantGoals + timeout + preDelay) * tickMs);
    }

    /// <summary>Период AI-тика по дистанции до ближайшего игрока (<c>MovementRules.AiTickMs</c>).</summary>
    public static int AiTickMs(float nearestPlayerMetres) => nearestPlayerMetres switch
    {
        < 30f => 100,
        < 50f => 200,
        < 75f => 300,
        < 90f => 500,
        _ => 900
    };

    public static int AiTickMsForNpc(Npc npc)
    {
        var world = npc.ParentWorld;
        if (world == null)
            return DefaultAiTickMs;

        var pos = npc.Transform.World.Position;
        var nearest = float.PositiveInfinity;
        foreach (var character in world.GetAllCharacters())
        {
            if (character is not Character { IsDead: false } ch)
                continue;
            var dx = ch.Transform.World.Position.X - pos.X;
            var dy = ch.Transform.World.Position.Y - pos.Y;
            var flat = MathF.Sqrt(dx * dx + dy * dy);
            if (flat < nearest)
                nearest = flat;
        }

        return AiTickMs(float.IsPositiveInfinity(nearest) ? 120f : nearest);
    }
}