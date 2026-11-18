# Порт движения и ИИ НПС: ArcheageZones → AAC3500 (2026-11-17)

**Цель:** максимально приблизить наземное движение, погоню и pathing к `ArcheageZones` / `ArcheageServer` на плане
[`2026-11-15-zone-physics-implementation-plan.md`](./2026-11-15-zone-physics-implementation-plan.md).

**Статус:** работа продолжается. Полный `NpcMoveStateMachine` и корабли **не** перенесены.

---

## Светофор (NPC movement / AI)

| Область | ArcheageZones | AAC3500 (2026-11-17) |
|--------|---------------|---------------------|
| P4 `GroundResolver` / `GetReferenceHeight` | ✓ | ✓ `NpcGroundResolver`; triangle `NavMeshGround` в walk+spawn settle |
| P6 `WalkerCollision.Clip` | ✓ | ✓ roam/return/chase; **не** `follow_path` patrol (`IsInPatrol`) |
| Stuck tail (`StuckEndMs` 1.2 s) | ✓ | ✓ roam/return/chase (P6); chase/return skip stuck waypoint |
| Client vel при упоре в стену | ✓ | ✓ `MovementAnimSpeedQuant` (нет run-vel без шага ≥2 cm) |
| Roam forbidden BAI | ✓ (nav) | ✓ `CheckImpossibleWalk` в `CalcNextRoamingPosition` |
| Roam eligibility (root / almighty follower) | ✓ | ✓ `RoamEligibilityRules` (`AAEMU_ZONE_IMMOBILE_IDLE`, `AAEMU_ZONE_ALMIGHTY_FOLLOWERS`) |
| Roam detour guard (2× chord + 3 m) | ✓ | ✓ `RoamMovementRules` + A* в roam (2026-11-17) |
| Roam идёт по A* waypoints | ✓ | ✓ `TryPlanRoamLeg` / `RoamingBehavior` (не чистый bee-line к цели) |
| Roam leash (`max roam + 10 m`) | ✓ | ✓ `RoamLeashRules` → `GoToReturn` (idle + roaming) |
| Client roam targets (x2ai offset) | ✓ | ✓ `RoamTargetRules` + `npc_ai_params` roamingMin/Max |
| Roam pipe cut / next leg (AI ticks) | ✓ | ✓ `RoamPipeRules` + player-distance `AiTickMs` |
| Return stuck → straight fallback | ✓ | ✓ skip A* waypoint; straight clip если очередь пуста |
| Path plan w/o legacy geo | ✓ | ✓ `PathPlanningRules` (triangle nav chase/return) |
| Chase path simplify | ✓ | ✓ `AiUtils.BuildPathQueue` — chase/return одна логика |
| Roam target tries | ✓ (4) | ✓ `RoamMovementRules.RoamTargetAttempts` |
| Chase replan 500 ms / 1.5 m | ✓ | ✓ `CombatChaseRules` + `MoveInRange` |
| Flytrap gimmick path | ✓ | ✓ `FlytrapAttackBehavior` Peek/stuck/arrival (2026-11-17) |
| Combat face when in range | ✓ | ✓ `StopChaseAndFace` + `LookTowards` |
| Return home stops at 2 m | ✓ | ✓ `ReturnMovementRules.HomeRadiusMetres`; A* return без P6 |
| Return timeout 20 s → home | ✓ | ✓ `ReturnTimeoutMs`; teleport at `OnCompletedReturn` |
| Straight run short of home | ✓ | ✓ `_returnStoppedShort` + timeout (Zones `EndShortOfHome`) |
| Return keeps new hostiles | ✓ | **частично** сброс aggro на Enter; новое — `CheckAggression` на tick + re-engage домой |
| Straight return climb cap | ✓ 45° | ✓ `ReturnStraightRules` (straight only; `AAEMU_ZONE_BLINK_CLIMB=0`) |
| Unseen floor (>15 m) | ✓ | ✓ **частично** `NpcFootingRules` (`AAEMU_UNSEEN_FLOOR=0`) |
| Structure floors / triangle navmesh | ✓ | **частично:** grounding + `LegPlannerRules.TryStructureLeg` в `PathNode`; полные W7 waypoints still open |
| World spawn Z / footing полный | ✓ | **частично** `NpcSpawnHeightRules` + `NavHeightForSpawn`; `NpcWorldFooting` (World relay opt-in) |
| Zone key at (x,y) for P4/water | ✓ | ✓ `GetReferenceHeight` / `MovementStance` (не только `Transform.ZoneId`) |
| Skill SC grounding | ✓ | ✓ dash/leap/wander/float/knock/impulse/blink |
| `Simulation` path | ✓ | ✓ квест/GM legacy; **не** дублирует open-world `AIPath_*` (`ShouldMirrorPathInSimulation`); квест `follow_path` → `AiPathHandler` (`NpcControlEffect`); `AiPathHandler` — open-world патруль |
| GM диагностика | — | ✓ `/npc ground` (+ Ai/path spawner) |
| FollowPath + HoldPosition | ✓ | ✓ `GoToDefaultBehavior` → `FollowPath`; load path до `GoToSpawn` |
| Designer paths `editor/npc.xml` | ✓ | ✓ `ZoneAiPathIndex` + `npc_spawners.g`; `Data/Path/*.path` выкл. (opt-in `AAEMU_LEGACY_DISK_PATH=1`, не для `AIPath_*`) |
| json vs client spawn (same zone) | ✓ | ✓ `PathWalkerRules.JsonPatrolSupersededByClient` + `SpawnManager` skip |
| Idle → patrol после return | ✓ | ✓ `IdleBehavior.Enter` → `EnsureFollowPathLoaded` / `GoToFollowPath` |
| follow_path look-ahead 10 m | ✓ | ✓ `PathFollowRules.TrimLookAhead` |
| patrol waypoint arrival | ✓ | ✓ `PatrolWaypointArrivalMetres` 2.5 m (`AiPathHandler`); Simulation checkpoints 10 m |
| `Rec._Speed` × base run speed | ✓ | ✓ `PathFollowRules.LegMetresPerSecond` / `AiPathHandler` (2026-11-17) |
| PathWalkers open world | ✓ | ✓ `PathWalkerRules` (+ opt-in native via `AAEMU_ZONE_OPEN_WORLD_PATROL=0`) |
| Placement anchor `LaidAt` | ✓ | ✓ `PlacementAnchorLocal` / `PlacementSpawnLocal` из `.g` |
| `NpcMoveStateMachine` | ✓ | ✗ AI v2 behaviors |

