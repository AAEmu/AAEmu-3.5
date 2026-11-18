using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Game.AI;

/// <summary>
/// Поводок idle-roam (Zones <c>FarFromHome</c> / <c>ReturnHomeBeyondMetres</c>): дальше home → run return.
/// </summary>
public static class RoamLeashRules
{
    public const float ReturnHomeBeyondMetres = 10f;
    public static bool Enabled { get; } = Environment.GetEnvironmentVariable("AAEMU_ZONE_ROAM_LEASH") != "0";

    public static float LeashLimitMetres(NpcAi ai) =>
        RoamTargetRules.ResolveRange(ai).Max + ReturnHomeBeyondMetres;

    public static float LeashLimitMetres(Npc? npc) =>
        npc?.Ai is { } ai ? LeashLimitMetres(ai) : RoamTargetRules.DefaultRoamingMaxDist + ReturnHomeBeyondMetres;

    public static bool IsBeyondLeash(NpcAi ai)
    {
        if (!Enabled || ai.Owner == null)
            return false;

        var home = ai.HomePosition;
        var pos = ai.Owner.Transform.World.Position;
        var dx = pos.X - home.X;
        var dy = pos.Y - home.Y;
        var limit = LeashLimitMetres(ai);
        return dx * dx + dy * dy > limit * limit;
    }

    public static bool ShouldReturnHome(NpcAi ai) =>
        IsBeyondLeash(ai) && RoamEligibilityRules.IsRoamingFamily(ai.Owner.Template);
}