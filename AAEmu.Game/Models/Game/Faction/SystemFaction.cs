using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Faction;

/// <summary>
/// Системная фракция из таблицы <c>system_factions</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>system_factions</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Id"/></description></item>
///   <item><description><c>owner_name</c> text → <see cref="OwnerName"/></description></item>
///   <item><description><c>owner_type_id</c> int → <see cref="UnitOwnerType"/></description></item>
///   <item><description><c>owner_id</c> int → <see cref="OwnerId"/></description></item>
///   <item><description><c>political_system_id</c> int → <see cref="PoliticalSystem"/></description></item>
///   <item><description><c>mother_id</c> int → <see cref="MotherId"/></description></item>
///   <item><description><c>aggro_link</c> bool → <see cref="AggroLink"/></description></item>
///   <item><description><c>guard_help</c> bool → <see cref="GuardHelp"/></description></item>
///   <item><description><c>is_diplomacy_tgt</c> bool → <see cref="DiplomacyTarget"/></description></item>
///   <item><description><c>diplomacy_link_id</c> int → <see cref="DiplomacyLinkId"/></description></item>
///   <item><description><c>icon_path</c> text → <see cref="IconPath"/></description></item>
///   <item><description><c>name</c> text → <see cref="DbName"/></description></item>
/// </list>
/// Свойство <see cref="Created"/> не читается из БД, но используется наследниками и пакетами.
/// </remarks>
public class SystemFaction : PacketMarshaler
{
    public FactionsEnum Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DbName { get; set; } = string.Empty;
    public uint OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public FactionsEnum MotherId { get; set; }
    public sbyte UnitOwnerType { get; set; }
    public byte PoliticalSystem { get; set; }
    public bool DiplomacyTarget { get; set; }
    public bool AggroLink { get; set; }
    public bool GuardHelp { get; set; }
    public uint DiplomacyLinkId { get; set; }
    public string IconPath { get; set; } = string.Empty;
    public DateTime Created { get; set; } = DateTime.MinValue;

    public Dictionary<FactionsEnum, FactionRelation> Relations { get; set; } = [];

    public RelationState GetRelationState(SystemFaction otherFaction)
    {
        if (otherFaction == null) return RelationState.Neutral;

        var factionId = MotherId != FactionsEnum.Invalid ? MotherId : Id;
        var otherFactionId = otherFaction.MotherId != 0 ? otherFaction.MotherId : otherFaction.Id;

        // Handle Root Factions
        switch (factionId)
        {
            case FactionsEnum.Neutral:
                return RelationState.Neutral;
            case FactionsEnum.Friendly:
                return RelationState.Friendly;
            case FactionsEnum.Hostile:
                return RelationState.Hostile;
        }

        // Handle Target Root Factions
        switch (otherFactionId)
        {
            case FactionsEnum.Neutral:
                return RelationState.Neutral;
            case FactionsEnum.Friendly:
                return RelationState.Friendly;
            case FactionsEnum.Hostile:
                return RelationState.Hostile;
        }

        if (factionId == otherFactionId)
            return RelationState.Friendly;

        // Not sure if we should prioritize mother faction here?
        if (MotherId != 0)
        {
            var motherFaction = FactionManager.Instance.GetFaction(MotherId);
            if (motherFaction != null)
            {
                if (motherFaction.Relations.TryGetValue(otherFactionId, out var motherRelation))
                    return motherRelation.State;

                // TODO not found, so enemy (id = [1, 2, 3])?
                return RelationState.Friendly;
            }
        }

        return Relations.TryGetValue(otherFactionId, out var relation) ? relation.State : RelationState.Neutral;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)Id);
        stream.Write(AggroLink);
        stream.Write((uint)MotherId);
        stream.Write(Name);
        stream.Write(OwnerId);
        stream.Write(OwnerName);
        stream.Write(UnitOwnerType);
        stream.Write(PoliticalSystem);
        stream.Write(Created);
        stream.Write(DiplomacyTarget);
        stream.Write((byte)0);
        return stream;
    }
}
