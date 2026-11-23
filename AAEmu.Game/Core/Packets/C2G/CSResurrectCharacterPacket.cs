using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSResurrectCharacterPacket() : GamePacket(CSOffsets.CSResurrectCharacterPacket, 5)
{
    /// <summary>Duration of the post-revive Respawn-Cooldown debuff in milliseconds (5 min).</summary>
    private const int RespawnCooldownDurationMs = 300_000;

    public override void Read(PacketStream stream)
    {
        var inPlace = stream.ReadBoolean();
        var character = Connection?.ActiveChar;
        if (character == null || !character.TryBeginResurrection(inPlace, out var recovery))
            return;

        var restoredVitals = false;
        try
        {
            var portal = inPlace ? null : FindReturnPortal(character);
            var hp = Math.Max(1, (int)(character.MaxHp * (recovery.Hp / 100d)));
            var mp = Math.Max(0, (int)(character.MaxMp * (recovery.Mp / 100d)));
            character.Hp = hp;
            character.Mp = mp;
            restoredVitals = true;
            character.PostUpdateCurrentHp(character, 0, hp, KillReason.Unknown);

            if (portal is { X: not 0 })
            {
                character.BroadcastPacket(new SCCharacterResurrectedPacket(character.ObjId, portal.X,
                    portal.Y, portal.Z, portal.ZRot), true);
            }
            else
            {
                var position = character.Transform.World.Position;
                character.BroadcastPacket(new SCCharacterResurrectedPacket(character.ObjId, position.X,
                    position.Y, position.Z, 0), true);
            }

            character.BroadcastPacket(new SCUnitPointsPacket(character.ObjId, character.Hp,
                character.Mp, character.HighAbilityRsc), true);
            ApplyRevivalDebuffs(character, inPlace);

            character.IsUnderWater = false;
            character.Breath = character.LungCapacity;
        }
        finally
        {
            character.EndResurrection(restoredVitals);
        }
    }

    private static Portal FindReturnPortal(Character character)
    {
        if (character.Transform.InstanceId != WorldManager.DefaultInstanceId)
        {
            foreach (var npc in character.ParentWorld.GetAllNpcs())
            {
                if (npc.TemplateId != 502)
                    continue;

                var location = npc.Transform.World;
                return new Portal
                {
                    WorldId = character.Transform.WorldId,
                    ZoneId = npc.Transform.ZoneId,
                    X = location.Position.X + Random.Shared.Next(1, 3),
                    Y = location.Position.Y + Random.Shared.Next(1, 3),
                    Z = location.Position.Z,
                    ZRot = location.Rotation.Z,
                    Yaw = location.Rotation.Z
                };
            }

            return null;
        }

        var usePortalId = 0u;
        var currentZone = ZoneManager.Instance.GetZoneByKey(character.Transform.ZoneId);
        if (currentZone != null)
        {
            foreach (var conflict in ZoneManager.Instance.GetConflicts())
            {
                if (conflict.ZoneGroupId != currentZone.GroupId)
                    continue;

                if (conflict.CurrentZoneState == ZoneConflictType.War)
                {
                    usePortalId = character.Faction.MotherId switch
                    {
                        FactionsEnum.NuiaAlliance => conflict.NuiaReturnPointId,
                        FactionsEnum.HaranyaAlliance => conflict.HariharaReturnPointId,
                        _ => 0u
                    };
                }
                break;
            }
        }

        var portal = usePortalId > 0 ? PortalManager.Instance.GetRespawnById(usePortalId) : null;
        return portal ?? PortalManager.Instance.GetClosestReturnPortal(character);
    }


    /// <summary>
    /// Apply post-revive debuffs based on the death context:
    ///   inPlace (player-res) → no debuffs at all
    ///   DiedInPvpWarZone     → Leech + 5 min Respawn-CD
    ///   DiedInPvp            → 5 min Respawn-CD only (no Weakened Body)
    ///   PvE death            → Weakened Body + 5 min Respawn-CD
    /// </summary>
    private static void ApplyRevivalDebuffs(Character character, bool inPlace)
    {
        if (inPlace)
        {
            // Player-resurrected (e.g. by another player's resurrect skill): no debuffs.
            character.DiedInPvpWarZone = false;
            character.DiedInPvp = false;
            return;
        }

        var casterObj = new SkillCasterUnit(character.ObjId);

        if (character.DiedInPvpWarZone)
        {
            // PvP death in War zone → Leech + Respawn-CD
            character.DiedInPvpWarZone = false;
            character.DiedInPvp = false;
            ApplyBuff(character, casterObj, (uint)BuffConstants.WarZoneLeech);
            ApplyBuff(character, casterObj, (uint)BuffConstants.RespawnCooldown, RespawnCooldownDurationMs);
        }
        else if (character.DiedInPvp)
        {
            // PvP death outside War zone → Respawn-CD only (no Weakened Body)
            character.DiedInPvp = false;
            ApplyBuff(character, casterObj, (uint)BuffConstants.RespawnCooldown, RespawnCooldownDurationMs);
        }
        else
        {
            // PvE death → Weakened Body + Respawn-CD
            ApplyBuff(character, casterObj, (uint)BuffConstants.WeakenedBody);
            ApplyBuff(character, casterObj, (uint)BuffConstants.RespawnCooldown, RespawnCooldownDurationMs);
        }
    }

    private static void ApplyBuff(Character character, SkillCasterUnit casterObj, uint buffId, int forcedDurationMs = 0)
    {
        var template = SkillManager.Instance.GetBuffTemplate(buffId);
        if (template == null)
            return;

        var buff = new Buff(character, character, casterObj, template, null, DateTime.UtcNow);
        if (forcedDurationMs > 0)
            character.Buffs.AddBuff(buff, forcedDuration: forcedDurationMs);
        else
            character.Buffs.AddBuff(buff);
    }
}
