-- Skill activation overrides persist across character sessions.
CREATE TABLE IF NOT EXISTS `skill_active_types` (
  `owner` INT UNSIGNED NOT NULL,
  `skill_id` INT UNSIGNED NOT NULL,
  `active_type` TINYINT UNSIGNED NOT NULL,
  PRIMARY KEY (`owner`, `skill_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
