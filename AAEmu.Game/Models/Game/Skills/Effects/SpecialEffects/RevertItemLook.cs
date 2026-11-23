using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class RevertItemLook : SpecialEffectAction
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

        if (!ReferenceEquals(target, owner) || casterObj is not SkillItem source ||
            targetObj is not SkillCastItemTarget itemTarget || owner.Inventory?.Bag is not { } bag)
        {
            skill.Cancelled = true;
            return;
        }

        var result = bag.RevertItemLook(owner.Id, source.ItemId, skill.Id, itemTarget.Id,
            out var reagent, out var item, out var clearedUcc);
        if (result != ErrorMessageType.NoErrorMessage)
        {
            skill.Cancelled = true;
            owner.SendErrorMessage(result);
            return;
        }

        if (owner.ConditionChance)
            owner.ItemUse(reagent);

        owner.SendPacket(new SCItemDetailUpdatedPacket(item));
        List<ItemTask> tasks = [];
        if (clearedUcc)
        {
            tasks.Add(new ItemUpdateBits(item));
            owner.SendPacket(new SCItemUccDataChangedPacket(0, owner.Id, item.Id));
        }
        owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.RevertItemLook, tasks, []));
    }
}
