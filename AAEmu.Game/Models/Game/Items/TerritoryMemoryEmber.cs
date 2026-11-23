using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Teleport;

namespace AAEmu.Game.Models.Game.Items;

internal static class TerritoryMemoryEmber
{
    internal const uint ItemId = 40745;
    internal const uint AcquireSkillId = 33475;
    internal const uint ReturnSkillId = 33476;
    internal const uint Marketplace = 645;
    private const uint ExpeditionPermission = 9;

    internal static bool IsCoolingDown(Character character, uint skillId) =>
        !character.IgnoreSkillCooldowns && character.Cooldowns.CheckCooldown(skillId);

    internal static void StartCooldown(Character character, SkillTemplate skill)
    {
        if (!character.IgnoreSkillCooldowns && skill.CooldownTime > 0)
            character.Cooldowns.AddSkillCooldown(skill, (uint)skill.CooldownTime);
    }

    internal static bool IsTerritory(uint group) => group is 33 or 34 or 43 or 44 or 54 or 56;

    internal static bool IsSourceHouse(uint house) => house is Marketplace or 654 or 655 or 656;

    private static bool IsBinding(uint house, uint doodad, AttachPointKind attachment) => (house, doodad, attachment) is
        (Marketplace, 10983, AttachPointKind.Cannon1) or
        (654, 11203, AttachPointKind.Cannon14) or
        (655, 11204, AttachPointKind.Cannon14) or
        (656, 11205, AttachPointKind.Cannon14);

    internal static bool MatchesSource(uint house, uint doodad, AttachPointKind attachment, uint phase, uint territory, uint permission)
    {
        if (!IsBinding(house, doodad, attachment) || !IsTerritory(territory))
            return false;

        if (house == Marketplace)
            return permission == 0 && (phase, territory) is (31216, 34) or (31217, 33) or (31218, 43) or
                (31219, 44) or (31220, 56) or (31221, 54);

        return permission == ExpeditionPermission && (house, phase) is (654, 32025) or (655, 32026) or (656, 32027);
    }

    internal static HousingBindingDoodad FindAttachment(HousingTemplate house, uint doodad, AttachPointKind attachment)
    {
        if (house?.HousingBindingDoodad == null || !IsBinding(house.Id, doodad, attachment))
            return null;

        foreach (var binding in house.HousingBindingDoodad)
        {
            if (binding.DoodadId == doodad && binding.AttachPointId == attachment)
                return binding;
        }

        return null;
    }

    internal static bool CanUseHouse(Character character, House house)
    {
        if (house is not { OwnerId: > 0, CurrentStep: -1, IsDead: false, IsVisible: true, ParentWorld.Template: not null } ||
            house.ParentWorld.Id != WorldManager.DefaultInstanceId ||
            !ReferenceEquals(HousingManager.Instance.GetHouseById(house.Id), house) ||
            !house.AllowedToInteract(character))
            return false;

        var expedition = character.Expedition;
        return house.TemplateId == Marketplace || house.OwnerId == character.Id ||
            expedition is { isDisbanded: false } && expedition.GetMember(house.OwnerId) != null;
    }

    internal static SkillResult CheckAcquisition(Character character, Doodad doodad, SkillTemplate skill,
        out House house, out uint zone, out uint territory, out Vector3 position, out float yaw)
    {
        house = doodad?.ParentObj as House;
        zone = 0;
        territory = 0;
        position = default;
        yaw = 0;
        if (doodad is not { IsVisible: true, OwnerType: DoodadOwnerType.Housing } ||
            doodad.Despawn > DateTime.MinValue || house == null || doodad.OwnerDbId != house.Id ||
            house.PlaceDate == DateTime.MinValue || character.ParentWorld == null ||
            !ReferenceEquals(character.ParentWorld, house.ParentWorld) ||
            !ReferenceEquals(character.ParentWorld, doodad.ParentWorld) ||
            !ReferenceEquals(character.ParentWorld.GetDoodad(doodad.ObjId), doodad) ||
            FindAttachment(house.Template, doodad.TemplateId, doodad.AttachPoint) == null)
            return SkillResult.InvalidTarget;

        if (!CanUseHouse(character, house))
            return SkillResult.NoPerm;
        if (!(character.GetDistanceTo(doodad, true) <= skill.MaxRange))
            return SkillResult.TooFarRange;

        var source = doodad.Transform.World.Position;
        var world = house.ParentWorld;
        var sourceZone = WorldManager.Instance.GetZoneId(world.Template, source.X, source.Y);
        var sourceTerritory = ZoneManager.Instance.GetZoneByKey(sourceZone)?.GroupId ?? 0;
        if ((ZoneManager.Instance.GetZoneByKey(house.Transform.ZoneId)?.GroupId ?? 0) != sourceTerritory)
            return SkillResult.InvalidLocation;

        var offersSkill = false;
        foreach (var func in doodad.CurrentFuncs)
        {
            if (func.SkillId != AcquireSkillId || func.FuncType != nameof(DoodadFuncUse))
                continue;
            offersSkill = MatchesSource(house.TemplateId, doodad.TemplateId, doodad.AttachPoint, func.GroupId, sourceTerritory, func.PermId);
            break;
        }
        if (!offersSkill)
            return SkillResult.InvalidTarget;

        var standing = character.Transform.World;
        position = standing.Position;
        yaw = standing.Rotation.Z;
        zone = WorldManager.Instance.GetZoneId(world.Template, position.X, position.Y);
        territory = ZoneManager.Instance.GetZoneByKey(zone)?.GroupId ?? 0;
        if (territory != sourceTerritory || !SkillTeleportLanding.CanLand(character, world, zone, position, yaw))
            return SkillResult.InvalidLocation;

        return SkillResult.Success;
    }
}
