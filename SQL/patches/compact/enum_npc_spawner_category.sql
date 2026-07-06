/*
 Navicat Premium Dump SQL

 Source Server         : AA-10.0.2.13 CN-game DB
 Source Server Type    : SQLite
 Source Server Version : 3045000 (3.45.0)
 Source Schema         : main

 Target Server Type    : SQLite
 Target Server Version : 3045000 (3.45.0)
 File Encoding         : 65001

 Date: 06/07/2026 08:43:38
*/

PRAGMA foreign_keys = false;

-- ----------------------------
-- Table structure for enum_npc_spawner_category
-- ----------------------------
DROP TABLE IF EXISTS "enum_npc_spawner_category";
CREATE TABLE "enum_npc_spawner_category" (
  "id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  "name" varchar(64) NOT NULL
);

-- ----------------------------
-- Records of enum_npc_spawner_category
-- ----------------------------
INSERT INTO "enum_npc_spawner_category" VALUES (0, 'normal');
INSERT INTO "enum_npc_spawner_category" VALUES (1, 'autoCreated');

-- ----------------------------
-- Auto increment value for enum_npc_spawner_category
-- ----------------------------
UPDATE "sqlite_sequence" SET seq = 1 WHERE name = 'enum_npc_spawner_category';

PRAGMA foreign_keys = true;
