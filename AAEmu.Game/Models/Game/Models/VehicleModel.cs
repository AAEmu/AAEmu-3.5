namespace AAEmu.Game.Models.Game.Models;

/// <summary>
/// Модель транспортного средства из таблицы <c>vehicle_models</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>vehicle_models</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Model.Id"/></description></item>
///   <item><description><c>angVel</c> real → <see cref="AngVel"/></description></item>
///   <item><description><c>auto_level</c> bool → <see cref="AutoLevel"/></description></item>
///   <item><description><c>can_fly</c> bool → <see cref="CanFly"/></description></item>
///   <item><description><c>char_anim_steer_backward_id</c> int → <see cref="CharAnimSteerBackwardId"/></description></item>
///   <item><description><c>char_anim_steer_forward_id</c> int → <see cref="CharAnimSteerForwardId"/></description></item>
///   <item><description><c>collision_box_offset_x</c> real → <see cref="CollisionBoxOffsetX"/></description></item>
///   <item><description><c>collision_box_offset_y</c> real → <see cref="CollisionBoxOffsetY"/></description></item>
///   <item><description><c>collision_box_offset_z</c> real → <see cref="CollisionBoxOffsetZ"/></description></item>
///   <item><description><c>collision_box_scale_x</c> real → <see cref="CollisionBoxScaleX"/></description></item>
///   <item><description><c>collision_box_scale_y</c> real → <see cref="CollisionBoxScaleY"/></description></item>
///   <item><description><c>collision_box_scale_z</c> real → <see cref="CollisionBoxScaleZ"/></description></item>
///   <item><description><c>damaged25</c> text → <see cref="Damaged25"/></description></item>
///   <item><description><c>damaged50</c> text → <see cref="Damaged50"/></description></item>
///   <item><description><c>damaged75</c> text → <see cref="Damaged75"/></description></item>
///   <item><description><c>dead</c> text → <see cref="Dead"/></description></item>
///   <item><description><c>driver_walk</c> bool → <see cref="DriverWalk"/></description></item>
///   <item><description><c>dying</c> text → <see cref="Dying"/></description></item>
///   <item><description><c>floating_height</c> real → <see cref="FloatingHeight"/></description></item>
///   <item><description><c>floating_wave_height</c> real → <see cref="FloatingWaveHeight"/></description></item>
///   <item><description><c>floating_wave_period_ratio</c> real → <see cref="FloatingWavePeriodRatio"/></description></item>
///   <item><description><c>installed_turret</c> bool → <see cref="InstalledTurret"/></description></item>
///   <item><description><c>lin_deaccel_inertia</c> real → <see cref="LinDeaccelInertia"/></description></item>
///   <item><description><c>lin_inertia</c> real → <see cref="LinInertia"/></description></item>
///   <item><description><c>max_climb_ang</c> real → <see cref="MaxClimbAng"/></description></item>
///   <item><description><c>normal</c> text → <see cref="Normal"/></description></item>
///   <item><description><c>rot_deaccel_inertia</c> real → <see cref="RotDeaccelInertia"/></description></item>
///   <item><description><c>rot_inertia</c> real → <see cref="RotInertia"/></description></item>
///   <item><description><c>sound_id</c> int → <see cref="SoundId"/></description></item>
///   <item><description><c>susp_axle</c> bool → <see cref="SuspAxle"/></description></item>
///   <item><description><c>susp_stroke</c> real → <see cref="SuspStroke"/></description></item>
///   <item><description><c>trail_align_ratio</c> real → <see cref="TrailAlignRatio"/></description></item>
///   <item><description><c>turret_pitch_angle_max</c> real → <see cref="TurretPitchAngleMax"/></description></item>
///   <item><description><c>turret_pitch_angle_min</c> real → <see cref="TurretPitchAngleMin"/></description></item>
///   <item><description><c>turret_pitch_angvel</c> real → <see cref="TurretPitchAngvel"/></description></item>
///   <item><description><c>turret_yaw_angle_max</c> real → <see cref="TurretYawAngleMax"/></description></item>
///   <item><description><c>turret_yaw_angle_min</c> real → <see cref="TurretYawAngleMin"/></description></item>
///   <item><description><c>turret_yaw_angvel</c> real → <see cref="TurretYawAngvel"/></description></item>
///   <item><description><c>use_center_spindle</c> bool → <see cref="UseCenterSpindle"/></description></item>
///   <item><description><c>use_proxy_collision</c> bool → <see cref="UseProxyCollision"/></description></item>
///   <item><description><c>use_wheeled_vehicle_simulation</c> bool → <see cref="UseWheeledVehicleSimulation"/></description></item>
///   <item><description><c>velocity</c> real → <see cref="Velocity"/></description></item>
///   <item><description><c>wheel</c> text → <see cref="Wheel"/></description></item>
///   <item><description><c>wheel2</c> text → <see cref="Wheel2"/></description></item>
///   <item><description><c>wheeled_vehicle_ballast_box_x</c> real → <see cref="WheeledVehicleBallastBoxX"/></description></item>
///   <item><description><c>wheeled_vehicle_ballast_box_y</c> real → <see cref="WheeledVehicleBallastBoxY"/></description></item>
///   <item><description><c>wheeled_vehicle_ballast_box_z</c> real → <see cref="WheeledVehicleBallastBoxZ"/></description></item>
///   <item><description><c>wheeled_vehicle_ballast_mass</c> real → <see cref="WheeledVehicleBallastMass"/></description></item>
///   <item><description><c>wheeled_vehicle_ballast_pos_y</c> real → <see cref="WheeledVehicleBallastPosY"/></description></item>
///   <item><description><c>wheeled_vehicle_ballast_pos_z</c> real → <see cref="WheeledVehicleBallastPosZ"/></description></item>
///   <item><description><c>wheeled_vehicle_brake_torque</c> real → <see cref="WheeledVehicleBrakeTorque"/></description></item>
///   <item><description><c>wheeled_vehicle_despawn_on_speed_over</c> bool → <see cref="WheeledVehicleDespawnOnSpeedOver"/></description></item>
///   <item><description><c>wheeled_vehicle_drive</c> int → <see cref="WheeledVehicleDrive"/></description></item>
///   <item><description><c>wheeled_vehicle_friction_ratio</c> real → <see cref="WheeledVehicleFrictionRatio"/></description></item>
///   <item><description><c>wheeled_vehicle_front_optimal_sa</c> real → <see cref="WheeledVehicleFrontOptimalSa"/></description></item>
///   <item><description><c>wheeled_vehicle_gear_speed_ratio_1</c> real → <see cref="WheeledVehicleGearSpeedRatio1"/></description></item>
///   <item><description><c>wheeled_vehicle_gear_speed_ratio_2</c> real → <see cref="WheeledVehicleGearSpeedRatio2"/></description></item>
///   <item><description><c>wheeled_vehicle_gear_speed_ratio_3</c> real → <see cref="WheeledVehicleGearSpeedRatio3"/></description></item>
///   <item><description><c>wheeled_vehicle_gear_speed_ratio_reverse</c> real → <see cref="WheeledVehicleGearSpeedRatioReverse"/></description></item>
///   <item><description><c>wheeled_vehicle_mass</c> real → <see cref="WheeledVehicleMass"/></description></item>
///   <item><description><c>wheeled_vehicle_max_gear</c> int → <see cref="WheeledVehicleMaxGear"/></description></item>
///   <item><description><c>wheeled_vehicle_power</c> real → <see cref="WheeledVehiclePower"/></description></item>
///   <item><description><c>wheeled_vehicle_rear_optimal_sa</c> real → <see cref="WheeledVehicleRearOptimalSa"/></description></item>
///   <item><description><c>wheeled_vehicle_speed_limit</c> real → <see cref="WheeledVehicleSpeedLimit"/></description></item>
///   <item><description><c>wheeled_vehicle_steer</c> int → <see cref="WheeledVehicleSteer"/></description></item>
///   <item><description><c>wheeled_vehicle_susp_damping</c> real → <see cref="WheeledVehicleSuspDamping"/></description></item>
///   <item><description><c>wheeled_vehicle_susp_front_axis_type</c> int → <see cref="WheeledVehicleSuspFrontAxisType"/></description></item>
///   <item><description><c>wheeled_vehicle_susp_rear_axis_type</c> int → <see cref="WheeledVehicleSuspRearAxisType"/></description></item>
///   <item><description><c>wheeled_vehicle_susp_stroke</c> real → <see cref="WheeledVehicleSuspStroke"/></description></item>
/// </list>
/// </remarks>
public class VehicleModel : Model
{
    public float LinInertia { get; set; }
    public float LinDeaccelInertia { get; set; }
    public float RotInertia { get; set; }
    public float RotDeaccelInertia { get; set; }
    public float Velocity { get; set; }
    public float AngVel { get; set; }
    public bool CanFly { get; set; }
    public float WheeledVehicleMass { get; set; }
    public float WheeledVehiclePower { get; set; }
    public float WheeledVehicleBrakeTorque { get; set; }
    public uint WheeledVehicleMaxGear { get; set; }
    public float WheeledVehicleGearSpeedRatioReverse { get; set; }
    public float WheeledVehicleGearSpeedRatio1 { get; set; }
    public float WheeledVehicleGearSpeedRatio2 { get; set; }
    public float WheeledVehicleGearSpeedRatio3 { get; set; }
    public float WheeledVehicleSuspStroke { get; set; }
    public int WheeledVehicleDrive { get; set; }
    public float WheeledVehicleFrontOptimalSa { get; set; }
    public float WheeledVehicleRearOptimalSa { get; set; }