---

## Ключевые файлы

| Файл | Роль |
|------|------|
| `NpcWalker.cs` | `StepAlongGround`, `ResolveWalkHeight`, stuck, unseen floor |
| `NpcFootingRules.cs` | детект и удержание невидимого пола |
| `NpcWorldFooting.cs` | API для World relay / `WZNpcState` |
| `WorldNpcRelay.cs` | точка входа для будущего `WZNpcState` decode |
| `CombatChaseRules.cs` | throttle `FindPath` |
| `WalkerCollision.cs` | `StuckEndMs`, `StuckSpeed`, `Clip` |
| `RoamingBehavior.cs` | stuck, waypoint legs (`TryPlanRoamLeg`), поздняя анимация roam |
| `AiPathHandler.cs` | snap Z, stuck skip waypoint |
| `BaseCombatBehavior.MoveInRange` | chase + hysteresis + face |
| `NpcGroundSubCommand.cs` | GM ground dump |
| `RoamMovementRules.cs` | detour, mesh walkable, acceptable path |
| `NpcSpawnHeightRules.cs` | Z спавна из `.g` + `NpcSpawnerNpc` |
| `ClientNpcSpawnerLoader.cs` | runtime `npc_spawners.g` (Solzreed 129) |
| `ZoneAiPaths.cs` / `AiPathsManager` | parse/cache `level_design/zone/{key}/editor/npc.xml` |
| `PathFollowRules.cs` | look-ahead, quest follow env, `AIPath_{zone}_` parsing |
| `LegPlannerRules.cs` / `PathNode.cs` | triangle `NavPlanner`, off-mesh roam leg |
| `StructureFloorRules.cs` | пороги structure floor |
| `AiGeodataManager.TryGroundNear` / `TryStructureFloor` | nav-layer на `.bai` |

