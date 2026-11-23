using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Features;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Items;

internal static class ItemLookConversion
{
    internal static bool CanUse(Character owner, uint npcObjId)
    {
        if (npcObjId == 0)
            return FeaturesManager.Fsets?.Check(Feature.itemLookConvertInBag) == true;

        var world = owner.ParentWorld;
        var npc = world?.GetNpc(npcObjId);
        if (npc is not { Template.LookConverter: true, IsVisible: true, IsDead: false } || npc.ParentWorld != world)
            return false;

        var centerDistance = MathUtil.CalculateDistance(owner.Transform.World.Position, npc.Transform.World.Position, true);
        return centerDistance - npc.ModelSize < 3f;
    }

    internal static ErrorMessageType GetConversion(Item target, Item donor, out ItemLookConvert conversion, out uint image)
    {
        conversion = null;
        image = 0;
        if (target.Template.ImplId != donor.Template.ImplId)
            return ErrorMessageType.ItemLookConvertAsInvalidCombination;
        if (target.Template.CharGender != donor.Template.CharGender)
            return ErrorMessageType.ItemLookConvertAsDiffGender;

        if (target is EquipItem { Template: EquipItemTemplate { ItemLookConvert: { } equipmentConversion } } && donor is EquipItem)
        {
            conversion = equipmentConversion;
            image = donor.TemplateId;
            return ErrorMessageType.NoErrorMessage;
        }

        if (target is not Backpack { IsGlider: true, Template: BackpackTemplate stat } ||
            donor is not Backpack { IsGlider: true, Template: BackpackTemplate look })
            return ErrorMessageType.ItemLookConvertAsInvalidCombination;
        if (!stat.UseAsStat)
            return ErrorMessageType.ItemLookConvertAsNotUseAsStat;
        if (look.SkinKindId != 2 && (look.SkinKindId != 3 || !donor.HasFlag(ItemFlag.Skinized)))
            return ErrorMessageType.ItemLookConvertAsNotUseAsSkin;

        var items = ItemManager.Instance;
        conversion = items.GetGliderItemLookConvert(stat.CategoryId);
        if (conversion == null || items.GetGliderItemLookConvert(look.CategoryId)?.Id != conversion.Id)
            return ErrorMessageType.ItemLookConvertAsInvalidCombination;

        image = donor.ImageItemTemplateId != 0 ? donor.ImageItemTemplateId : donor.TemplateId;
        var currentImage = target.ImageItemTemplateId != 0 ? target.ImageItemTemplateId : target.TemplateId;
        if (image == currentImage)
            return ErrorMessageType.ItemLookConvertAsInvalidCombination;

        return ErrorMessageType.NoErrorMessage;
    }
}
