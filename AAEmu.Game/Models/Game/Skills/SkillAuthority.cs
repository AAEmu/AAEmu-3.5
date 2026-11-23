using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

using MateUnit = AAEmu.Game.Models.Game.Units.Mate;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>Authorizes the sources of player skill requests before any skill is started.</summary>
public sealed class SkillAuthority(SkillAuthorityGameData data)
{
    private const float InteractionDistance = 25f;
    private const uint SavePortal = 11215;
    private const uint RenamePortal = 16841;
    private const uint OpenPortal = 11216;

    public SkillResult Check(Character sender, SkillCaster caster, SkillCastTarget target, SkillTemplate template,
        BaseUnit mountUnit = null, bool isDefaultSkill = false) =>
        Check(sender, caster, target, template, mountUnit, isDefaultSkill, out _, out _);

    internal SkillResult Check(Character sender, SkillCaster caster, SkillCastTarget target, SkillTemplate template,
        BaseUnit mountUnit, bool isDefaultSkill, out uint mountSkillId, out AttachPointKind mountSeat)
    {
        mountSkillId = 0;
        mountSeat = AttachPointKind.None;
        if (template == null || template.Id == 0)
            return SkillResult.InvalidSkill;

        switch (caster)
        {
            case SkillCasterUnit unit when unit.ObjId == sender.ObjId:
                return CheckUnit(sender, target, template, isDefaultSkill);
            case SkillItem item when item.ObjId == sender.ObjId:
                if (!item.ResolveSourceItem(sender))
                    return SkillResult.InvalidSource;
                return CheckItem(sender, item.SkillSourceItem, template.Id);
            case SkillCasterMount mount when mountUnit?.ObjId == mount.ObjId:
                return CheckMount(sender, mountUnit, template, out mountSkillId, out mountSeat);
            default:
                return SkillResult.InvalidSource;
        }
    }

    internal SkillResult CheckItem(Character sender, Item item, uint skillId)
    {
        if (item?.Template == null || item.Id == 0 || item.OwnerId != sender.Id ||
            item.SlotType is not (SlotType.Inventory or SlotType.Equipment))
            return SkillResult.InvalidSource;
        if (skillId == 0)
            return SkillResult.InvalidSkill;

        var gradeSkill = data.GetGradeSkill(item.TemplateId, item.Grade);
        if (skillId == (gradeSkill != 0 ? gradeSkill : item.Template.UseSkillId) ||
            item.Template.ImplId == ItemImplEnum.Portal && skillId is SavePortal or RenamePortal or OpenPortal)
            return item.TemplateId == TerritoryMemoryEmber.ItemId && skillId == TerritoryMemoryEmber.ReturnSkillId &&
                TerritoryMemoryEmber.IsCoolingDown(sender, skillId) ? SkillResult.CooldownTime : SkillResult.Success;

        return SkillResult.InvalidSkill;
    }

    private SkillResult CheckUnit(Character sender, SkillCastTarget target, SkillTemplate template, bool isDefaultSkill)
    {
        var skillId = template.Id;
        var hasActiveType = sender.SkillActiveTypes.TryGet(skillId, out var activeType);
        if (template.AbilityId == AbilityType.General)
        {
            if (hasActiveType && activeType != 1)
                return SkillResult.InvalidSkill;
            if (!hasActiveType && isDefaultSkill &&
                SkillManager.Instance.GetDefaultSkill(skillId) is { SkillActiveTypeId: not 1 })
                return SkillResult.InvalidSkill;
        }

        if (CharacterAbilities.IsSpecialAbility(template.AbilityId) &&
            sender.Abilities?.CanUseSpecialAbilitySkill(template) != true)
            return SkillResult.InvalidSkill;

        var targetUnit = target switch
        {
            SkillCastDoodadTarget doodadTarget => sender.ParentWorld?.GetDoodad(doodadTarget.ObjId),
            SkillCastUnitTarget unit => sender.ParentWorld?.GetBaseUnit(unit.ObjId),
            _ => null
        };

        if (skillId == TerritoryMemoryEmber.AcquireSkillId)
        {
            if (TerritoryMemoryEmber.IsCoolingDown(sender, skillId))
                return SkillResult.CooldownTime;
            if (sender.Inventory?.Bag is not { FreeSlotCount: > 0 })
                return SkillResult.BagFull;

            return TerritoryMemoryEmber.CheckAcquisition(sender, targetUnit as Doodad, template,
                out _, out _, out _, out _, out _);
        }

        foreach (var effect in template.Effects)
        {
            if (effect.Template is DoodadItemChangeEffect itemChange)
                return (targetUnit as Doodad)?.CheckItemChange(sender, template, itemChange.Idx) ?? SkillResult.InvalidTarget;
        }

        var isHeirSkill = data.TryGetHeirSkill(skillId, out var heir);
        if (isHeirSkill && sender.Skills?.IsSelectedHeirSkill(heir) != true)
            return SkillResult.InvalidSkill;

        var activatedGeneralSkill = template.AbilityId == AbilityType.General && hasActiveType && activeType == 1;
        var held = isHeirSkill || isDefaultSkill || activatedGeneralSkill || skillId is 2 or 3 or 4 || HasSkill(sender, template) ||
                   data.IsSlashSkill(skillId) || IsClientCommand(sender, target, targetUnit, skillId);
        if (held && targetUnit is not Doodad)
            return SkillResult.Success;

        if (held)
            return InReach(sender, targetUnit, template) ? SkillResult.Success : SkillResult.TooFarRange;

        var targetOffers = targetUnit is Doodad doodad
            ? DoodadOffers(sender, doodad, skillId)
            : UnitOffers(sender, targetUnit, skillId);

        if (!targetOffers)
            return SkillResult.InvalidSkill;

        return InReach(sender, targetUnit, template) ? SkillResult.Success : SkillResult.TooFarRange;
    }

