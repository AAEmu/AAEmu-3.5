namespace AAEmu.Game.Models.Game.Items.Templates;

public class GradeTemplate
{
    // Updated for 3.5.0.3
    public int Grade { get; set; }
    public string ColorArgb { get; set; }
    public string ColorArgbSecond { get; set; }
    public float Durability { get; set; }
    public int GradeOrder { get; set; }
    public int IconId { get; set; }
    public string Name { get; set; }
    public int RefundMultiplier { get; set; }
    public int StatMultiplier { get; set; }
    public int UpgradeRatio { get; set; }
    public float HoldableArmor { get; set; }
    public float HoldableDps { get; set; }
    public float HoldableHealDps { get; set; }
    public float HoldableMagicDps { get; set; }
    public float WearableArmor { get; set; }
    public float WearableMagicResistance { get; set; }
}
