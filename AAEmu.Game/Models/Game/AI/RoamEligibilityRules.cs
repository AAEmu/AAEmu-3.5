using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.AI.Utils;
using AAEmu.Game.Models.Game.NPChar;
namespace AAEmu.Game.Models.Game.AI;

/// <summary>Who roams (ArcheageZones <c>RoamEligibility</c>): rooted idlers and almighty followers.</summary>
public static class RoamEligibilityRules
{
    public static bool ImmobileIdleEnabled { get; } =
        Environment.GetEnvironmentVariable("AAEMU_ZONE_IMMOBILE_IDLE") != "0";

    public static bool AlmightyFollowersEnabled { get; } =
        Environment.GetEnvironmentVariable("AAEMU_ZONE_ALMIGHTY_FOLLOWERS") != "0";

    private static readonly AiParamType[] RoamingFamilies =
    [
        AiParamType.Roaming,
        AiParamType.ArcherRoaming,
        AiParamType.BigMonsterRoaming,
        AiParamType.WildBoarRoaming
    ];

    public static bool IsRoamingFamily(NpcTemplate template) =>
        RoamingFamilies.Contains((AiParamType)template.AiFileId);

    /// <summary>False when a roaming-family unit must stay put (spawn root / almighty follower).</summary>
    public static bool ShouldRoam(Npc npc)
    {
        if (!IsRoamingFamily(npc.Template))
            return true;

        if (ImmobileIdleEnabled && IsMovementBlocked(npc))
            return false;

        if (AlmightyFollowersEnabled && IsAlmightyFollower(npc))
            return false;

        return true;
    }

    private static bool IsMovementBlocked(Npc npc) =>
        npc.Buffs.HasEffectsMatchingCondition(e =>
            e.Template.Root || e.Template.Stun || e.Template.Sleep || e.Template.Fastened);

    private static bool IsAlmightyFollower(Npc npc)
    {
        if (npc.Spawner?.Template?.Npcs is not { Count: > 0 } lines)
            return false;

        foreach (var line in lines)
        {
            if (!string.Equals(line.MemberType, "NpcGroup", StringComparison.OrdinalIgnoreCase))
                continue;

            var members = NpcGroupGameData.Instance.GetNpcGroupMembers((int)line.MemberId);
            if (members.Count == 0 || members.All(m => m.NpcId != (int)npc.TemplateId))
                continue;

            var leaderMember = members.FirstOrDefault(m => m.IsMoveLeader)
                ?? members.FirstOrDefault(m => m.IsLeader)
                ?? members[0];
            var leaderTemplate = NpcManager.Instance.GetTemplate((uint)leaderMember.NpcId);
            if (leaderTemplate == null)
                continue;

            return (AiParamType)leaderTemplate.AiFileId == AiParamType.AlmightyNpc;
        }

        return false;
    }
}