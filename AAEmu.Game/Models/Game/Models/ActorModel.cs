namespace AAEmu.Game.Models.Game.Models;

/// <summary>
/// Модель актёра из таблицы <c>actor_models</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>actor_models</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Model.Id"/></description></item>
///   <item><description><c>actor_height</c> real → <see cref="ActorHeight"/></description></item>
///   <item><description><c>add_box</c> bool → <see cref="AddBox"/></description></item>
///   <item><description><c>animation_graph</c> text → <see cref="AnimationGraph"/></description></item>
///   <item><description><c>attack_start_range</c> real → <see cref="AttackStartRange"/></description></item>
///   <item><description><c>beanstalk_back</c> real → <see cref="BeanstalkBack"/></description></item>
///   <item><description><c>box_x</c> real → <see cref="BoxX"/></description></item>
///   <item><description><c>box_y</c> real → <see cref="BoxY"/></description></item>
///   <item><description><c>box_z</c> real → <see cref="BoxZ"/></description></item>
///   <item><description><c>center_x</c> real → <see cref="CenterX"/></description></item>
///   <item><description><c>center_y</c> real → <see cref="CenterY"/></description></item>
///   <item><description><c>center_z</c> real → <see cref="CenterZ"/></description></item>
///   <item><description><c>face_target_instantly</c> bool → <see cref="FaceTargetInstantly"/></description></item>
///   <item><description><c>fly_mode</c> bool → <see cref="FlyMode"/></description></item>
///   <item><description><c>fx_scale</c> real → <see cref="FxScale"/></description></item>
///   <item><description><c>game_backward_diagonal_multiplier</c> real → <see cref="GameBackwardDiagonalMultiplier"/></description></item>
///   <item><description><c>game_backward_multiplier</c> real → <see cref="GameBackwardMultiplier"/></description></item>
///   <item><description><c>game_bow_look_ik_blend_head</c> real → <see cref="GameBowLookIkBlendHead"/></description></item>
///   <item><description><c>game_bow_look_ik_blend_neck</c> real → <see cref="GameBowLookIkBlendNeck"/></description></item>
///   <item><description><c>game_bow_look_ik_blend_spine1</c> real → <see cref="GameBowLookIkBlendSpine1"/></description></item>
///   <item><description><c>game_bow_look_ik_blend_spine2</c> real → <see cref="GameBowLookIkBlendSpine2"/></description></item>
///   <item><description><c>game_bow_look_ik_blend_spine3</c> real → <see cref="GameBowLookIkBlendSpine3"/></description></item>
///   <item><description><c>game_forward_diagonal_multiplier</c> real → <see cref="GameForwardDiagonalMultiplier"/></description></item>
///   <item><description><c>game_forward_multiplier</c> real → <see cref="GameForwardMultiplier"/></description></item>
///   <item><description><c>game_grab_multiplier</c> real → <see cref="GameGrabMultiplier"/></description></item>
///   <item><description><c>game_inertia</c> real → <see cref="GameInertia"/></description></item>
///   <item><description><c>game_inertia_accel</c> real → <see cref="GameInertiaAccel"/></description></item>
///   <item><description><c>game_jump_height</c> real → <see cref="GameJumpHeight"/></description></item>
///   <item><description><c>game_lean_angle</c> int → <see cref="GameLeanAngle"/></description></item>
///   <item><description><c>game_lean_shift</c> real → <see cref="GameLeanShift"/></description></item>
///   <item><description><c>game_look_ik_blend_head</c> real → <see cref="GameLookIkBlendHead"/></description></item>
///   <item><description><c>game_look_ik_blend_neck</c> real → <see cref="GameLookIkBlendNeck"/></description></item>
///   <item><description><c>game_look_ik_blend_spine1</c> real → <see cref="GameLookIkBlendSpine1"/></description></item>
///   <item><description><c>game_look_ik_blend_spine2</c> real → <see cref="GameLookIkBlendSpine2"/></description></item>
///   <item><description><c>game_look_ik_blend_spine3</c> real → <see cref="GameLookIkBlendSpine3"/></description></item>
///   <item><description><c>game_max_grab_mass</c> int → <see cref="GameMaxGrabMass"/></description></item>
///   <item><description><c>game_max_grab_volume</c> real → <see cref="GameMaxGrabVolume"/></description></item>
///   <item><description><c>game_sprint_multiplier</c> real → <see cref="GameSprintMultiplier"/></description></item>
///   <item><description><c>game_strafe_multiplier</c> real → <see cref="GameStrafeMultiplier"/></description></item>
///   <item><description><c>game_walk_backward_diagonal_multiplier</c> real → <see cref="GameWalkBackwardDiagonalMultiplier"/></description></item>
///   <item><description><c>game_walk_backward_multiplier</c> real → <see cref="GameWalkBackwardMultiplier"/></description></item>
///   <item><description><c>game_walk_forward_diagonal_multiplier</c> real → <see cref="GameWalkForwardDiagonalMultiplier"/></description></item>
///   <item><description><c>game_walk_multiplier</c> real → <see cref="GameWalkMultiplier"/></description></item>
///   <item><description><c>game_walk_strafe_multiplier</c> real → <see cref="GameWalkStrafeMultiplier"/></description></item>
///   <item><description><c>ground_targetable</c> bool → <see cref="GroundTargetable"/></description></item>
///   <item><description><c>hand_rate</c> real → <see cref="HandRate"/></description></item>
///   <item><description><c>height</c> real → <see cref="Height"/></description></item>
///   <item><description><c>hit_power</c> int → <see cref="HitPower"/></description></item>
///   <item><description><c>hrope_down</c> real → <see cref="HropeDown"/></description></item>
///   <item><description><c>model_file</c> text → <see cref="ModelFile"/></description></item>
///   <item><description><c>movement_id</c> int → <see cref="MovementId"/></description></item>
///   <item><description><c>physics_flags</c> int → <see cref="PhysicsFlags"/></description></item>
///   <item><description><c>physics_living_air_resistance</c> real → <see cref="PhysicsLivingAirResistance"/></description></item>
///   <item><description><c>physics_living_collider_mat</c> text → <see cref="PhysicsLivingColliderMat"/></description></item>
///   <item><description><c>physics_living_gravity</c> real → <see cref="PhysicsLivingGravity"/></description></item>
///   <item><description><c>physics_living_k_air_control</c> real → <see cref="PhysicsLivingKAirControl"/></description></item>
///   <item><description><c>physics_living_mass</c> int → <see cref="PhysicsLivingMass"/></description></item>
///   <item><description><c>physics_living_max_climb_angle</c> real → <see cref="PhysicsLivingMaxClimbAngle"/></description></item>
///   <item><description><c>physics_living_max_vel_ground</c> int → <see cref="PhysicsLivingMaxVelGround"/></description></item>
///   <item><description><c>physics_living_min_fall_angle</c> real → <see cref="PhysicsLivingMinFallAngle"/></description></item>
///   <item><description><c>physics_living_min_slide_angle</c> real → <see cref="PhysicsLivingMinSlideAngle"/></description></item>
///   <item><description><c>physics_living_time_impulse_recover</c> real → <see cref="PhysicsLivingTimeImpulseRecover"/></description></item>
///   <item><description><c>physics_mass</c> int → <see cref="PhysicsMass"/></description></item>
///   <item><description><c>physics_stiffness_scale</c> int → <see cref="PhysicsStiffnessScale"/></description></item>
///   <item><description><c>portrait</c> text → <see cref="Portrait"/></description></item>
///   <item><description><c>push_ragdoll</c> bool → <see cref="PushRagdoll"/></description></item>
///   <item><description><c>radius</c> real → <see cref="Radius"/></description></item>
///   <item><description><c>restrict_boarding_mate</c> bool → <see cref="RestrictBoardingMate"/></description></item>
///   <item><description><c>restrict_boarding_slave</c> bool → <see cref="RestrictBoardingSlave"/></description></item>
///   <item><description><c>restrict_climb</c> bool → <see cref="RestrictClimb"/></description></item>
///   <item><description><c>rope_back</c> real → <see cref="RopeBack"/></description></item>
///   <item><description><c>rope_hanging_hand_offset_x</c> real → <see cref="RopeHangingHandOffsetX"/></description></item>
///   <item><description><c>rope_hanging_hand_offset_y</c> real → <see cref="RopeHangingHandOffsetY"/></description></item>
///   <item><description><c>rope_hanging_hand_offset_z</c> real → <see cref="RopeHangingHandOffsetZ"/></description></item>
///   <item><description><c>shared_dummy_model</c> bool → <see cref="SharedDummyModel"/></description></item>
///   <item><description><c>sight_fov</c> real → <see cref="SightFov"/></description></item>
///   <item><description><c>sight_range</c> real → <see cref="SightRange"/></description></item>
///   <item><description><c>slope_alignment</c> bool → <see cref="SlopeAlignment"/></description></item>
///   <item><description><c>turn_speed</c> real → <see cref="TurnSpeed"/></description></item>
///   <item><description><c>underwater_creature</c> bool → <see cref="UnderwaterCreature"/></description></item>
///   <item><description><c>upperbody_graph</c> text → <see cref="UpperbodyGraph"/></description></item>
///   <item><description><c>use_ragdoll</c> bool → <see cref="UseRagdoll"/></description></item>
///   <item><description><c>use_ragdoll_hit</c> bool → <see cref="UseRagdollHit"/></description></item>
///   <item><description><c>use_ragdoll_knock_down</c> bool → <see cref="UseRagdollKnockDown"/></description></item>
///   <item><description><c>use_random_idle_control</c> bool → <see cref="UseRandomIdleControl"/></description></item>
/// </list>
/// </remarks>
public class ActorModel : Model
{
    public float Radius { get; set; }
    public float Height { get; set; }
    public int MovementId { get; set; } // 0 = normal, 1 = mounts, 2 = bird/fish (ignores gravity), 3 = sunk in the ground / hidden underground

