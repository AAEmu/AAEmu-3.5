namespace AAEmu.Game.Models.Game.World.Zones;

/// <summary>
/// Группа зон из таблицы <c>zone_groups</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>zone_groups</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Id"/></description></item>
///   <item><description><c>name</c> text → <see cref="Name"/></description></item>
///   <item><description><c>x</c> real → <see cref="X"/></description></item>
///   <item><description><c>y</c> real → <see cref="Y"/></description></item>
///   <item><description><c>w</c> real → <see cref="Width"/></description></item>
///   <item><description><c>h</c> real → <see cref="Height"/></description></item>
///   <item><description><c>target_id</c> int → <see cref="TargetId"/></description></item>
///   <item><description><c>faction_id</c> int → <see cref="FactionId"/></description></item>
///   <item><description><c>faction_chat_region_id</c> int → <see cref="FactionChatRegionId"/></description></item>
///   <item><description><c>pirate_desperado</c> bool → <see cref="PirateDesperado"/></description></item>
///   <item><description><c>fishing_sea_loot_pack_id</c> int → <see cref="FishingSeaLootPackId"/></description></item>
///   <item><description><c>fishing_land_loot_pack_id</c> int → <see cref="FishingLandLootPackId"/></description></item>
///   <item><description><c>buff_id</c> int → <see cref="BuffId"/></description></item>
///   <item><description><c>display_text</c> text → <see cref="DisplayText"/></description></item>
///   <item><description><c>enable_physics_collision_damage</c> bool → <see cref="EnablePhysicsCollisionDamage"/></description></item>
///   <item><description><c>image_map</c> int → <see cref="ImageMap"/></description></item>
///   <item><description><c>sound_id</c> int → <see cref="SoundId"/></description></item>
///   <item><description><c>sound_pack_id</c> int → <see cref="SoundPackId"/></description></item>
/// </list>
/// </remarks>
public class ZoneGroup
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public uint TargetId { get; set; }
    public uint FactionId { get; set; }
    public uint FactionChatRegionId { get; set; }
    public bool PirateDesperado { get; set; }
    public uint FishingSeaLootPackId { get; set; }
    public uint FishingLandLootPackId { get; set; }
    public uint BuffId { get; set; } // TODO: 1.2 BuffId
    public string DisplayText { get; set; } = string.Empty;
    public bool EnablePhysicsCollisionDamage { get; set; }
    public uint ImageMap { get; set; }
    public uint SoundId { get; set; }
    public uint SoundPackId { get; set; }

    public ZoneConflict Conflict { get; set; }
}
