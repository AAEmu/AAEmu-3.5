namespace AAEmu.Game.Models.Game.Items;

public class EquipSlotEnchantingCost
{
    // TODO: no DB column, stale
    public uint Id { get; set; }
    public uint SlotTypeId { get; set; }
    public int Cost { get; set; }
}
