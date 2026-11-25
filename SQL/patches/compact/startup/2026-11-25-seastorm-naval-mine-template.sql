-- Restore the missing 3.5 template from game.sqlite3. Its ship bindings and phases are already present.
INSERT INTO doodad_almighties (
    id, childable, climate_id, collide_ship, collide_vehicle, delete_when_not_exist_creator,
    despawn_on_collision, faction_id, force_tod_top_priority, force_up_action, group_id, growth_time,
    load_model_from_world, mark_model, max_time, mgmt_spawn, min_time, model, model_kind_id, name,
    no_collision, once_one_interaction, once_one_man, parentable, pass_through_outerside,
    pass_update_dist, percent, place_area_kind_id, reset_data, restrict_zone_id, save_indun,
    show_minimap, show_name, sim_radius, spawn_fx_group_id, system_doodad, target_decal_size,
    use_creator_faction, use_target_decal, use_target_highlight, use_target_silhouette
) VALUES (
    7367, 1, NULL, 0, 0, 0,
    0, 0, 0, 0, 24, NULL,
    0, '', 700000, 0, 600000, 'cgf://Game/objects/env/01_nuia/001_ndeco/old/barrel_grape_e.cgf', 1, '물거품급 기뢰',
    0, 1, 0, 1, 0,
    0, 100, 0, 0, NULL, 0,
    0, 0, 0, 1994, 0, 0.0,
    0, 0, 1, 0
)
ON CONFLICT(id) DO NOTHING;