    public bool AutoLevel { get; set; }
    public int CharAnimSteerBackwardId { get; set; }
    public int CharAnimSteerForwardId { get; set; }
    public float CollisionBoxOffsetX { get; set; }
    public float CollisionBoxOffsetY { get; set; }
    public float CollisionBoxOffsetZ { get; set; }
    public float CollisionBoxScaleX { get; set; }
    public float CollisionBoxScaleY { get; set; }
    public float CollisionBoxScaleZ { get; set; }
    public string Damaged25 { get; set; } = string.Empty;
    public string Damaged50 { get; set; } = string.Empty;
    public string Damaged75 { get; set; } = string.Empty;
    public string Dead { get; set; } = string.Empty;
    public bool DriverWalk { get; set; }
    public string Dying { get; set; } = string.Empty;
    public float FloatingHeight { get; set; }
    public float FloatingWaveHeight { get; set; }
    public float FloatingWavePeriodRatio { get; set; }
    public bool InstalledTurret { get; set; }
    public float MaxClimbAng { get; set; }
    public string Normal { get; set; } = string.Empty;
    public int SoundId { get; set; }
    public bool SuspAxle { get; set; }
    public float SuspStroke { get; set; }
    public float TrailAlignRatio { get; set; }
    public float TurretPitchAngleMax { get; set; }
    public float TurretPitchAngleMin { get; set; }
    public float TurretPitchAngvel { get; set; }
    public float TurretYawAngleMax { get; set; }
    public float TurretYawAngleMin { get; set; }
    public float TurretYawAngvel { get; set; }
    public bool UseCenterSpindle { get; set; }
    public bool UseProxyCollision { get; set; }
    public bool UseWheeledVehicleSimulation { get; set; }
    public string Wheel { get; set; } = string.Empty;
    public string Wheel2 { get; set; } = string.Empty;
    public float WheeledVehicleBallastBoxX { get; set; }
    public float WheeledVehicleBallastBoxY { get; set; }
    public float WheeledVehicleBallastBoxZ { get; set; }
    public float WheeledVehicleBallastMass { get; set; }
    public float WheeledVehicleBallastPosY { get; set; }
    public float WheeledVehicleBallastPosZ { get; set; }
    public bool WheeledVehicleDespawnOnSpeedOver { get; set; }
    public float WheeledVehicleFrictionRatio { get; set; }
    public float WheeledVehicleSpeedLimit { get; set; }
    public int WheeledVehicleSteer { get; set; }
    public float WheeledVehicleSuspDamping { get; set; }
    public int WheeledVehicleSuspFrontAxisType { get; set; }
    public int WheeledVehicleSuspRearAxisType { get; set; }
}