**Юнит-тесты (добавлены):** `WalkerCollisionScopeTests`, `WalkerStuckPatrolTests`, `RoamMovementRulesTests`, `NpcFootingRulesTests` (расш.), `NpcWorldFootingTests`, `ZoneAiPathsTests`, `PathFollowRulesTests`, `PathWalkerRulesTests`, `AiPathsManagerDesignerTests`, `SolzreedArthurSpawnerTests`, `SolzreedSpawnerFileTests` (129+179 path resolve).

**Проверка сборки:** `dotnet build AAEmu.Game`; регрессия движения: `dotnet test AAEmu.UnitTests` (ожидаемые 4 fail в Login, не в Game).  
Изолированно (game exe не блокирует DLL): `Scripts/verify_npc_movement_tests.ps1`. Solzreed live: `Scripts/start_game_solzreed.ps1` (сборка в `_build_probe`).

---

## Откуда берётся спавн НПС (портированный пайплайн)

**Приоритет данных (ArcheageZones):**  
`npc_spawners.g` → `editor/npc.xml` → `AiPathHandler` → `npc_spawns.json` (fallback).

| Шаг | Источник | Когда |
|-----|----------|--------|
| 1 | **`npc_spawners.g`** | `SpawnManager.Load()` **сначала** — `ClientNpcSpawnerLoader`: зоны из `ClientNpcSpawnZoneKeys` / `AAEMU_CLIENT_SPAWN_ZONES` (пусто = все зоны `world.xml`, где есть файл на диске/в ClientData). `spawnerType` → SQLite, `pathName`/`pointNo` → `FollowPath`, `PlacementAnchorLocal` / `PlacementSpawnLocal` для `LaidAt`. |
| 2 | **`editor/npc.xml`** | Не спавн: при `NpcAi.TryLoadDesignerPath` / `EnsureFollowPathLoaded` — точки `AIPath_{zone}_*`, `Rec._Speed`, loop. |
| 3 | **`AiPathHandler`** | Тик патруля: `FollowPathBehavior` → `RunCurrentPath` (скорость `PathFollowRules.LegMetresPerSecond`, not `Simulation` для open-world `AIPath_*`). |
| 4 | **`npc_spawns*.json`** | **После** `.g`: остальной мир и зоны без client-файла. Пропуск: `AIPath_*` в зонах с `.g`, дубликат `spawnerId`/XY уже из `.g`. |

Загрузка на диске: `ClientNpcSpawnerLoader.LoadNpcSpawners` → `LoadNpcSpawns` (см. `SpawnManager.Load()`).

**Появление экземпляра (runtime):**

| Режим | Механизм | Вкл. |
|-------|----------|------|
| **Zones (по умолчанию при `ClientNpcSpawnEnabled`)** | `ZoneNpcSpawnOrchestrator`: очередь слотов сразу после `SpawnManager.Load`, тик 100 ms, до 32 `DoSpawn`/тик — **без** `IsPlayerInSpawnRadius` | `AAEMU_ZONE_NPC_SPAWN` ≠ `0`, не `AAEMU_LEGACY_NPC_SPAWN=1` |
| **Legacy (json + отладка)** | `ActiveRegionTick` (1 s) → `NpcSpawners` в радиусе игрока → `Update` → `DoSpawn` | `AAEMU_LEGACY_NPC_SPAWN=1` или client spawn выкл. |

