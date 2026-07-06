namespace AAEmu.Game.Models.Game.Animation;

/// <summary>
/// Шаблон анимации из таблицы <c>anims</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>anims</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Id"/></description></item>
///   <item><description><c>name</c> text → <see cref="Name"/></description></item>
///   <item><description><c>loop</c> bool → <see cref="Loop"/></description></item>
///   <item><description><c>category_id</c> int → <see cref="Category"/></description></item>
///   <item><description><c>ride_ub</c> text → <see cref="RideUB"/></description></item>
///   <item><description><c>hang_ub</c> text → <see cref="HangUB"/></description></item>
///   <item><description><c>swim_ub</c> text → <see cref="SwimUB"/></description></item>
///   <item><description><c>move_ub</c> text → <see cref="MoveUB"/></description></item>
///   <item><description><c>relaxed_ub</c> text → <see cref="RelaxedUB"/></description></item>
///   <item><description><c>swim_move_ub</c> text → <see cref="SwimMoveUB"/></description></item>
/// </list>
/// Поля <see cref="Duration"/> и <see cref="CombatSyncTime"/> заполняются из клиентского файла <c>game/combat_sync_event_list.g</c>.
/// </remarks>
public class Anim
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public bool Loop { get; set; }
    public AnimCategory Category { get; set; }

    /* Overrides for different situations */
    public string RideUB { get; set; }
    public string HangUB { get; set; }
    public string SwimUB { get; set; }
    public string MoveUB { get; set; }
    public string RelaxedUB { get; set; }
    public string SwimMoveUB { get; set; }

    /* Data from client */
    public int Duration { get; set; }
    public int CombatSyncTime { get; set; }
}
