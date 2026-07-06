namespace AAEmu.Game.Models.Game.World.Zones;

/// <summary>
/// Элемент климата зоны из таблицы <c>zone_climate_elems</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>zone_climate_elems</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Id"/></description></item>
///   <item><description><c>zone_climate_id</c> int → <see cref="ZoneClimateId"/></description></item>
///   <item><description><c>climate_id</c> int → <see cref="ClimateId"/></description></item>
/// </list>
/// </remarks>
public class ZoneClimateElem
{
    public uint Id { get; set; }
    public uint ZoneClimateId { get; set; }
    public Climate ClimateId { get; set; }
}
