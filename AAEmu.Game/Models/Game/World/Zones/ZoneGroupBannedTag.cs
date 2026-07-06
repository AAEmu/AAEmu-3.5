namespace AAEmu.Game.Models.Game.World.Zones;

/// <summary>
/// Запрещённый тег группы зон из таблицы <c>zone_group_banned_tags</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>zone_group_banned_tags</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Id"/></description></item>
///   <item><description><c>zone_group_id</c> int → <see cref="ZoneGroupId"/></description></item>
///   <item><description><c>tag_id</c> int → <see cref="TagId"/></description></item>
///   <item><description><c>banned_periods_id</c> int → <see cref="BannedPeriodsId"/></description></item>
///   <item><description><c>usage</c> text → <see cref="Usage"/></description></item>
/// </list>
/// </remarks>
public class ZoneGroupBannedTag
{
    public uint Id { get; set; }
    public uint ZoneGroupId { get; set; }
    public uint TagId { get; set; }
    public uint BannedPeriodsId { get; set; }
    public string Usage { get; set; } = string.Empty;
}
