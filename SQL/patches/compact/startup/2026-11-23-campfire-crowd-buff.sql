-- Restore the 3.5 buff granted by campfire crowd buff 22658.
INSERT OR IGNORE INTO buffs (
    id, name, "desc", duration, tick, icon_id, fx_group_id, kind_id, system, active_weapon_id,
    stack_rule_id, max_stack, save_rule_id, remove_on_death, remove_on_exempt,
    crowd_friendly, crowd_hostile, crowd_number, scale, scaleDuration,
    custom_dual_material_fade_time, reflection_ratio, reflection_target_ratio, psychokinesis_speed, tick_area_angle,
    gliding_fall_speed_fast, gliding_fall_speed_normal, gliding_fall_speed_slow, gliding_land_height,
    gliding_lift_count, gliding_lift_duration, gliding_lift_height, gliding_lift_speed, gliding_lift_valid_time,
    gliding_move_speed_fast, gliding_move_speed_normal, gliding_move_speed_slow, gliding_sliding_time,
    gliding_smooth_time, gliding_startup_speed, gliding_startup_time
) VALUES (
    22659, '사랑이 넘치는 연인들', '모닥불을 쬐며 사랑을 속삭여보세요', 0, 0, 6769, 836, 1, 1, 3,
    1, 2, 1, 1, 1,
    1, 1, 2, 1, 1,
    1, 100, 100, 100, 360,
    3, 2, 1, 0.5,
    2, 0.5, 1.5, 3, 10,
    10, 8, 6, 1,
    0.3, 3, 0.7
);

INSERT OR IGNORE INTO tagged_buffs (id, buff_id, tag_id)
SELECT 56770, 22659, 2480
WHERE NOT EXISTS (SELECT 1 FROM tagged_buffs WHERE buff_id = 22659 AND tag_id = 2480);
