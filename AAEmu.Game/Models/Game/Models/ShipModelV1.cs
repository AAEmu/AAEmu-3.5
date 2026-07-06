namespace AAEmu.Game.Models.Game.Models;

/// <summary>
/// Модель корабля из таблицы <c>ship_models</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>ship_models</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Model.Id"/></description></item>
///   <item><description><c>accel</c> real → <see cref="Accel"/></description></item>
///   <item><description><c>accel_exponent</c> real → <see cref="AccelExponent"/></description></item>
///   <item><description><c>char_anim_steer_backward_id</c> int → <see cref="CharAnimSteerBackwardId"/></description></item>
///   <item><description><c>char_anim_steer_forward_id</c> int → <see cref="CharAnimSteerForwardId"/></description></item>
///   <item><description><c>collision_box_center_x</c> real → <see cref="CollisionBoxCenterX"/></description></item>
///   <item><description><c>collision_box_center_y</c> real → <see cref="CollisionBoxCenterY"/></description></item>
///   <item><description><c>collision_box_center_z</c> real → <see cref="CollisionBoxCenterZ"/></description></item>
///   <item><description><c>collision_box_offset_x</c> real → <see cref="CollisionBoxOffsetX"/></description></item>
///   <item><description><c>collision_box_offset_y</c> real → <see cref="CollisionBoxOffsetY"/></description></item>
///   <item><description><c>collision_box_offset_z</c> real → <see cref="CollisionBoxOffsetZ"/></description></item>
///   <item><description><c>collision_box_scale_x</c> real → <see cref="CollisionBoxScaleX"/></description></item>
///   <item><description><c>collision_box_scale_y</c> real → <see cref="CollisionBoxScaleY"/></description></item>
///   <item><description><c>collision_box_scale_z</c> real → <see cref="CollisionBoxScaleZ"/></description></item>
///   <item><description><c>collision_box_size_x</c> real → <see cref="CollisionBoxSizeX"/></description></item>
///   <item><description><c>collision_box_size_y</c> real → <see cref="CollisionBoxSizeY"/></description></item>
///   <item><description><c>collision_box_size_z</c> real → <see cref="CollisionBoxSizeZ"/></description></item>
///   <item><description><c>collision_sphere_radius</c> real → <see cref="CollisionSphereRadius"/></description></item>
///   <item><description><c>damaged25</c> text → <see cref="Damaged25"/></description></item>
///   <item><description><c>damaged50</c> text → <see cref="Damaged50"/></description></item>
///   <item><description><c>damaged75</c> text → <see cref="Damaged75"/></description></item>
///   <item><description><c>dead</c> text → <see cref="Dead"/></description></item>
///   <item><description><c>halt_rate</c> real → <see cref="HaltRate"/></description></item>
///   <item><description><c>impact_mass</c> real → <see cref="ImpactMass"/></description></item>
///   <item><description><c>keel_height</c> real → <see cref="KeelHeight"/></description></item>
///   <item><description><c>keel_length</c> real → <see cref="KeelLength"/></description></item>
///   <item><description><c>keel_offset_z</c> real → <see cref="KeelOffsetZ"/></description></item>
///   <item><description><c>mass</c> real → <see cref="Mass"/></description></item>
///   <item><description><c>mass_box_size_x</c> real → <see cref="MassBoxSizeX"/></description></item>
///   <item><description><c>mass_box_size_y</c> real → <see cref="MassBoxSizeY"/></description></item>
///   <item><description><c>mass_box_size_z</c> real → <see cref="MassBoxSizeZ"/></description></item>
///   <item><description><c>mass_center_x</c> real → <see cref="MassCenterX"/></description></item>
///   <item><description><c>mass_center_y</c> real → <see cref="MassCenterY"/></description></item>
///   <item><description><c>mass_center_z</c> real → <see cref="MassCenterZ"/></description></item>
///   <item><description><c>max_rpm_sec</c> real → <see cref="MaxRpmSec"/></description></item>
///   <item><description><c>min_rpm_sec</c> real → <see cref="MinRpmSec"/></description></item>
///   <item><description><c>normal</c> text → <see cref="Normal"/></description></item>
///   <item><description><c>passenger_box_offset_x</c> real → <see cref="PassengerBoxOffsetX"/></description></item>
///   <item><description><c>passenger_box_offset_y</c> real → <see cref="PassengerBoxOffsetY"/></description></item>
///   <item><description><c>passenger_box_offset_z</c> real → <see cref="PassengerBoxOffsetZ"/></description></item>
///   <item><description><c>passenger_box_scale_x</c> real → <see cref="PassengerBoxScaleX"/></description></item>
///   <item><description><c>passenger_box_scale_y</c> real → <see cref="PassengerBoxScaleY"/></description></item>
///   <item><description><c>passenger_box_scale_z</c> real → <see cref="PassengerBoxScaleZ"/></description></item>
///   <item><description><c>reverse_accel</c> real → <see cref="ReverseAccel"/></description></item>
///   <item><description><c>reverse_velocity</c> real → <see cref="ReverseVelocity"/></description></item>
///   <item><description><c>steer_vel</c> real → <see cref="SteerVel"/></description></item>
///   <item><description><c>tube_length</c> real → <see cref="TubeLength"/></description></item>
///   <item><description><c>tube_offset_z</c> real → <see cref="TubeOffsetZ"/></description></item>
///   <item><description><c>tube_radius</c> real → <see cref="TubeRadius"/></description></item>
///   <item><description><c>turn_accel</c> real → <see cref="TurnAccel"/></description></item>
///   <item><description><c>velocity</c> real → <see cref="Velocity"/></description></item>
///   <item><description><c>water_damping</c> real → <see cref="WaterDamping"/></description></item>
///   <item><description><c>water_density</c> real → <see cref="WaterDensity"/></description></item>
///   <item><description><c>water_resistance</c> real → <see cref="WaterResistance"/></description></item>
/// </list>
/// </remarks>
public class ShipModelV1 : Model
{
    public float Velocity { get; set; }
    public float Mass { get; set; }
    public float MassCenterX { get; set; }
    public float MassCenterY { get; set; }
    public float MassCenterZ { get; set; }
    public float MassBoxSizeX { get; set; }
    public float MassBoxSizeY { get; set; }
    public float MassBoxSizeZ { get; set; }
    public float WaterDensity { get; set; }
    public float WaterResistance { get; set; }
    public float SteerVel { get; set; }
    public float Accel { get; set; }
    public float ReverseAccel { get; set; }
    public float ReverseVelocity { get; set; }
    public float TurnAccel { get; set; }
    public float TubeLength { get; set; }
    public float TubeRadius { get; set; }
    public float TubeOffsetZ { get; set; }
    public float KeelLength { get; set; }
    public float KeelHeight { get; set; }
    public float KeelOffsetZ { get; set; }

