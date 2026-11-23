SET @heir_progression_ddl = IF(
  EXISTS (
    SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'characters' AND COLUMN_NAME = 'heir_exp'
  ),
  'DO 0',
  'ALTER TABLE `characters` ADD COLUMN `heir_exp` INT UNSIGNED NOT NULL DEFAULT 0 AFTER `experience`'
);
PREPARE heir_progression_update FROM @heir_progression_ddl;
EXECUTE heir_progression_update;
DEALLOCATE PREPARE heir_progression_update;

CREATE TABLE IF NOT EXISTS `character_heir_skills` (
  `owner` int unsigned NOT NULL,
  `heir_group_id` smallint unsigned NOT NULL,
  `successor_id` smallint unsigned NOT NULL,
  PRIMARY KEY (`owner`,`heir_group_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Selected Heir skills';
