namespace AAEmu.Game.Models.Game.Models;

/// <summary>
/// Тип модели из таблицы <c>models</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>models</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Id"/></description></item>
///   <item><description><c>sub_id</c> int → <see cref="SubId"/></description></item>
///   <item><description><c>sub_type</c> text → <see cref="SubType"/></description></item>
///   <item><description><c>big</c> bool → <see cref="Big"/></description></item>
///   <item><description><c>camera_distance</c> real → <see cref="CameraDistance"/></description></item>
///   <item><description><c>camera_distance_for_wide_angle</c> real → <see cref="CameraDistanceForWideAngle"/></description></item>
///   <item><description><c>despawn_doodad_on_collision</c> bool → <see cref="DespawnDoodadOnCollision"/></description></item>
///   <item><description><c>dying_time</c> real → <see cref="DyingTime"/></description></item>
///   <item><description><c>high_impact_fx_group_id</c> int → <see cref="HighImpactFxGroupId"/></description></item>
///   <item><description><c>low_impact_fx_group_id</c> int → <see cref="LowImpactFxGroupId"/></description></item>
///   <item><description><c>middle_impact_fx_group_id</c> int → <see cref="MiddleImpactFxGroupId"/></description></item>
///   <item><description><c>mount_pose_id</c> int → <see cref="MountPoseId"/></description></item>
///   <item><description><c>name</c> text → <see cref="Name"/></description></item>
///   <item><description><c>name_tag_offset</c> real → <see cref="NameTagOffset"/></description></item>
///   <item><description><c>play_mount_animation</c> bool → <see cref="PlayMountAnimation"/></description></item>
///   <item><description><c>player_mount_name_tag_pos</c> bool → <see cref="PlayerMountNameTagPos"/></description></item>
///   <item><description><c>selectable</c> bool → <see cref="Selectable"/></description></item>
///   <item><description><c>show_name_tag</c> bool → <see cref="ShowNameTag"/></description></item>
///   <item><description><c>sound_material_id</c> int → <see cref="SoundMaterialId"/></description></item>
///   <item><description><c>sound_pack_id</c> int → <see cref="SoundPackId"/></description></item>
///   <item><description><c>target_decal_size</c> real → <see cref="TargetDecalSize"/></description></item>
///   <item><description><c>use_target_decal</c> bool → <see cref="UseTargetDecal"/></description></item>
///   <item><description><c>use_target_highlight</c> bool → <see cref="UseTargetHighlight"/></description></item>
///   <item><description><c>use_target_silhouette</c> bool → <see cref="UseTargetSilhouette"/></description></item>
/// </list>
/// </remarks>
public class ModelType
{
    public uint Id { get; set; }
    public uint SubId { get; set; }
    public string SubType { get; set; } = string.Empty;

    public bool Big { get; set; }
    public float CameraDistance { get; set; }
    public float CameraDistanceForWideAngle { get; set; }
    public bool DespawnDoodadOnCollision { get; set; }
    public float DyingTime { get; set; }
    public int HighImpactFxGroupId { get; set; }
    public int LowImpactFxGroupId { get; set; }
    public int MiddleImpactFxGroupId { get; set; }
    public int MountPoseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public float NameTagOffset { get; set; }
    public bool PlayMountAnimation { get; set; }
    public bool PlayerMountNameTagPos { get; set; }
    public bool Selectable { get; set; }
    public bool ShowNameTag { get; set; }
    public int SoundMaterialId { get; set; }
    public int SoundPackId { get; set; }
    public float TargetDecalSize { get; set; }
    public bool UseTargetDecal { get; set; }
    public bool UseTargetHighlight { get; set; }
    public bool UseTargetSilhouette { get; set; }
}
