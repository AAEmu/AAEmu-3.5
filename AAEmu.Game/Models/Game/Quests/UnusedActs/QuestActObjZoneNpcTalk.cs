using AAEmu.Game.Models.Game.Quests.Templates;

#pragma warning disable IDE0130 // Namespace does not match folder structure

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Assumed to be talking to X amount in a zone. Not actively used
/// </summary>
/// <param name="parentComponent"></param>
public class QuestActObjZoneNpcTalk(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint NpcId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        return base.RunAct(quest, questAct, currentObjectiveCount);
    }
}
