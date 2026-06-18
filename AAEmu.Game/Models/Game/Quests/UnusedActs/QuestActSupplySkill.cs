using AAEmu.Game.Models.Game.Quests.Templates;

#pragma warning disable IDE0130 // Namespace does not match folder structure

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Assumed to make you learn a skill, no longer used
/// </summary>
/// <param name="parentComponent"></param>
public class QuestActSupplySkill(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint SkillId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        return base.RunAct(quest, questAct, currentObjectiveCount);
    }
}
