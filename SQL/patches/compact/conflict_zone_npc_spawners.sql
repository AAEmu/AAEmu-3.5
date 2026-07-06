/*
 Navicat Premium Dump SQL

 Source Server         : AA-10.0.2.13 CN-game DB
 Source Server Type    : SQLite
 Source Server Version : 3045000 (3.45.0)
 Source Schema         : main

 Target Server Type    : SQLite
 Target Server Version : 3045000 (3.45.0)
 File Encoding         : 65001

 Date: 06/07/2026 08:43:44
*/

PRAGMA foreign_keys = false;

-- ----------------------------
-- Table structure for conflict_zone_npc_spawners
-- ----------------------------
DROP TABLE IF EXISTS "conflict_zone_npc_spawners";
CREATE TABLE "conflict_zone_npc_spawners" (
  "id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  "conflict_zone_id" integer NOT NULL,
  "npc_spawner_id" integer DEFAULT 0,
  "zone_state_kind_id" integer DEFAULT 0,
  "spawn_activate" boolean DEFAULT 'f',
  "use_despawn" boolean DEFAULT 'f'
);

-- ----------------------------
-- Records of conflict_zone_npc_spawners
-- ----------------------------
INSERT INTO "conflict_zone_npc_spawners" VALUES (3, 23, 102316, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (5, 26, 56878, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (6, 26, 92570, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (10, 15, 60463, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (11, 15, 64350, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (12, 15, 60497, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (13, 15, 60495, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (14, 15, 60500, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (15, 15, 60496, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (16, 15, 60499, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (17, 15, 60494, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (18, 15, 60498, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (23, 16, 168410, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (24, 19, 168663, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (25, 27, 168433, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (26, 22, 168435, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (29, 14, 72478, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (30, 63, 169344, 1, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (31, 63, 169343, 1, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (32, 61, 189803, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (33, 61, 189802, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (34, 63, 200747, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (47, 133, 204119, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (48, 133, 204540, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (49, 133, 204542, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (50, 133, 205732, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (51, 133, 208471, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (53, 139, 213307, 1, 'f', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (60, 147, 238679, 1, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (61, 147, 238607, 1, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (62, 147, 238568, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (63, 147, 238569, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (64, 147, 238570, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (65, 147, 238571, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (66, 147, 238572, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (67, 147, 238573, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (68, 147, 238574, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (69, 147, 238576, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (70, 147, 238577, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (71, 147, 238578, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (72, 147, 238580, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (73, 147, 238581, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (74, 147, 238610, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (75, 147, 238611, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (76, 147, 238612, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (77, 147, 238613, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (78, 147, 238614, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (79, 147, 238615, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (80, 147, 238616, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (81, 147, 238617, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (82, 147, 238618, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (83, 147, 238619, 2, 't', 't');
INSERT INTO "conflict_zone_npc_spawners" VALUES (84, 147, 238620, 2, 't', 't');

-- ----------------------------
-- Auto increment value for conflict_zone_npc_spawners
-- ----------------------------
UPDATE "sqlite_sequence" SET seq = 84 WHERE name = 'conflict_zone_npc_spawners';

PRAGMA foreign_keys = true;
