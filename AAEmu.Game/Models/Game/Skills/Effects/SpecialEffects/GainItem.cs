using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class GainItem : SpecialEffectAction
{
    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1, // Item TemplateId
        int value2, // Item Count
        int value3,
        int value4
    )
    {
        if (caster is not Character character)
            return;

        Logger.Debug("Special effects: GainItem value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

        if (value1 <= 0)
        {
            Logger.Warn("Special effects: GainItem ignored invalid item template id {0}", value1);
            return;
        }

        var itemId = (uint)value1;
        var itemCount = value2 > 0 ? value2 : 1;
        var taskType = ItemTaskType.SkillEffectGainItem;
        if (itemId == TerritoryMemoryEmber.ItemId)
        {
            if (skill is { Cancelled: false, Template.Id: TerritoryMemoryEmber.AcquireSkillId } && itemCount == 1)
                GrantTerritoryEmber(character, target as Doodad, skill, taskType);
            return;
        }

        // TryAddNewItem internally routes tradepacks to TryEquipNewBackPack and
        // regular items to Bag.AcquireDefaultItem, so a single call handles both paths.
        var acquired = character.Inventory.TryAddNewItem(taskType, itemId, itemCount, 0);

        if (!acquired)
            Logger.Warn("Special effects: GainItem failed to give item {0} x{1} to character {2}", itemId, itemCount, character.Id);
    }

    private static void GrantTerritoryEmber(Character character, Doodad doodad, Skill skill, ItemTaskType taskType)
    {
        lock (character.GcdLock)
        {
            var result = TerritoryMemoryEmber.CheckAcquisition(character, doodad, skill.Template,
                out var house, out var zone, out var territory, out var position, out var yaw);
            if (result != SkillResult.Success)
            {
                character.SendErrorMessage(result == SkillResult.NoPerm
                    ? ErrorMessageType.InteractionPermissionDeny
                    : ErrorMessageType.NoInteractionAvailable);
                skill.Cancelled = true;
                return;
            }

            if (TerritoryMemoryEmber.IsCoolingDown(character, skill.Id))
            {
                skill.Cancelled = true;
                return;
            }

            if (character.Inventory?.Bag?.AcquireDefaultItemEx(taskType, TerritoryMemoryEmber.ItemId, 1, 0,
                    out _, out _, 0, prepareNewItem: item => ((MemoryEmber)item).BindToTerritory(house, zone, territory, position, yaw)) != true)
            {
                character.SendErrorMessage(ErrorMessageType.BagFull);
                skill.Cancelled = true;
                return;
            }

            TerritoryMemoryEmber.StartCooldown(character, skill.Template);
        }
    }
}
