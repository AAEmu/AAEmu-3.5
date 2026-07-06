using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.World.Zones;

/// <summary>
/// Зона из таблицы <c>zones</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>zones</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Id"/></description></item>
///   <item><description><c>name</c> text → <see cref="Name"/></description></item>
///   <item><description><c>zone_key</c> int → <see cref="ZoneKey"/></description></item>
///   <item><description><c>group_id</c> int → <see cref="GroupId"/></description></item>
///   <item><description><c>closed</c> bool → <see cref="Closed"/></description></item>
///   <item><description><c>faction_id</c> int → <see cref="FactionId"/></description></item>
///   <item><description><c>zone_climate_id</c> int → <see cref="ZoneClimateId"/></description></item>
///   <item><description><c>display_text</c> text → <see cref="DisplayText"/></description></item>
/// </list>
/// </remarks>
public class Zone
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint ZoneKey { get; set; }
    public uint GroupId { get; set; }
    public bool Closed { get; set; }
    public FactionsEnum FactionId { get; set; }
    public uint ZoneClimateId { get; set; }
    public string DisplayText { get; set; } = string.Empty;
}
