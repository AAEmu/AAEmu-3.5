-- The active high abilities belong to a character and survive reconnects.
CREATE TABLE IF NOT EXISTS `character_high_abilities` (
  `owner` INT UNSIGNED NOT NULL,
  `ability1` TINYINT UNSIGNED NOT NULL DEFAULT 13,
  `ability2` TINYINT UNSIGNED NOT NULL DEFAULT 13,
  `ability3` TINYINT UNSIGNED NOT NULL DEFAULT 13,
  PRIMARY KEY (`owner`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