    private bool HasSkill(Character sender, SkillTemplate skill)
    {
        var learned = sender.Skills;
        if (learned?.IsLearned(skill.Id) == true)
            return true;

        foreach (var swap in data.GetBuffSwaps(skill.Id))
        {
            if (learned?.IsLearned(swap.OriginSkillId) == true && sender.Buffs.CheckBuff(swap.BuffId))
                return true;
        }

        foreach (var buffId in data.GetGrantingBuffs(skill.Id))
        {
            if (sender.Buffs.CheckBuff(buffId))
                return true;
        }

        if (data.IsComboTarget(skill.Id))
            return learned?.HasComboFollowUp(skill.Id) == true;

        // The 3.5 skill book also groups hidden alternate forms with the same name, tree and level.
        return learned?.HasNamedVariant(skill) == true;
    }

    private static bool IsClientCommand(Character sender, SkillCastTarget target, BaseUnit targetUnit, uint skillId) => skillId switch
    {
        30666 => target is SkillCastItemTarget, // Item synthesis
        17063 or 26152 or 31602 or 31603 or 32189 or 33995 => target is SkillCastUnitTarget && target.ObjId == sender.ObjId,
        35837 => targetUnit is Slave slave && ReferenceEquals(slave.Summoner, sender),
        _ => false
    };

    private bool UnitOffers(Character sender, BaseUnit unit, uint skillId) => unit switch
    {
        Npc { Template: { } npc } =>
            (npc.NpcInteractionSetId > 0 && data.IsNpcInteraction((uint)npc.NpcInteractionSetId, skillId)) ||
            GetNpcMenuSkill(npc) == skillId ||
            skillId == 12081 && npc.SkillTrainer ||
            skillId == 12085 && npc.Teleporter,
        MateUnit mate => skillId == SkillsEnum.SlaveMounting ||
                        mate.OwnerId == sender.Id && skillId == 13719, // Raise own mate
        Slave slave => data.IsSlaveInteraction(slave.TemplateId, skillId),
        House house => house.OwnerId == sender.Id && (skillId == 12945 || data.IsHousingRebuildSkill(skillId)) ||
                       house.CurrentStep >= 0 && house.Template?.BuildSteps.TryGetValue(house.CurrentStep, out var step) == true &&
                       step.SkillId == skillId,
        Shipyard.Shipyard { CurrentStep: >= 0, Template: { } template } yard
            when template.ShipyardSteps.TryGetValue(yard.CurrentStep, out var step) => step.SkillId == skillId,
        _ => false
    };

    internal static uint GetNpcMenuSkill(NpcTemplate npc)
    {
        if (npc.Banker)
            return SkillsEnum.UseWarehouse;
        if (npc.AbilityChanger)
            return SkillsEnum.ChangeSkillsets;
        if (npc.Auctioneer)
            return SkillsEnum.UseAuctioneer;
        if (npc.Priest)
            return SkillsEnum.Blessing;
        if (npc.Repairman)
            return SkillsEnum.Repair;
        if (npc.Merchant)
            return SkillsEnum.UseStore;
        if (npc.Stabler)
            return SkillsEnum.HealPetSWounds;
        if (npc.Expedition)
            return SkillsEnum.FormGuild;
        if (npc.RecrutingBattlefieldId > 0)
            return SkillsEnum.WarSupport;
        return npc.Blacksmith ? SkillsEnum.ItemFusion : 0;
    }

