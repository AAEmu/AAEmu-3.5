using AAEmu.Game.Core.Packets;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class MoveToRezPointEffect : EffectTemplate
{
    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (target is not Character character || ResolveReturnPoint(character, PortalManager.Instance) is not { } portal)
            return;

        var world = WorldManager.Instance.GetWorld(WorldManager.DefaultInstanceId);
        if (portal.WorldId != 0 && world?.Template?.Id != portal.WorldId)
            return;

        var point = new Vector3(portal.X, portal.Y, portal.Z);
        var yaw = portal.ZRot.DegToRad();
        if (SkillTeleportLanding.CanLand(character, world, portal.ZoneId, point, yaw))
            SkillTeleportLanding.Apply(character, world, portal.ZoneId, point, yaw, TeleportReason.Resurrect);
    }

    internal static Portal ResolveReturnPoint(Character character, IPortalManager portals)
    {
        if (character.Faction == null)
            return null;

        var faction = character.Faction;
        var bound = GetDistrictRespawn(character.ReturnDistrictId);
        if (bound != null)
            return bound;

        if (character.ResurrectionDistrictId != character.ReturnDistrictId)
        {
            bound = GetDistrictRespawn(character.ResurrectionDistrictId);
            if (bound != null)
                return bound;
        }

        // Current coordinates are only meaningful to the main-world respawn search while in that world.
        return character.Transform.InstanceId == WorldManager.DefaultInstanceId &&
               portals.GetClosestReturnPortal(character) is { Id: > 0 } closest ? closest : null;

        Portal GetDistrictRespawn(uint districtId)
        {
            if (districtId == 0)
                return null;

            var pointId = portals.GetDistrictReturnPoint(districtId, faction.Id);
            if (pointId == 0 && faction.MotherId != faction.Id && faction.MotherId != 0)
                pointId = portals.GetDistrictReturnPoint(districtId, faction.MotherId);
            return pointId != 0 ? portals.GetRespawnById(pointId) : null;
        }
    }
}
