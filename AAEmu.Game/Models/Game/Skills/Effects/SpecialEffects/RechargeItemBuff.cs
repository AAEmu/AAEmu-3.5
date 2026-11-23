using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class RechargeItemBuff : SpecialEffectAction
{
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
        if (caster is not Character owner || skill is not { Cancelled: false })
            return;

        if (ReferenceEquals(target, owner) && casterObj is SkillItem source && targetObj is SkillCastItemTarget itemTarget)
        {
            if (owner.Inventory?.Bag?.TryRechargeEquipment(owner.Id, source.ItemId, skill.Id, itemTarget.Id, time,
                    out var reagent, out var equipment) == true)
            {
                if (owner.ConditionChance)
                    owner.ItemUse(reagent);

                owner.SendPacket(new SCItemDetailUpdatedPacket(equipment));
                owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.RechargeBuff, [], []));
                return;
            }

            if (owner.Inventory?.Equipment?.GetItemByItemId(itemTarget.Id) is EquipItem { SlotType: SlotType.Equipment } equipped &&
                equipped.OwnerId == owner.Id)
            {
                skill.Cancelled = true;
                owner.SendErrorMessage(ErrorMessageType.CannotRechargeWhenEquipped);
                return;
            }
        }

        skill.Cancelled = true;
        owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
    }
}
