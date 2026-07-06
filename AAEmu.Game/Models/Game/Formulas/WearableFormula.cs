namespace AAEmu.Game.Models.Game.Formulas;

/// <summary>
/// Формула wearable-предмета из таблицы <c>wearable_formulas</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>wearable_formulas</c>:
/// <list type="bullet">
///   <item><description><c>kind_id</c> int → <see cref="Type"/></description></item>
///   <item><description><c>formula</c> text → <see cref="Formula.TextFormula"/></description></item>
/// </list>
/// Поле <c>id</c> в таблице отсутствует, поэтому <see cref="Formula.Id"/> остаётся значением по умолчанию.
/// </remarks>
public class WearableFormula : Formula
{
    public WearableFormulaType Type { get; set; }
}
