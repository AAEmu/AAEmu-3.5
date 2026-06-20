using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

public class DefaultSkill
{
    // updated to 3.5.0.3
    public SkillTemplate Template { get; set; }
    public int Id { get; set; }
    public bool AddToSlot { get; set; }
    public int SkillActiveTypeId { get; set; }
    public int SkillBookCategoryId { get; set; }
    public uint SkillId { get; set; }
    public byte Slot { get; set; }
}
