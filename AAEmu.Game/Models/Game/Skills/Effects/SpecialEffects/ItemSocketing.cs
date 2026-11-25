using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ItemSocketing : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ItemSocketing;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        Logger.Debug("Special effects: ItemSocketing value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

        if (caster is not Character owner)
        {
            Logger.Error($"Special effects: ItemSocketing caster {caster.Id} is not a character");
            return;
        }

        if (casterObj is not SkillItem gemSkillItem)
        {
            Logger.Error($"Special effects: ItemSocketing casterObj {casterObj} is not a SkillItem");
            return;
        }

        if (targetObj is not SkillCastItemTarget skillTargetItem)
        {
            Logger.Error($"Special effects: ItemSocketing targetObj {targetObj} is not a SkillCastItemTarget");
            return;
        }

        var targetItem = owner.Inventory.GetItemById(skillTargetItem.Id);
        var gemItem = owner.Inventory.GetItemById(gemSkillItem.ItemId);

        if (targetItem is null || gemItem is null)
        {
            Logger.Warn($"Special effects: ItemSocketing targetItem {skillTargetItem.Id} or gemItem {gemSkillItem.ItemId} not found");
            return;
        }

        if (targetItem is not EquipItem equipItem)
        {
            Logger.Warn($"Special effects: ItemSocketing targetItem {skillTargetItem.Id} was not a EquipItem");
            return;
        }

        var tasksSocketing = new List<ItemTask>();

        byte result = 0;
        var installed = false;
        var sockets = equipItem.SocketGemIds;
        if (gemItem.TemplateId != (uint)ItemConstants.DawnStone)
        {
            var freeSocket = sockets.IndexOf(0u);
            if (freeSocket >= 0)
            {
                var gemCount = 0u;
                foreach (var gem in sockets)
                    if (gem != 0)
                        gemCount++;

                var gemChance = ItemManager.Instance.GetSocketChance(gemCount);
                if (Random.Shared.Next(0, 10000) < gemChance)
                {
                    sockets[freeSocket] = gemItem.TemplateId;
                    result = 1;
                }
                else
                    sockets.Clear();
                installed = true;
            }
        }
        else
        {
            sockets.Clear();
            result = 1;
        }

        if (installed || result == 1)
        {
            equipItem.IsDirty = true;
            if (ReferenceEquals(equipItem.HoldingContainer, owner.Equipment))
                owner.RebuildGearBonuses();
        }

        tasksSocketing.Add(new ItemUpdate(equipItem));

        owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Socketing, tasksSocketing, []));
        owner.SendPacket(new SCItemSocketingLunagemResultPacket(result, equipItem.Id, gemItem.TemplateId, installed));
        owner.BroadcastPacket(new SCSkillEndedPacket(skill.TlId), true);
    }
}
