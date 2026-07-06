namespace AAEmu.Game.Models.Game.Taxations;

/// <summary>
/// Шаблон налогообложения из таблицы <c>taxations</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>taxations</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Id"/></description></item>
///   <item><description><c>desc</c> text → <see cref="Desc"/></description></item>
///   <item><description><c>seal_count</c> int → <see cref="SealCount"/></description></item>
///   <item><description><c>show</c> bool → <see cref="Show"/></description></item>
///   <item><description><c>tax</c> int → <see cref="Tax"/></description></item>
/// </list>
/// </remarks>
public class Taxation
{
    public uint Id { get; set; }
    public string Desc { get; set; } = string.Empty;
    public uint SealCount { get; set; }
    public bool Show { get; set; }
    public uint Tax { get; set; }
}
