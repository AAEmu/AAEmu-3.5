using AAEmu.Game.Core.Packets;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/*
 * Original Authors: nikes, AAGene, Hexlulz
 * Modified by: ZeromusXYZ
 */

public class GainLootPackItemEffect : EffectTemplate
{
    public uint LootPackId { get; set; }
    public bool ConsumeSourceItem { get; set; }
    public uint ConsumeItemId { get; set; }
    public int ConsumeCount { get; set; }
    public bool InheritGrade { get; set; }

    public override bool OnActionTime => false;
    internal override bool HandlesSourceItemConsumption => ConsumeSourceItem;
    internal override bool HandlesItemTemplateConsumption => !ConsumeSourceItem && ConsumeItemId > 0 && ConsumeCount > 0;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        // Ignore if not a player
        if (caster is not Character character)
            return;

        // Get Pack data
        var pack = LootGameData.Instance.GetPack(LootPackId);
        if (pack == null || pack.Loots.Count <= 0)
            return;

        // TODO: Find the related ActAbility
        var actAbility = ActabilityType.None;

        if (!ConsumeSourceItem && ConsumeCount == 0)
        {
            // the tractor collects water
            character.Inventory.Bag.ConsumeItem(ItemTaskType.ConsumeSkillSource, ConsumeItemId, ConsumeCount, null);
            pack.GiveLootPack(character, actAbility, ItemTaskType.SkillEffectGainItem);
            Logger.Debug($"GainLootPackItemEffect {LootPackId}");
            return;
        }

        if (casterObj is not SkillItem skillItem)
            return;

        var sourceItem = character.Inventory.Bag.GetItemByItemId(skillItem.ItemId);
        if (sourceItem == null || sourceItem.OwnerId != character.Id)
        {
            Logger.Warn($"Invalid loot result items {skillItem.ItemId} in lootpack {LootPackId}");
            return;
        }

        if (ConsumeSourceItem)
        {
            if (!sourceItem.CanDestroy() ||
                !character.Inventory.Bag.TryConsumeExact(ItemTaskType.ConsumeSkillSource, sourceItem.TemplateId, 1, sourceItem))
                return;
        }
        else
        {
            character.Inventory.Bag.ConsumeItem(ItemTaskType.ConsumeSkillSource, ConsumeItemId, ConsumeCount, null);
        }

        //Get the source item's grade if we need to inherit it  
        byte? inheritedGrade = InheritGrade ? sourceItem.Grade : null;

        // Give the results
        if (pack.GiveLootPack(character, actAbility, ItemTaskType.SkillEffectGainItem, inheritedGrade: inheritedGrade) &&
            ConsumeSourceItem && source?.Skill != null && character.ConditionChance)
            character.ItemUse(sourceItem);

        Logger.Debug($"GainLootPackItemEffect {LootPackId}");
    }
}
