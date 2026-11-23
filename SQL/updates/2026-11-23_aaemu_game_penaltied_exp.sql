SET @penaltied_exp_ddl = IF(
  EXISTS (
    SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'characters' AND COLUMN_NAME = 'penaltied_exp'
  ),
  'DO 0',
  'ALTER TABLE `characters` ADD COLUMN `penaltied_exp` INT NOT NULL DEFAULT 0 AFTER `recoverable_exp`'
);
PREPARE penaltied_exp_update FROM @penaltied_exp_ddl;
EXECUTE penaltied_exp_update;
DEALLOCATE PREPARE penaltied_exp_update;
