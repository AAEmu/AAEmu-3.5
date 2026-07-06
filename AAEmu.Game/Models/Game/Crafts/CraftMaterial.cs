namespace AAEmu.Game.Models.Game.Crafts;

/// <summary>
/// Материал, необходимый для крафта, из таблицы <c>craft_materials</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>craft_materials</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Id"/></description></item>
///   <item><description><c>craft_id</c> int → <see cref="CraftId"/></description></item>
///   <item><description><c>item_id</c> int → <see cref="ItemId"/></description></item>
///   <item><description><c>amount</c> int → <see cref="Amount"/></description></item>
///   <item><description><c>main_grade</c> bool → <see cref="MainGrade"/></description></item>
///   <item><description><c>require_grade</c> int → <see cref="RequireGrade"/></description></item>
///   <item><description><c>upper_grade</c> bool → <see cref="UpperGrade"/></description></item>
/// </list>
/// </remarks>
public class CraftMaterial
{
    public uint Id { get; set; }
    public uint CraftId { get; set; }
    public uint ItemId { get; set; }
    public int Amount { get; set; }
    public bool MainGrade { get; set; }
    public int RequireGrade { get; set; }
    public bool UpperGrade { get; set; }
}