Client-`.g` в режиме Zones **не** регистрируется в `SpawnManager.NpcSpawners` — только в оркестраторе (`ZonePopulationManaged`). Json fallback по-прежнему через legacy tick. **Respawn:** `CheckRespawns` → `EnqueueRespawn`; **min_population:** на смерти (`DoDespawn`) → `EnqueueMinPopulationRespawn` (один слот сразу; живые = не `IsDead`, труп в списке не считается); **расписание:** каждую 1 с `TickZonePopulationSchedule` (despawn по game-hours, без despawn при уходе игрока). После спавна: `NpcSpawnerNpc.Spawn` → footing → `npc.xml` → `FollowPath` / `AiPathHandler`.

| Проверка | Arthur (tpl 11999) |
|----------|-------------------|
| Лог старта | `Solzreed (zone 179): … registered …`, `Client npc_spawners.g: …`, `NPC spawn pipeline for main_world: N npc_spawners.g → …; M npc_spawns.json fallback` |
| `/npc ground` | `spawn=client.g`, `pipeline=client.g→npc.xml→AiPathHandler`, `spawnerId=13257`, `followPath=AIPath_179_moang_29` |
| Без client zones | `spawn=npc_spawns.json`, патруль всё ещё из `npc.xml`, если `FollowPath=AIPath_179_*` |

---

## Смоук-тест (клиент 3.5)

1. **Solzreed (zones 129, 179):** `ClientNpcSpawnZoneKeys: [129, 179]` или env `AAEMU_CLIENT_SPAWN_ZONES=129,179`; зона **179** — Forest Keeper Arthur: `npc_spawners.g` spawnerType **13257** → `AIPath_179_moang_29` в `editor/npc.xml` (`src=npc.xml`, `loop=True`, `/npc ground`); зона **129** — roam/патрули `AIPath_129_*`. Скрипт: `Scripts/start_game_solzreed.ps1`.
2. Лестница / мост в зоне с собранной статикой: НПЦ стоит на поле кисти, не на чистом heightmap.
3. Агро: моб подбегает с hysteresis, останавливается лицом к цели в melee.
4. Fear / knockback: Z остаётся на слое P4, без провала в −0 навмеш.
5. Высокий спавн (инстанс / палуба): `unseenFloor` в `/npc ground`, НПЦ не падает на terrain при roam.

---

## Delta (2026-11-17, cont.)

- **Return:** `ReturnTimeoutMs`, `_returnStoppedShort`, `TeleportHomeHeight` / `TeleportEndsWalk`, planned-return stuck, `ReturnKeepsNewHostiles` (+ aggression на tick).
- **Chase/path:** `BuildPathQueue` unify; flytrap Peek/stuck; `MovementAnimSpeedQuant` (нет run без шага).
- **P4:** triangle `NavHeightForSpawn`; zone key `(x,y)` в walk/snap/roam/chase/return; idle ≠ hold для `GetReferenceHeight`.
- **Spawn:** `AAEMU_ZONE_RESPAWN_FROM_DEATH`, delayed `QueuedSpawn.NotBeforeUtc`.
- **Roam:** snap Z в `EndRoamLeg`; idle snap при `InitializeIdleState`.
- **Simulation:** `SyncFromAiPath` через `SnapPathPoint`.
- **Тесты:** Roam/Leash/Target build fix; `WalkerStuckPatrol` (planned return); `WorldManagerNavHeightTests`.

---

## Оставшиеся работы (приоритет)

| # | Работа | Источник |
|---|--------|----------|
| N1 | Смоук N1–N5 на live, журнал `NpcGround` у проблемных tplId | W9 |
| N2 | `WorldStartZ` / stream spawn height | **частично:** `WorldNpcRelay`, `NpcWorldFooting`, spawner + deferred `SettledZ(worldZ)` |
| N3 | Navmesh **triangle** ground в `NpcGroundResolver` | **частично:** walk+spawn settle wired; W7 structure grids in planner |
| N4 | Structure waypoint floors | Zones W7 |
| N5 | Порт `NpcMoveStateMachine` или поэтапная замена behaviors | Zones AI |
| N6 | `HullSystem` | W5 |

---

## Переменные окружения

