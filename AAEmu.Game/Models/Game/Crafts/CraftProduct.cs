namespace AAEmu.Game.Models.Game.Crafts;

/// <summary>
/// Результат крафта из таблицы <c>craft_products</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>craft_products</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Id"/></description></item>
///   <item><description><c>craft_id</c> int → <see cref="CraftId"/></description></item>
///   <item><description><c>item_id</c> int → <see cref="ItemId"/></description></item>
///   <item><description><c>amount</c> int → <see cref="Amount"/></description></item>
///   <item><description><c>rate</c> int → <see cref="Rate"/></description></item>
///   <item><description><c>show_lower_crafts</c> bool → <see cref="ShowLowerCrafts"/></description></item>
///   <item><description><c>use_grade</c> bool → <see cref="UseGrade"/></description></item>
///   <item><description><c>item_grade_id</c> int → <see cref="ItemGradeId"/></description></item>
/// </list>
/// </remarks>
public class CraftProduct
{
    public uint Id { get; set; }
    public uint CraftId { get; set; }
    public uint ItemId { get; set; }
    public int Amount { get; set; }
    public int Rate { get; set; }
    public bool ShowLowerCrafts { get; set; }
    public bool UseGrade { get; set; }
    public uint ItemGradeId { get; set; }
}
