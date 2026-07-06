/*
 Navicat Premium Dump SQL

 Source Server         : AA-10.0.2.13 CN-game DB
 Source Server Type    : SQLite
 Source Server Version : 3045000 (3.45.0)
 Source Schema         : main

 Target Server Type    : SQLite
 Target Server Version : 3045000 (3.45.0)
 File Encoding         : 65001

 Date: 06/07/2026 08:45:34
*/

PRAGMA foreign_keys = false;

-- ----------------------------
-- Table structure for gacha_loot_pack_items
-- ----------------------------
DROP TABLE IF EXISTS "gacha_loot_pack_items";
CREATE TABLE "gacha_loot_pack_items" (
  "id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  "gacha_loot_pack_id" integer NOT NULL DEFAULT 0,
  "kind" integer NOT NULL DEFAULT 0,
  "item_id" integer NOT NULL DEFAULT 0
);

-- ----------------------------
-- Records of gacha_loot_pack_items
-- ----------------------------
INSERT INTO "gacha_loot_pack_items" VALUES (1, 1, 0, 39623);
INSERT INTO "gacha_loot_pack_items" VALUES (2, 1, 1, 39624);
INSERT INTO "gacha_loot_pack_items" VALUES (3, 1, 0, 40843);
INSERT INTO "gacha_loot_pack_items" VALUES (4, 1, 1, 40844);
INSERT INTO "gacha_loot_pack_items" VALUES (5, 2, 0, 39623);
INSERT INTO "gacha_loot_pack_items" VALUES (6, 2, 0, 40843);
INSERT INTO "gacha_loot_pack_items" VALUES (7, 2, 1, 39624);
INSERT INTO "gacha_loot_pack_items" VALUES (8, 2, 1, 40844);
INSERT INTO "gacha_loot_pack_items" VALUES (9, 3, 0, 42333);
INSERT INTO "gacha_loot_pack_items" VALUES (10, 3, 1, 42335);
INSERT INTO "gacha_loot_pack_items" VALUES (11, 4, 0, 42078);
INSERT INTO "gacha_loot_pack_items" VALUES (12, 4, 1, 42336);
INSERT INTO "gacha_loot_pack_items" VALUES (13, 5, 0, 44801);
INSERT INTO "gacha_loot_pack_items" VALUES (14, 5, 1, 44802);
INSERT INTO "gacha_loot_pack_items" VALUES (15, 6, 0, 45505);
INSERT INTO "gacha_loot_pack_items" VALUES (16, 6, 1, 42335);
INSERT INTO "gacha_loot_pack_items" VALUES (17, 7, 0, 45506);
INSERT INTO "gacha_loot_pack_items" VALUES (18, 7, 1, 42336);
INSERT INTO "gacha_loot_pack_items" VALUES (19, 8, 0, 45507);
INSERT INTO "gacha_loot_pack_items" VALUES (20, 8, 1, 44802);
INSERT INTO "gacha_loot_pack_items" VALUES (21, 9, 0, 53078);
INSERT INTO "gacha_loot_pack_items" VALUES (23, 10, 0, 53736);
INSERT INTO "gacha_loot_pack_items" VALUES (24, 10, 1, 53737);
INSERT INTO "gacha_loot_pack_items" VALUES (25, 11, 0, 54461);

-- ----------------------------
-- Auto increment value for gacha_loot_pack_items
-- ----------------------------
UPDATE "sqlite_sequence" SET seq = 26 WHERE name = 'gacha_loot_pack_items';

PRAGMA foreign_keys = true;
