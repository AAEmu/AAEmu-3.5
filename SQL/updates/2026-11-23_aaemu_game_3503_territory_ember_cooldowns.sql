-- The server persists the acquisition and return deadlines for territorial Memory Embers.
-- Character saves use REPLACE, so this table must not cascade deletes from characters.
CREATE TABLE IF NOT EXISTS `character_skill_cooldowns` (
  `character_id` INT UNSIGNED NOT NULL,
  `skill_id` INT UNSIGNED NOT NULL,
  `duration` INT UNSIGNED NOT NULL,
  `ends_at` DATETIME(3) NOT NULL,
  PRIMARY KEY (`character_id`, `skill_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
