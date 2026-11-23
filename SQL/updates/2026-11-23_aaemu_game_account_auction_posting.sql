-- NULL retains the previous posting access for existing accounts.
SET @auction_posting_ddl = IF(
  EXISTS (
    SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'accounts' AND COLUMN_NAME = 'auction_post_allowed'
  ),
  'DO 0',
  'ALTER TABLE `accounts` ADD COLUMN `auction_post_allowed` TINYINT UNSIGNED DEFAULT NULL COMMENT ''Explicit account-wide auction posting status'''
);
PREPARE auction_posting_update FROM @auction_posting_ddl;
EXECUTE auction_posting_update;
DEALLOCATE PREPARE auction_posting_update;

SET @auction_posting_ddl = IF(
  EXISTS (
    SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'accounts' AND COLUMN_NAME = 'auction_post_changed_at'
  ),
  'DO 0',
  'ALTER TABLE `accounts` ADD COLUMN `auction_post_changed_at` DATETIME(6) DEFAULT NULL'
);
PREPARE auction_posting_update FROM @auction_posting_ddl;
EXECUTE auction_posting_update;
DEALLOCATE PREPARE auction_posting_update;
