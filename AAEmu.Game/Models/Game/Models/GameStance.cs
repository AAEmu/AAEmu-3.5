using System.Numerics;

namespace AAEmu.Game.Models.Game.Models;

/// <summary>
/// Игровая стойка/поза актёра из таблицы <c>game_stances</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>game_stances</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Id"/></description></item>
///   <item><description><c>actor_model_id</c> int → <see cref="ActorModelId"/></description></item>
///   <item><description><c>stance_id</c> int → <see cref="StanceId"/></description></item>
///   <item><description><c>name</c> text → <see cref="Name"/></description></item>
///   <item><description><c>ai_move_speed_run</c> real → <see cref="AiMoveSpeedRun"/></description></item>
///   <item><description><c>ai_move_speed_slow</c> real → <see cref="AiMoveSpeedSlow"/></description></item>
///   <item><description><c>ai_move_speed_sprint</c> real → <see cref="AiMoveSpeedSprint"/></description></item>
///   <item><description><c>ai_move_speed_walk</c> real → <see cref="AiMoveSpeedWalk"/></description></item>
///   <item><description><c>height_collider</c> real → <see cref="HeightCollider"/></description></item>
///   <item><description><c>height_pivot</c> real → <see cref="HeightPivot"/></description></item>
///   <item><description><c>ignore_collision</c> bool → <see cref="IgnoreCollision"/></description></item>
///   <item><description><c>max_speed</c> real → <see cref="MaxSpeed"/></description></item>
///   <item><description><c>model_offset_x/y/z</c> real → <see cref="ModelOffset"/></description></item>
///   <item><description><c>normal_speed</c> real → <see cref="NormalSpeed"/></description></item>
///   <item><description><c>size_x/y/z</c> real → <see cref="Size"/></description></item>
///   <item><description><c>use_capsule</c> bool → <see cref="UseCapsule"/></description></item>
///   <item><description><c>view_offset_x/y/z</c> real → <see cref="ViewOffset"/></description></item>
/// </list>
/// </remarks>
public class GameStance
{
    public uint Id { get; set; }
    public GameStanceType StanceId { get; set; }
    public uint ActorModelId { get; set; }
    public string Name { get; set; } = string.Empty; // Not really needed
    public float AiMoveSpeedRun { get; set; }
    public float AiMoveSpeedSlow { get; set; }
    public float AiMoveSpeedSprint { get; set; }
    public float AiMoveSpeedWalk { get; set; }
    public float HeightCollider { get; set; }
    public float HeightPivot { get; set; }
    public bool IgnoreCollision { get; set; }
    public float NormalSpeed { get; set; }
    public float MaxSpeed { get; set; }
    public Vector3 Size { get; set; }
    public Vector3 ModelOffset { get; set; }
    public Vector3 ViewOffset { get; set; }
    public bool UseCapsule { get; set; }
}
