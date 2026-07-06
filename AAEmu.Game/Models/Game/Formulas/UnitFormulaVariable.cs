namespace AAEmu.Game.Models.Game.Formulas;

public enum UnitFormulaVariableType : byte
{
    Unknown = 1,
    NpcTemplate = 2,
    NpcKind = 3,
    NpcGrade = 4,
    MateKind = 5
}

/// <summary>
/// Переменная формулы юнита из таблицы <c>unit_formula_variables</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>unit_formula_variables</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Id"/></description></item>
///   <item><description><c>unit_formula_id</c> int → <see cref="FormulaId"/></description></item>
///   <item><description><c>variable_kind_id</c> int → <see cref="Type"/></description></item>
///   <item><description><c>key</c> int → <see cref="Key"/></description></item>
///   <item><description><c>value</c> real → <see cref="Value"/></description></item>
/// </list>
/// </remarks>
public class UnitFormulaVariable
{
    public uint Id { get; set; }
    public uint FormulaId { get; set; }
    public UnitFormulaVariableType Type { get; set; }
    public uint Key { get; set; }
    public float Value { get; set; }
}
