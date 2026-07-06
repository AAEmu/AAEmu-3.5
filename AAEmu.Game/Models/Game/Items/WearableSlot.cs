namespace AAEmu.Game.Models.Game.Items;

public class WearableSlot
{
    public uint Id { get; set; }
    public uint SlotTypeId { get; set; }
    public int Coverage { get; set; }
    public int GearScoreMultiplier { get; set; }
}