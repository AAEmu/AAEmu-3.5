namespace AAEmu.Game.Models.Game;

/// <summary>
/// Шаблон уровня опыта из таблицы <c>levels</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>levels</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Level"/></description></item>
///   <item><description><c>expedition_exp</c> int → <see cref="ExpeditionExp"/></description></item>
///   <item><description><c>req_item_count</c> int → <see cref="ReqItemCount"/></description></item>
///   <item><description><c>req_item_id</c> int → <see cref="ReqItemId"/></description></item>
///   <item><description><c>skill_points</c> int → <see cref="SkillPoints"/></description></item>
///   <item><description><c>total_exp</c> int → <see cref="TotalExp"/></description></item>
///   <item><description><c>total_mate_exp</c> int → <see cref="TotalMateExp"/></description></item>
/// </list>
/// </remarks>
public class ExperienceLevelTemplate
{
    public byte Level { get; set; }
    public int ExpeditionExp { get; set; }
    public int ReqItemCount { get; set; }
    public int ReqItemId { get; set; }
    public int SkillPoints { get; set; }
    public int TotalExp { get; set; }
    public int TotalMateExp { get; set; }
}