    public float AccelExponent { get; set; }
    public int CharAnimSteerBackwardId { get; set; }
    public int CharAnimSteerForwardId { get; set; }
    public float CollisionBoxCenterX { get; set; }
    public float CollisionBoxCenterY { get; set; }
    public float CollisionBoxCenterZ { get; set; }
    public float CollisionBoxOffsetX { get; set; }
    public float CollisionBoxOffsetY { get; set; }
    public float CollisionBoxOffsetZ { get; set; }
    public float CollisionBoxScaleX { get; set; }
    public float CollisionBoxScaleY { get; set; }
    public float CollisionBoxScaleZ { get; set; }
    public float CollisionBoxSizeX { get; set; }
    public float CollisionBoxSizeY { get; set; }
    public float CollisionBoxSizeZ { get; set; }
    public float CollisionSphereRadius { get; set; }
    public string Damaged25 { get; set; } = string.Empty;
    public string Damaged50 { get; set; } = string.Empty;
    public string Damaged75 { get; set; } = string.Empty;
    public string Dead { get; set; } = string.Empty;
    public float HaltRate { get; set; }
    public float ImpactMass { get; set; }
    public float MaxRpmSec { get; set; }
    public float MinRpmSec { get; set; }
    public string Normal { get; set; } = string.Empty;
    public float PassengerBoxOffsetX { get; set; }
    public float PassengerBoxOffsetY { get; set; }
    public float PassengerBoxOffsetZ { get; set; }
    public float PassengerBoxScaleX { get; set; }
    public float PassengerBoxScaleY { get; set; }
    public float PassengerBoxScaleZ { get; set; }
    public float WaterDamping { get; set; }
}
