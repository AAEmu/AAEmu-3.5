using AAEmu.Game.Core.Packets;
using System.Numerics;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class MoveToLocationEffect : EffectTemplate
{
    public bool OwnHouseOnly { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (caster is not Character character)
            return;

        if (source?.Skill is { Id: TerritoryMemoryEmber.ReturnSkillId } territoryRecall)
        {
            lock (character.GcdLock)
            {
                if (TerritoryMemoryEmber.IsCoolingDown(character, territoryRecall.Id))
                {
                    territoryRecall.Cancelled = true;
                    character.ConditionChance = false;
                    return;
                }

                if (ApplyLanding(character, casterObj, source, skillObject, time))
                    TerritoryMemoryEmber.StartCooldown(character, territoryRecall.Template);
            }
            return;
        }

        ApplyLanding(character, casterObj, source, skillObject, time);
    }

    private bool ApplyLanding(Character character, SkillCaster caster, EffectSource source, SkillObject skillObject, DateTime time)
    {
        var error = ResolveLanding(character, caster, skillObject, time, out var world, out var zone, out var point, out var yaw);
        if (error.HasValue)
        {
            character.SendErrorMessage(error.Value);
            if (source?.Skill is { } skill)
            {
                skill.Cancelled = true;
                character.ConditionChance = false;
            }
            return false;
        }

        SkillTeleportLanding.Apply(character, world, zone, point, yaw, TeleportReason.MoveToLocation);
        return true;
    }

    internal ErrorMessageType? CheckDestination(Character character, SkillCaster caster, SkillObject skillObject) =>
        ResolveLanding(character, caster, skillObject, DateTime.UtcNow, out _, out _, out _, out _);

    internal Portal ResolveBookDestination(Character character, SkillObject skillObject)
    {
        if (character.Portals == null || skillObject is not SkillObjectUnk2 { Id: > 0 } portalObject)
            return null;

        var id = (uint)portalObject.Id;
        if (character.Portals.PrivatePortals.TryGetValue(id, out var portal))
            return portal.Owner == character.Id ? portal : null;

        return OwnHouseOnly ? null : character.Portals.DistrictPortals.GetValueOrDefault(id);
    }

    private ErrorMessageType? ResolveLanding(Character character, SkillCaster caster, SkillObject skillObject, DateTime now,
        out WorldInstance world, out uint zone, out Vector3 point, out float yaw)
    {
        world = null;
        zone = 0;
        point = default;
        yaw = 0;
        if (character.Buffs.CheckBuffTag((uint)BuffConstants.TagOverburdened))
            return ErrorMessageType.CannotUsePortalWithBackpack;

        var item = (caster as SkillItem)?.SkillSourceItem;
        if (item?.TemplateId == TerritoryMemoryEmber.ItemId)
        {
            if (item is not MemoryEmber ember || ember.GetDestinationTerritory(character, now, out zone) is not { } house)
                return ErrorMessageType.InvalidLocation;

            world = house.ParentWorld;
            point = ember.ReturnPosition;
            yaw = ember.ReturnRotation;
        }
        else if (item != null && MemoryEmber.IsTemplate(item.TemplateId))
        {
            if (item is not MemoryEmber ember || ember.GetDestinationHouse(character, now) is not { } house)
                return ErrorMessageType.InvalidHouseInfo;

            if (!MemoryEmber.IsExclusiveHousePoint(house, ember.ReturnPosition))
                return ErrorMessageType.InvalidLocation;

            world = house.ParentWorld;
            point = ember.ReturnPosition;
            yaw = ember.ReturnRotation;
            zone = WorldManager.Instance.GetZoneId(world.Template, point.X, point.Y);
        }
        else
        {
            if (ResolveBookDestination(character, skillObject) is not { } portal)
                return ErrorMessageType.InvalidLocation;

            world = WorldManager.Instance.GetWorld(WorldManager.DefaultInstanceId);
            if (portal.WorldId != 0 && world?.Template?.Id != portal.WorldId)
                return ErrorMessageType.InvalidLocation;
            point = new Vector3(portal.X, portal.Y, portal.Z);
            yaw = portal.ZRot.DegToRad();
            zone = portal.ZoneId;
        }

        return SkillTeleportLanding.CanLand(character, world, zone, point, yaw) ? null : ErrorMessageType.InvalidLocation;
    }
}