    public Dictionary<GameStanceType, GameStance> Stances { get; set; } = [];

    public float ActorHeight { get; set; }
    public bool AddBox { get; set; }
    public string AnimationGraph { get; set; } = string.Empty;
    public float AttackStartRange { get; set; }
    public float BeanstalkBack { get; set; }
    public float BoxX { get; set; }
    public float BoxY { get; set; }
    public float BoxZ { get; set; }
    public float CenterX { get; set; }
    public float CenterY { get; set; }
    public float CenterZ { get; set; }
    public bool FaceTargetInstantly { get; set; }
    public bool FlyMode { get; set; }
    public float FxScale { get; set; }
    public float GameBackwardDiagonalMultiplier { get; set; }
    public float GameBackwardMultiplier { get; set; }
    public float GameBowLookIkBlendHead { get; set; }
    public float GameBowLookIkBlendNeck { get; set; }
    public float GameBowLookIkBlendSpine1 { get; set; }
    public float GameBowLookIkBlendSpine2 { get; set; }
    public float GameBowLookIkBlendSpine3 { get; set; }
    public float GameForwardDiagonalMultiplier { get; set; }
    public float GameForwardMultiplier { get; set; }
    public float GameGrabMultiplier { get; set; }
    public float GameInertia { get; set; }
    public float GameInertiaAccel { get; set; }
    public float GameJumpHeight { get; set; }
    public int GameLeanAngle { get; set; }
    public float GameLeanShift { get; set; }
    public float GameLookIkBlendHead { get; set; }
    public float GameLookIkBlendNeck { get; set; }
    public float GameLookIkBlendSpine1 { get; set; }
    public float GameLookIkBlendSpine2 { get; set; }
    public float GameLookIkBlendSpine3 { get; set; }
    public int GameMaxGrabMass { get; set; }
    public float GameMaxGrabVolume { get; set; }
    public float GameSprintMultiplier { get; set; }
    public float GameStrafeMultiplier { get; set; }
    public float GameWalkBackwardDiagonalMultiplier { get; set; }
    public float GameWalkBackwardMultiplier { get; set; }
    public float GameWalkForwardDiagonalMultiplier { get; set; }
    public float GameWalkMultiplier { get; set; }
    public float GameWalkStrafeMultiplier { get; set; }
    public bool GroundTargetable { get; set; }
    public float HandRate { get; set; }
    public int HitPower { get; set; }
    public float HropeDown { get; set; }
    public string ModelFile { get; set; } = string.Empty;
    public int PhysicsFlags { get; set; }
    public float PhysicsLivingAirResistance { get; set; }
    public string PhysicsLivingColliderMat { get; set; } = string.Empty;
    public float PhysicsLivingGravity { get; set; }
    public float PhysicsLivingKAirControl { get; set; }
    public int PhysicsLivingMass { get; set; }
    public float PhysicsLivingMaxClimbAngle { get; set; }
    public int PhysicsLivingMaxVelGround { get; set; }
    public float PhysicsLivingMinFallAngle { get; set; }
    public float PhysicsLivingMinSlideAngle { get; set; }
    public float PhysicsLivingTimeImpulseRecover { get; set; }
    public int PhysicsMass { get; set; }
    public int PhysicsStiffnessScale { get; set; }
    public string Portrait { get; set; } = string.Empty;
    public bool PushRagdoll { get; set; }
    public bool RestrictBoardingMate { get; set; }
    public bool RestrictBoardingSlave { get; set; }
    public bool RestrictClimb { get; set; }
    public float RopeBack { get; set; }
    public float RopeHangingHandOffsetX { get; set; }
    public float RopeHangingHandOffsetY { get; set; }
    public float RopeHangingHandOffsetZ { get; set; }
    public bool SharedDummyModel { get; set; }
    public float SightFov { get; set; }
    public float SightRange { get; set; }
    public bool SlopeAlignment { get; set; }
    public float TurnSpeed { get; set; }
    public bool UnderwaterCreature { get; set; }
    public string UpperbodyGraph { get; set; } = string.Empty;
    public bool UseRagdoll { get; set; }
    public bool UseRagdollHit { get; set; }
    public bool UseRagdollKnockDown { get; set; }
    public bool UseRandomIdleControl { get; set; }
}