    private static bool DoodadOffers(Character sender, Doodad doodad, uint skillId)
    {
        foreach (var func in doodad.CurrentFuncs)
        {
            if (func.FuncType == nameof(DoodadFuncSkillHit))
                continue;
            if (func.SkillId == skillId)
                return true;

            var extraSkill = func.FuncType switch
            {
                nameof(DoodadFuncFakeUse) =>
                    (DoodadManager.Instance.GetFuncTemplate(func.FuncId, func.FuncType) as DoodadFuncFakeUse)?.FakeSkillId ?? 0,
                nameof(DoodadFuncConditionalUse) =>
                    (DoodadManager.Instance.GetFuncTemplate(func.FuncId, func.FuncType) as DoodadFuncConditionalUse)?.FakeSkillId ?? 0,
                nameof(DoodadFuncEvidenceItemLoot) =>
                    (DoodadManager.Instance.GetFuncTemplate(func.FuncId, func.FuncType) as DoodadFuncEvidenceItemLoot)?.SkillId ?? 0,
                _ => 0u
            };
            if (extraSkill == skillId)
                return true;
            if (func.SkillId == 0 && extraSkill == 0 && IsSkillLessDoodadAction(sender, doodad, func.FuncType, skillId))
                return true;
        }

        return false;
    }

    // AAC 3.5 uses a compiled action map. These ids are from its action initializer and secondary mapper
    // (x2game_dump.dll, 0x39A67210 and 0x3958B690).
    private static bool IsSkillLessDoodadAction(Character sender, Doodad doodad, string funcType, uint skillId) =>
        funcType switch
        {
            nameof(DoodadFuncButcher) => skillId == 10212,
            nameof(DoodadFuncCutdown) => skillId == 10741,
            nameof(DoodadFuncDig) => skillId == 10199,
            nameof(DoodadFuncFeed) => skillId == 10204,
            nameof(DoodadFuncHarvest) => skillId == 10744,
            nameof(DoodadFuncMow) => skillId == 11007,
            nameof(DoodadFuncShear) => skillId == 10203,
            nameof(DoodadFuncUse) or nameof(DoodadFuncFakeUse) or nameof(DoodadFuncConditionalUse) => skillId == 10999,
            nameof(DoodadFuncClimb) => skillId == 11004,
            nameof(DoodadFuncBinding) => skillId == 11005,
            nameof(DoodadFuncQuest) => skillId is 11006 or 11008,
            nameof(DoodadFuncCraftStart) or nameof(DoodadFuncCraftPack) => skillId == 11000,
            nameof(DoodadFuncCraftInfo) => skillId == 11001,
            nameof(DoodadFuncCraftCancel) => skillId == 11003,
            nameof(DoodadFuncCraftGetItem) => skillId == 11002,
            nameof(DoodadFuncGassExtract) => skillId == 11099,
            nameof(DoodadFuncSkinOff) => skillId == 11100,
            nameof(DoodadFuncCrystalCollect) => skillId == 11101,
            nameof(DoodadFuncOreMine) => skillId == 11106,
            nameof(DoodadFuncTreeByProductsCollect) => skillId == 11102,
            nameof(DoodadFuncDairyCollect) => skillId == 11103,
            nameof(DoodadFuncCatch) => skillId == 11104,
            nameof(DoodadFuncFiberCollect) => skillId == 11105,
            nameof(DoodadFuncRockMine) => skillId == 11107,
            nameof(DoodadFuncMedicalingredientMine) => skillId == 11108,
            nameof(DoodadFuncFruitPick) => skillId == 11109,
            nameof(DoodadFuncDyeingredientCollect) => skillId == 11110,
            nameof(DoodadFuncCropHarvest) => skillId == 11111,
            nameof(DoodadFuncSeedCollect) => skillId == 11112,
            nameof(DoodadFuncCerealHarvest) => skillId == 11113,
            nameof(DoodadFuncSoilCollect) => skillId == 11114,
            nameof(DoodadFuncSpiceCollect) => skillId == 11115,
            nameof(DoodadFuncPlantCollect) => skillId == 11116,
            nameof(DoodadFuncMachinePartsCollect) => skillId == 11223,
            nameof(DoodadFuncRemoveItem) => skillId == 10993,
            nameof(DoodadFuncNaviNaming) => skillId == 11399,
            nameof(DoodadFuncNaviOpenMailbox) => skillId == 11400,
            nameof(DoodadFuncNaviMarkPosToMap) => skillId == 11401,
            nameof(DoodadFuncNaviRemove) => skillId == 11398,
            nameof(DoodadFuncNaviOpenPortal) => skillId == 11406,
            nameof(DoodadFuncNaviTeleport) => skillId == 11407,
            nameof(DoodadFuncLootPack) => skillId is 10991 or 12079,
            nameof(DoodadFuncLootItem) => skillId is 14047 or 12079,
            nameof(DoodadFuncEnterInstance) => skillId == 12015,
            nameof(DoodadFuncRemoveInstance) => skillId == 12016,
            nameof(DoodadFuncCleanupLogicLink) => skillId == 12033,
            nameof(DoodadFuncCoffer) => skillId == 13337,
            nameof(DoodadFuncNaviOpenBounty) => skillId == 20558,
            nameof(DoodadFuncRecoverItem) => skillId == 11361 && doodad.OwnerId == sender.Id,
            _ => false
        };

