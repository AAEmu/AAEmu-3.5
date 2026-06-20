namespace AAEmu.Game.Models.Game.Items.ItemEnchants;

public class ItemEnchantingGem
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int EquipItemTagId { get; set; }
    public int EquipItemId { get; set; }
    public int EquipLevel { get; set; }
    public int EquipSlotGroupId { get; set; }
    public int GemVisualEffectId { get; set; }
    public bool IgnoreEquipItemTag { get; set; }
    public int ItemGradeId { get; set; }
}
