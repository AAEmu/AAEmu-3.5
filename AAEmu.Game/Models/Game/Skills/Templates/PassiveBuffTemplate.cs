namespace AAEmu.Game.Models.Game.Skills.Templates;

public class PassiveBuffTemplate
{
    // updated to 3.5.0.3
    public uint Id { get; set; }
    public AbilityType AbilityId { get; set; }
    public bool Active { get; set; }
    public uint BuffId { get; set; }
    public int HighAbilityId { get; set; }
    public int Level { get; set; }
    public int ReqPoints { get; set; }
    public int SkillPoints { get; set; }
}