| Переменная | Значение |
|------------|----------|
| `AAEMU_WALKER_COLLISION=0` | отключить обрезку шага у стен |
| `AAEMU_UNSEEN_FLOOR=0` | отключить невидимый пол |
| `AAEMU_ROAM_DETOUR=0` | отключить отказ от длинных roam-маршрутов |
| `AAEMU_ZONE_ROAM_LEASH=0` | не принуждать return home после ухода дальше 15 m от spawn |
| `AAEMU_ZONE_CLIENT_ROAM_TARGETS=0` | круговой roam вместо per-axis offset (x2ai) |
| `AAEMU_ZONE_ROAM_AI_TICKS=0` | roam-пауза 3–6 с, без pipe-cut по timeout |
| `AAEMU_STRUCTURE_FLOOR=0` / `AAEMU_ZONE_STRUCTURE_FLOORS=0` | отключить полы сооружений (ground + structure legs) |
| `AAEMU_TRIANGLE_NAV=0` | узловый fallback вместо `NavPlanner` |
| `AAEMU_CLIENT_NPC_SPAWN=0` | не читать `npc_spawners.g` |
| `AAEMU_ZONE_NPC_SPAWN=0` | client `.g` в legacy `SpawnManager` + radius |
| `AAEMU_LEGACY_NPC_SPAWN=1` | только json/radius spawn |
| `AAEMU_ZONE_MIN_POPULATION=0` | не дозаполнять слоты сразу при падении ниже min_population |
| `AAEMU_ZONE_RESPAWN_FROM_DEATH=0` | respawn после снятия трупа (zone), не death+`spawn_delay` |
| `AAEMU_ZONE_RETURN_NEW_HOSTILES=0` | сброс aggro на старте return; иначе reserved attack после домой |
| `AAEMU_CLIENT_SPAWN_ZONES=129` | только указанные zone key (Solzreed), через запятую |
| `AAEMU_OFF_MESH_ROAM=0` | не строить прямой short leg вне mesh-route |
| `AAEMU_ROAM_WALL_CHECK=0` | не отбрасывать roam-цель за стеной (по умолчанию on при `StaticGeometryEnabled`) |
| `AAEMU_ZONE_RETURN_STOPS_SHORT=0` | подходить к home вплотную (не 2 m) |
| `AAEMU_ZONE_RETURN_WITHOUT_ROUTE=0` | телепорт home, если A* не нашёл маршрут (без straight fallback) |
| `AAEMU_ZONE_TELEPORT_HOME_HEIGHT=0` | при return-телепорте сохранить текущий Z (не spawn Z) |
| `AAEMU_ZONE_TELEPORT_ENDS_WALK=0` | не сбрасывать patrol/stand после return-телепорта |
| `AAEMU_ZONE_BLINK_CLIMB=0` | прямой return без обрезки на крутом рельефе |
| `AAEMU_ZONE_QUEST_FOLLOW_PATH=0` | отключить designer follow_path в command sets |
| `AAEMU_ZONE_OPEN_WORLD_PATROL=0` | не входить в FollowPath по `AIPath_*` (только PathWalkers) |
| `AAEMU_ZONE_GROUP_PATH_WALK=0` | отключить underwater group path walks |
| `AAEMU_ZONE_IMMOBILE_IDLE=0` | не блокировать roam у rooted/stun/sleep |
| `AAEMU_ZONE_ALMIGHTY_FOLLOWERS=0` | не блокировать roam у последователей Almighty |
| `AAEMU_LEGACY_DISK_PATH=1` | грузить маршруты из `Data/Path/*.path` (не `AIPath_*`; designer всегда из `npc.xml`) |

Пайплайн: **`Configurations/World.json`** → `ClientNpcSpawnEnabled`, `ClientNpcSpawnZoneKeys`. Патрули — имена из `npc.xml` (`AIPath_{zone}_*`), не `Data/Path/*.path`. JSON только там, где нет `.g`.  
Переменные **`AAEMU_CLIENT_*`** в окружении процесса имеют приоритет (см. `Scripts/start_game_solzreed.ps1`).

Config: `World.json` → `StaticGeometryEnabled`, `WalkerCollisionEnabled`.