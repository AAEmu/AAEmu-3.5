namespace AAEmu.Game.Models.Game.Formulas;

/// <summary>
/// Формула юнита из таблицы <c>unit_formulas</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>unit_formulas</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Formula.Id"/></description></item>
///   <item><description><c>formula</c> text → <see cref="Formula.TextFormula"/></description></item>
///   <item><description><c>kind_id</c> int → <see cref="Kind"/></description></item>
///   <item><description><c>owner_type_id</c> int → <see cref="Owner"/></description></item>
/// </list>
/// </remarks>
public class UnitFormula : Formula
{
    public UnitFormulaKind Kind { get; set; }
    public FormulaOwnerType Owner { get; set; }
}
