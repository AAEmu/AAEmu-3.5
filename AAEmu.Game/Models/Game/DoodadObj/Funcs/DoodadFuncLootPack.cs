using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncLootPack : DoodadFuncTemplate
{
    // doodad_funcs
    public uint LootPackId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (caster is not Character character)
            return;

        var actAbility = SkillManager.Instance.GetSkillActAbility(skillId);

        var lootPack = LootGameData.Instance.GetPack(LootPackId);
        if (lootPack == null)
            return;

        var lootPackContents = lootPack.GeneratePack(character, actAbility);
        if (LootPackId is 9972 or 14932)
        {
            owner.ToNextPhase = false;
            var house = MemoryEmber.GetFireplaceHouse(owner);
            var transform = character.Transform.World;
            if (house == null || house.OwnerId != character.Id || !ReferenceEquals(character.ParentWorld, house.ParentWorld) ||
                !MemoryEmber.IsExclusiveHousePoint(house, transform.Position))
            {
                character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
                return;
            }

            // Both authored hearth packs contain exactly one nonstackable Ember.
            if (lootPackContents.Count != 1 || !MemoryEmber.IsTemplate(lootPackContents[0].itemId) || lootPackContents[0].count != 1)
                return;
            var (itemId, count, grade, _) = lootPackContents[0];
            owner.ToNextPhase = character.Inventory.Bag.AcquireDefaultItemEx(ItemTaskType.DoodadInteraction,
                itemId, count, grade, out _, out _, 0, prepareNewItem: item =>
                    ((MemoryEmber)item).BindToHouse(house, transform.Position, transform.Rotation.Z));
            if (!owner.ToNextPhase)
                character.SendErrorMessage(ErrorMessageType.BagFull);
            return;
        }

        if (character.Inventory.Bag.FreeSlotCount >= lootPackContents.Count)
        {
            lootPack.GiveLootPack(character, actAbility, ItemTaskType.DoodadInteraction, lootPackContents);
            owner.ToNextPhase = true;

            return;
        }
        // TODO: make sure the doodad is marked as loot-able when not enough inventory space

        character.SendErrorMessage(ErrorMessageType.BagFull);
    }
}
