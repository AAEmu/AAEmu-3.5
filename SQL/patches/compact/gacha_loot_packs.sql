/*
 Navicat Premium Dump SQL

 Source Server         : AA-10.0.2.13 CN-game DB
 Source Server Type    : SQLite
 Source Server Version : 3045000 (3.45.0)
 Source Schema         : main

 Target Server Type    : SQLite
 Target Server Version : 3045000 (3.45.0)
 File Encoding         : 65001

 Date: 06/07/2026 08:45:41
*/

PRAGMA foreign_keys = false;

-- ----------------------------
-- Table structure for gacha_loot_packs
-- ----------------------------
DROP TABLE IF EXISTS "gacha_loot_packs";
CREATE TABLE "gacha_loot_packs" (
  "id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  "loot_pack_id" integer NOT NULL DEFAULT 0,
  "name" varchar(255) NOT NULL DEFAULT '',
  "active" boolean NOT NULL DEFAULT 't'
);

-- ----------------------------
-- Records of gacha_loot_packs
-- ----------------------------
INSERT INTO "gacha_loot_packs" VALUES (1, 11580, 'Old data 1', 't');
INSERT INTO "gacha_loot_packs" VALUES (2, 11580, 'test02', 'f');
INSERT INTO "gacha_loot_packs" VALUES (3, 10863, '42333.41-50_gacha', 't');
INSERT INTO "gacha_loot_packs" VALUES (4, 10864, '42078.51-55_gacha', 't');
INSERT INTO "gacha_loot_packs" VALUES (5, 12329, '44801-dynamic_box', 't');
INSERT INTO "gacha_loot_packs" VALUES (6, 12419, '45505_live_2018.03_copper_box', 't');
INSERT INTO "gacha_loot_packs" VALUES (7, 12420, '45506_live_2018.03_silver_box', 't');
INSERT INTO "gacha_loot_packs" VALUES (8, 12421, '45507_live_2018.03_gold_box', 't');
INSERT INTO "gacha_loot_packs" VALUES (9, 14719, 'AACN_G 팔로스의 뽑기 상자', 'f');
INSERT INTO "gacha_loot_packs" VALUES (10, 14920, '53736_잠긴 둔의 상자', 't');
INSERT INTO "gacha_loot_packs" VALUES (11, 15153, '정화되지 않은 상자 (23.05.18)', 't');

-- ----------------------------
-- Auto increment value for gacha_loot_packs
-- ----------------------------
UPDATE "sqlite_sequence" SET seq = 11 WHERE name = 'gacha_loot_packs';

PRAGMA foreign_keys = true;