    private SkillResult CheckMount(Character sender, BaseUnit mount, SkillTemplate template, out uint mountSkillId,
        out AttachPointKind mountSeat)
    {
        mountSkillId = 0;
        mountSeat = AttachPointKind.None;
        var isMate = mount is MateUnit;
        var seat = AttachPointKind.None;
        if (mount is MateUnit mate)
        {
            if (mate.OwnerId != sender.Id)
                return SkillResult.NoPerm;
            if (mate.Passengers.TryGetValue(AttachPointKind.Driver, out var driver) && Volatile.Read(ref driver._objId) == sender.ObjId)
                seat = AttachPointKind.Driver;
        }
        else if (mount is Slave slave)
        {
            foreach (var (point, occupant) in slave.AttachedCharacters)
            {
                if (ReferenceEquals(occupant, sender))
                {
                    seat = point;
                    break;
                }
            }
        }
        else
        {
            return SkillResult.InvalidSource;
        }

        var barSeat = seat;
        if (seat == AttachPointKind.None && mount is Slave ownSlave && ReferenceEquals(ownSlave.Summoner, sender) &&
            ownSlave.OwnerType != BaseUnitType.Slave && (sender.AttachedPoint == AttachPointKind.None || sender.IsRiding) &&
            InReach(sender, ownSlave, template))
            barSeat = AttachPointKind.Driver;

        var rows = data.GetMountSkills(mount.TemplateId, template.Id, isMate);
        var hasSource = rows.Count > 0;
        foreach (var row in rows)
        {
            if (!SeatAllows(row, seat, barSeat, isMate))
                continue;
            mountSkillId = row.Id;
            mountSeat = seat;
            return SkillResult.Success;
        }

        foreach (var grant in data.GetBuffMountSkills(template.Id))
        {
            if (mount is not Unit unit || !unit.Buffs.CheckBuff(grant.BuffId))
                continue;
            hasSource = true;
            if (!SeatAllows(grant.Row, seat, barSeat, isMate))
                continue;
            mountSkillId = grant.Row.Id;
            mountSeat = seat;
            return SkillResult.Success;
        }

        return hasSource ? SkillResult.NoPerm : SkillResult.InvalidSkill;
    }

    private static bool SeatAllows(SkillAuthorityGameData.MountSkill row, AttachPointKind seat,
        AttachPointKind barSeat, bool isMate)
    {
        var requiredSeat = row.CheckAttachPoint ? seat : barSeat;
        if (row.Seats.Count > 0)
            return row.Seats.Contains(requiredSeat) && (requiredSeat != AttachPointKind.None || isMate);

        return requiredSeat != AttachPointKind.None || isMate && !row.CheckAttachPoint;
    }

    private static bool InReach(Character sender, BaseUnit target, SkillTemplate skill)
    {
        var reach = Math.Max(InteractionDistance, skill.MaxRange);
        for (var transform = target.Transform; transform != null; transform = transform.Parent)
        {
            if (transform.GameObject is not Slave { ShipModel: { } model })
                continue;
            reach += Math.Max(model.MassBoxSizeX, Math.Max(model.MassBoxSizeY, model.MassBoxSizeZ)) / 2f;
            break;
        }

        return sender.GetDistanceTo(target, true) <= reach;
    }
}
