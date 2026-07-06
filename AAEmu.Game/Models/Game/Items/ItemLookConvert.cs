namespace AAEmu.Game.Models.Game.Items;

public class ItemLookConvert
{
    public uint Id { get; set; }
    // TODO: no DB column, stale
    public uint SlotId { get; set; }
    public uint EntryId { get; set; }
    public uint RequiredItemId { get; set; }
    public int RequiredItemCount { get; set; }
}
