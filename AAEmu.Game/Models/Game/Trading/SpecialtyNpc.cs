namespace AAEmu.Game.Models.Game.Trading;

/// <summary>
/// NPC специальной торговли из таблицы <c>specialty_npcs</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>specialty_npcs</c>:
/// <list type="bullet">
///   <item><description><c>npc_id</c> int → <see cref="NpcId"/></description></item>
///   <item><description><c>specialty_bundle_id</c> int → <see cref="SpecialtyBundleId"/></description></item>
/// </list>
/// </remarks>
public class SpecialtyNpc
{
    public uint NpcId { get; set; }
    public uint SpecialtyBundleId { get; set; }
}
