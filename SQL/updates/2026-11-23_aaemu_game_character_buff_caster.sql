-- Keep existing rows untyped. Their numeric caster IDs also contained runtime IDs.
SET @buff_caster_ddl = IF(
  EXISTS (
    SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'character_active_buffs' AND COLUMN_NAME = 'caster_kind'
  ),
  'DO 0',
  'ALTER TABLE `character_active_buffs` ADD COLUMN `caster_kind` TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT ''0=legacy, 1=self, 2=character, 3=unavailable'' AFTER `caster_id`'
);
PREPARE buff_caster_update FROM @buff_caster_ddl;
EXECUTE buff_caster_update;
DEALLOCATE PREPARE buff_caster_update;

SET @buff_caster_ddl = IF(
  EXISTS (
    SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'character_active_buffs' AND COLUMN_NAME = 'caster_life'
  ),
  'DO 0',
  'ALTER TABLE `character_active_buffs` ADD COLUMN `caster_life` CHAR(36) CHARACTER SET ascii COLLATE ascii_bin DEFAULT NULL COMMENT ''Life token for source-death buffs'' AFTER `caster_kind`'
);
PREPARE buff_caster_update FROM @buff_caster_ddl;
EXECUTE buff_caster_update;
DEALLOCATE PREPARE buff_caster_update;
