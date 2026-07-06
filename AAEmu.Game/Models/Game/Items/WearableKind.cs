namespace AAEmu.Game.Models.Game.Items;

public class WearableKind
{
    public uint TypeId { get; set; }
    // TODO: no DB column, stale
    public int ArmorRatio { get; set; }
    // TODO: no DB column, stale
    public int MagicResistanceRatio { get; set; }
    public uint FullBufId { get; set; }
    public uint HalfBufId { get; set; }
    public int ExtraDamagePierce { get; set; }
    public int ExtraDamageSlash { get; set; }
    public int ExtraDamageBlunt { get; set; }
    public float DurabilityRatio { get; set; }
    public uint SoundMaterialId { get; set; }
}