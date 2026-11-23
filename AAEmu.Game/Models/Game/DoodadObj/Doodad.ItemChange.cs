using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.DoodadObj;

public partial class Doodad
{
    private const string ItemChangerMenuType = "DoodadFuncItemChangerUiOpen";
    private const uint CookingSuppliesPhase = 30870;

    internal SkillResult CheckItemChange(Character player, SkillTemplate skill, int index)
    {
        lock (_phaseLock)
            return CheckItemChange(player, skill, index, out _);
    }

    internal SkillResult ResolveItemChangeSkill(Character player, int index, out SkillTemplate skill)
    {
        lock (_phaseLock)
        {
            var selected = FindItemChange(0, index);
            skill = selected == null ? null : SkillManager.Instance.GetSkillTemplate(selected.SkillId);
            if (skill == null)
                return SkillResult.InvalidSkill;

            foreach (var effect in skill.Effects)
            {
                if (effect.Template is not DoodadItemChangeEffect itemChange)
                    continue;

                var result = CheckItemChange(player, skill, itemChange.Idx, out var option);
                if (result != SkillResult.Success)
                    return result;
                if (!ReferenceEquals(selected, option))
                    return SkillResult.InvalidSkill;

                if (player.Inventory?.Bag is not { } bag ||
                    !bag.GetAllItemsByTemplate(selected.ItemId, -1, out _, out var count) || count < selected.ItemCount)
                    return SkillResult.NeedReagent;

                return SkillResult.Success;
            }

            return SkillResult.InvalidSkill;
        }
    }

    internal SkillResult ApplyItemChange(Character player, SkillTemplate skill, int index)
    {
        lock (_phaseLock)
        {
            var result = CheckItemChange(player, skill, index, out var option);
            if (result != SkillResult.Success)
                return result;
            if (player.Inventory?.Bag?.TryConsumeExact(ItemTaskType.DoodadItemChanger, option.ItemId, option.ItemCount) != true)
                return SkillResult.NeedReagent;

            ListGroupId.Clear();
            ToNextPhase = false;
            DoChangePhase(player, option.NextPhase);
            return SkillResult.Success;
        }
    }

    private SkillResult CheckItemChange(Character player, SkillTemplate skill, int index, out DoodadFuncItemChanger option)
    {
        option = null;
        if (_deleted || Despawn > DateTime.MinValue || !IsVisible || player.ParentWorld == null ||
            !ReferenceEquals(player.ParentWorld, ParentWorld) || player.Transform.InstanceId != Transform.InstanceId)
            return SkillResult.InvalidTarget;

        option = FindItemChange(skill.Id, index);
        if (option is not { ItemId: > 0, ItemCount: > 0, NextPhase: > 0 } || option.NextPhase == FuncGroupId)
            return SkillResult.InvalidSkill;
        if (!(player.GetDistanceTo(this, true) <= skill.MaxRange))
            return SkillResult.TooFarRange;
        if (!CanUseItemChangeMenu(player))
            return SkillResult.NoPerm;
        return SkillResult.Success;
    }

    private DoodadFuncItemChanger FindItemChange(uint skillId, int index)
    {
        if (index < 0)
            return null;

        // The cooking event's supply cabinet gives each option its own skill with effect index 0.
        var selectBySkill = skillId != 0 && FuncGroupId == CookingSuppliesPhase && index == 0;
        var position = 0;
        foreach (var func in CurrentPhaseFuncs)
        {
            if (func.FuncType != nameof(DoodadFuncItemChanger))
                continue;
            if (position++ != index && !selectBySkill)
                continue;
            if (DoodadManager.Instance.GetPhaseFuncTemplate(func.FuncId, func.FuncType) is DoodadFuncItemChanger option &&
                (skillId == 0 || option.SkillId == skillId))
                return option;
        }

        return null;
    }

    private bool CanUseItemChangeMenu(Character player)
    {
        var permission = uint.MaxValue;
        foreach (var func in CurrentFuncs)
        {
            if (func.FuncType != ItemChangerMenuType)
                continue;
            permission = func.PermId;
            break;
        }

        // The royal vault's permission 9 depends on nation owner checks unavailable in this branch.
        if (permission > 1 || !AllowedToInteract(player))
            return false;
        if (OwnerType == DoodadOwnerType.Housing &&
            HousingManager.Instance.GetHouseById(OwnerDbId)?.AllowedToInteract(player) != true)
            return false;
        if (permission == 1 && OwnerType == DoodadOwnerType.Character && OwnerId != player.Id)
            return false;

        foreach (var phase in CurrentPhaseFuncs)
        {
            if (phase.FuncType != nameof(DoodadFuncRequireQuest))
                continue;
            if (DoodadManager.Instance.GetPhaseFuncTemplate(phase.FuncId, phase.FuncType) is DoodadFuncRequireQuest requirement &&
                player.Quests?.HasQuest(requirement.QuestId) == true)
                continue;
            return false;
        }

        return true;
    }
}
