/*
 Navicat Premium Dump SQL

 Source Server         : AA-10.0.2.13 CN-game DB
 Source Server Type    : SQLite
 Source Server Version : 3045000 (3.45.0)
 Source Schema         : main

 Target Server Type    : SQLite
 Target Server Version : 3045000 (3.45.0)
 File Encoding         : 65001

 Date: 06/07/2026 08:43:30
*/

PRAGMA foreign_keys = false;

-- ----------------------------
-- Table structure for faction_change_npc_spawners
-- ----------------------------
DROP TABLE IF EXISTS "faction_change_npc_spawners";
CREATE TABLE "faction_change_npc_spawners" (
  "id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  "to_faction_id" integer NOT NULL,
  "from_faction_id" integer NOT NULL,
  "npc_spawner_id" integer NOT NULL
);

-- ----------------------------
-- Records of faction_change_npc_spawners
-- ----------------------------
INSERT INTO "faction_change_npc_spawners" VALUES (1, 114, 148, 21885);
INSERT INTO "faction_change_npc_spawners" VALUES (2, 114, 149, 22122);
INSERT INTO "faction_change_npc_spawners" VALUES (3, 148, 114, 22132);
INSERT INTO "faction_change_npc_spawners" VALUES (4, 148, 149, 21908);
INSERT INTO "faction_change_npc_spawners" VALUES (5, 149, 114, 22131);
INSERT INTO "faction_change_npc_spawners" VALUES (6, 149, 148, 21907);

-- ----------------------------
-- Auto increment value for faction_change_npc_spawners
-- ----------------------------
UPDATE "sqlite_sequence" SET seq = 6 WHERE name = 'faction_change_npc_spawners';

PRAGMA foreign_keys = true;
