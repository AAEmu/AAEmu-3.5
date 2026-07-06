/*
 Navicat Premium Dump SQL

 Source Server         : AA-10.0.2.13 CN-game DB
 Source Server Type    : SQLite
 Source Server Version : 3045000 (3.45.0)
 Source Schema         : main

 Target Server Type    : SQLite
 Target Server Version : 3045000 (3.45.0)
 File Encoding         : 65001

 Date: 06/07/2026 08:45:28
*/

PRAGMA foreign_keys = false;

-- ----------------------------
-- Table structure for gacha_advanced_loot_packs
-- ----------------------------
DROP TABLE IF EXISTS "gacha_advanced_loot_packs";
CREATE TABLE "gacha_advanced_loot_packs" (
  "id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  "gacha_loot_pack_id" integer NOT NULL DEFAULT 0,
  "add_round" integer NOT NULL DEFAULT 0,
  "give_term" integer NOT NULL DEFAULT 0,
  "loot_pack_id" integer NOT NULL DEFAULT 0,
  "rate" integer NOT NULL DEFAULT 0,
  "priority" integer NOT NULL DEFAULT 1
);

-- ----------------------------
-- Records of gacha_advanced_loot_packs
-- ----------------------------
INSERT INTO "gacha_advanced_loot_packs" VALUES (1, 1, 0, 9, 11579, 0, 3);
INSERT INTO "gacha_advanced_loot_packs" VALUES (2, 1, 100, 200, 11709, 10000, 2);
INSERT INTO "gacha_advanced_loot_packs" VALUES (3, 1, 200, 400, 11710, 10000, 1);
INSERT INTO "gacha_advanced_loot_packs" VALUES (4, 2, 0, 9, 11579, 0, 3);
INSERT INTO "gacha_advanced_loot_packs" VALUES (5, 2, 100, 200, 11709, 10000, 2);
INSERT INTO "gacha_advanced_loot_packs" VALUES (6, 2, 200, 400, 11710, 10000, 1);
INSERT INTO "gacha_advanced_loot_packs" VALUES (7, 3, 0, 120, 12375, 67000, 2);
INSERT INTO "gacha_advanced_loot_packs" VALUES (8, 3, 10, 600, 12374, 5700, 1);
INSERT INTO "gacha_advanced_loot_packs" VALUES (9, 4, 0, 20, 10865, 100000, 3);
INSERT INTO "gacha_advanced_loot_packs" VALUES (10, 4, 10, 299, 10866, 6700, 2);
INSERT INTO "gacha_advanced_loot_packs" VALUES (12, 4, 10, 2999, 10887, 700, 1);
INSERT INTO "gacha_advanced_loot_packs" VALUES (13, 5, 0, 20, 12330, 70000, 4);
INSERT INTO "gacha_advanced_loot_packs" VALUES (14, 5, 10, 250, 12332, 9000, 3);
INSERT INTO "gacha_advanced_loot_packs" VALUES (15, 5, 20, 1500, 12331, 8000, 2);
INSERT INTO "gacha_advanced_loot_packs" VALUES (16, 5, 30, 2300, 12336, 700, 1);
INSERT INTO "gacha_advanced_loot_packs" VALUES (17, 6, 0, 50, 12422, 2000, 2);
INSERT INTO "gacha_advanced_loot_packs" VALUES (18, 6, 10, 100, 12423, 100, 1);
INSERT INTO "gacha_advanced_loot_packs" VALUES (19, 7, 0, 20, 12424, 5000, 2);
INSERT INTO "gacha_advanced_loot_packs" VALUES (20, 7, 30, 300, 12425, 250, 1);
INSERT INTO "gacha_advanced_loot_packs" VALUES (21, 8, 5, 40, 12426, 2500, 2);
INSERT INTO "gacha_advanced_loot_packs" VALUES (22, 8, 30, 150, 12427, 667, 1);
INSERT INTO "gacha_advanced_loot_packs" VALUES (23, 8, 8, 12, 12450, 20000, 3);
INSERT INTO "gacha_advanced_loot_packs" VALUES (24, 9, 0, 50, 14720, 1520000, 1);
INSERT INTO "gacha_advanced_loot_packs" VALUES (25, 9, 0, 300, 14721, 20000, 2);
INSERT INTO "gacha_advanced_loot_packs" VALUES (26, 10, 30, 60, 14923, 100, 1);
INSERT INTO "gacha_advanced_loot_packs" VALUES (27, 10, 12, 40, 14922, 50000, 2);
INSERT INTO "gacha_advanced_loot_packs" VALUES (28, 10, 8, 15, 14921, 400000, 3);
INSERT INTO "gacha_advanced_loot_packs" VALUES (29, 11, 5, 40, 15154, 300000, 3);
INSERT INTO "gacha_advanced_loot_packs" VALUES (30, 11, 30, 90, 15155, 150000, 2);
INSERT INTO "gacha_advanced_loot_packs" VALUES (31, 11, 75, 140, 15156, 10000, 1);

-- ----------------------------
-- Auto increment value for gacha_advanced_loot_packs
-- ----------------------------
UPDATE "sqlite_sequence" SET seq = 31 WHERE name = 'gacha_advanced_loot_packs';

PRAGMA foreign_keys = true;
