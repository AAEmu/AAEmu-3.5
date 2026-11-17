# Внедрение «физики зон» в текущий проект (AAC3500 / AAEmu 3.5.0.3)

**Дата:** 2026-11-15
**Исходный документ:** [`2026-11-15-zone-server-physics-research.md`](./2026-11-15-zone-server-physics-research.md)
**Статус:** план внедрения; код проекта не менялся.
**Решения владельца проекта (2026-11-15):** база — общий пакет `AAEmu.Geometry` (вариант S3);
цель — максимальная фиделити: настоящие CryPhysics-прокси (`MeshPhysicsData`) + правила стансов воды.

---

## 1. Резюме

Исследовательский отчёт верно поставил вопрос («может ли “сервер зон” заменить нашу физику?») и верно
пришёл к трём вариантам A/B/C. Но при сверке отчёта с **нашим фактическим кодом** выяснилось четыре
вещи, которые переписывают план:

1. **Отчёт составлен не по AAC3500, а по соседнему дереву нашей же 3.5-линии.** Он ссылается на
   `AAEmu.Game/Physics/GodotGeo/*`, `Source/CgfModelReader.cs` и
   `AAEmu.Game/Services/NpcSpawnAuthoringImportService.cs` — **в AAC3500 таких файлов нет**
   (проверено: `git log --all -- "*GodotGeo*"` — 0 совпадений, `Services/NpcSpawnAuthoringImportService.cs`
   отсутствует). Все эти файлы лежат в
   `D:\DEVELOPMENT\archeage\SRC\_melcosoft\archeage\game-server-3.5.0.3` (ветка `3.5`, private
   gitlab `melc.cc/archeage/game-server-3.0.git`). Вывод: **вариант C отчёта уже реализован в
   melc-дереве** — читать надо оттуда, а не из ArcheageZones.

2. **Что уже сделано в melc-дереве (и чего нет в AAC3500):**
   * `AAEmu.Game/Physics/GodotGeo/**` — чтение настоящей геометрии ячеек 3.5-клиента:
     `CgfModelReader.cs` (39 КБ), `CellObjectReader.cs` (57 КБ), `CellEntityReader.cs` (14 КБ),
     `TerrainCellReader.cs` (6 КБ), `GodotGeoNpcMotor` (23 КБ), `GodotGeoNavigator` (28 КБ);
   * `AAEmu.Game/Physics/WorldStaticGeometryIndex.cs` (89 КБ) + `StaticGeometryVerticalIndex.cs` (14 КБ)
     — индекс статической геометрии с рейкастами;
   * `AAEmu.Game/Models/Game/NPChar/NpcHeightResolver.cs` (921 стр.) + `NpcHeightState.cs` +
     `NpcWaterSurfaceOverrides.cs` + `NpcSpawnHeightOverrides.cs` — **этих четырёх файлов в AAC3500 нет**;
   * вендорённый `AAEmu.Game/Models/CryEngine/CgfConverter/**` с `MeshPhysicsData800Reader.cs` —
     **чтение настоящих CryPhysics-прокси** (то, что отчёт называет «более сильной» реализацией
     `CgfPhysicsReader` у ArcheageZones);
   * ~40 тестовых файлов по геометрии/физике/НПС (в melc-дереве — 4176 зелёных тестов);
   * подтверждённый боевой результат: «NPC идёт по видимой поверхности» (лестницы многоэтажки,
     ячейка `027_007`; отчёт `Docs/reports/2026-10-05-GodotGeo-Replacement-Result.md`).

3. **Деревья разошлись в обе стороны.** Общей git-истории нет (`git cat-file 04b204caf` → «not a valid
   object»); из 2452 `.cs` в AAC3500 побайтово совпадают с melc-деревом только 6, в melc на 1220
   файлов больше. AAC3500 содержит проработку **протокола/структур пакетов 3.5**
   (`SCUnitMovementsPacket`, `SCGameRuleConfigPacket` — в melc-версии короче), melc-дерево —
   проработку **геометрии, физики и ИИ НПС**. Отсюда и решение: общий пакет `AAEmu.Geometry` (§4.1).

4. **Клиентские данные 3.5 лежат локально** — `E:\games\MAA-3500\game_pak` (35 ГБ, единый AAPak,
   читается на месте через `AAEmu.Commons/Utils/AAPak/AAPak.cs`). Любой порт проверяется на
   настоящих данных.

**Итог по вариантам исходного отчёта:** A/B (зонехост) отклоняются — они требуют нативный CN 10.0
dedicate (в `_AA-retail\zone-3.5\dedicate` лежат только `.bat`-скрипты, бинарей нет) и контент эры
10.0, то есть отказ от 3.5.0.3. Вариант C реализуется, но не «портированием 10.x-кода
ArcheageZones», а **выделением уже существующей 3.5-подсистемы геометрии в общий пакет** и
доведением её до целевой фиделити (прокси-геометрия + стансы воды).

---

## 2. Что в исходном отчёте верно, а что требует уточнения

| Тезис отчёта | Проверка по коду | Итог |
|---|---|---|
| Физика в ArcheageZones — не движок, а кинематика + рейкасты + хулл 3-DOF | `Core/Sim/Physics/*`, `Core/Data/ZoneHeightmap.cs` | Верно, но применимость нам — только как источник идей |
| Корабли в их Game — «правила без тела», передача хулла через швы зон | `BoatSeam*Rules` в ArcheageServer | Верно, но нерелевантно: у нас Jitter2-динамика и своя репликация (`ShipMoveType`, `ReplicationSmoothing`) |
| «Jitter2 применяется к НПС как дорогой механизм запросов» | **Неверно для AAC3500:** Jitter2 у нас только для кораблей (`ShipController.Build` → `BoxShape`); тел НПС нет вообще (блок `PhysicsManager.cs:222-231` закомментирован) | Переформулировать: Jitter2 = корабли; НПЦ = чистая кинематика по навмешу `.bai` |
| «Эксперимент `AAEmu.Game/Physics/GodotGeo/*` (`CgfModelReader.cs` читает рендер-геометрию и пропускает физ-прокси)» | Эксперимента в AAC3500 **нет**. Он есть в melc-дереве, и там прокси **уже читаются** (`MeshPhysicsData800Reader`, `PhysicsData`) | Ссылка отчёта указывала на другое дерево |
| «Наш `NpcSpawnAuthoringImportService` рекурсивно парсит `npc_spawners.g`» | В AAC3500 такого сервиса нет; спавнеры приходят из SQLite-таблицы `npc_spawners` (`GameData/NpcGameData.cs:107`). Сервис есть в melc-дереве | То же: отчёт описывает melc-дерево |
| «Портировать `ZoneHeightmap` + `CgfPhysicsReader` + `StaticCollision` + `ZoneWater` в наш `Physics/`» | `ZoneHeightmap` дублирует наш `WorldCell.LoadCellHeightMapFromClientData` + `WorldTemplate.GetHeight`; `ZoneWater` дублирует наш `WaterBodies`; `StaticCollision` дублирует melc-овский `WorldStaticGeometryIndex` | Идея верна, но переносить надо **из melc-дерева**; из ArcheageZones берём только недостающие куски (прокси-примитивы `CgfPhysicsReader`, 379 LOC) |
| Спайк: извлечь `zone_server/` из 3.5 `game_pak` и сверить счётчики спавнеров | Для задачи «физика» не требуется | Заменяется прямым чтением геометрии (инструменты melc-дерева готовы) |
| Зоны 129..405 = зоны `main_world` | Подтверждено: это **zone key** из `game/worlds/main_world/world.xml` (`XmlWorldZone.Id` → `WorldTemplate.ZoneKeys`, `ZoneKeyByRegions`); в SQLite `zones.id` — другая нумерация (`ZoneManager._zoneIdToKey`) | Верно, но важно не путать два id-пространства |

---

## 3. Состояние AAC3500 (карта дефицита)

### 3.1 Что уже есть и годится как фундамент

| Подсистема | Файл | Оценка |
|---|---|---|
| Чтение `heightmap.dat` (CryEngine `Hmap`) | `AAEmu.Game/Models/Game/World/WorldCell.cs:135-225` | Готово. Квантование `ushort` через `HeightMaxCoefficient`, 512×512 на ячейку (0.5 м) |
| Билинейная высота рельефа | `WorldTemplate.GetHeight` (`WorldTemplate.cs:174`), `GetRawHeightMapHeight` (`:118`) | Готово |
| Чтение `object.dat` (кисти/воксели/объёмы воды) | `Models/CryEngine/Objects/ObjectsFile.cs` + `ObjectDataType1Brush` | Готово, **но теряется связь кисти с asset-path**: индекс ассета лежит в `ObjectDataType1Brush.PathId` (`offset+0x7F`), трансформация — в `Matrix3x4` (`offset+0x47`) |
| Зоны/ячейки/секторы | `Models/Game/World/Xml/XmlWorld*.cs` | Готово — покрывает потребность `ZoneLevelInfo` у ArcheageZones |
| Вода (реки/озёра) | `Models/Game/World/WaterBodies.cs` (`IsWater`, `GetWaterSurface`, `AddFromCellData`) | Готово; **правил станса (swim/stand) нет** — это цель P5 |
| Высота НПЦ | `Core/Managers/AiGeodataManager.cs:259-333` (ближайший узел `.bai`, fallback — heightmap) | Работает, но **плоская**: нет полов, мостов, интерьеров; исключения глушатся в 0 |
| Корабли | `AAEmu.Game/Physics/*` (Jitter2: `ShipController`, `Buoyancy`, `ShipShoreInteraction`, `ShipShipInteraction` SAT, `ShipCliffInteraction`, `ShipStaticBarrierInteraction`) | Зрелая, но **без brushes/voxels**: рельеф = broad-phase MPR/EPA по heightmap, остальное — ручные плоские запросы |
| Чтение `.bai` (навмеш) | `Models/CryEngine/Readers/*`, `BaseBaiLoader` | Готово |
| Чтение из AAPak | `AAEmu.Commons/Utils/AAPak/AAPak.cs`, `AAEmu.Game/IO/ClientFileManager.cs` | Готово, чтение на месте, без распаковки |

### 3.2 Чего нет (и что предстоит добавить)

| Чего нет | Источник | Объём |
|---|---|---|
| Чтение `.cgf`/`.cga` (рендер-геометрия) | melc: `Physics/GodotGeo/Source/CgfModelReader.cs` (39 КБ) — **самодостаточен**, ноль зависимостей от AAEmu | копируется дословно |
| Чтение **CryPhysics-прокси** `MeshPhysicsData 0x800` | melc: `Models/CryEngine/CgfConverter/**` (`MeshPhysicsData800Reader.cs`, `ChunkMeshPhysicsData*.cs`, `PhysicsData`); альтернатива — ArcheageZones: `CgfPhysicsReader.cs` (379 LOC, box/trimesh/sphere/cylinder/capsule, LOD-пропуск) | 0.4–1 КБ LOC |
| Сборка сцены ячейки (placement + модели) | melc: `Source/CellObjectReader.cs` (57 КБ), `Source/CellEntityReader.cs` (14 КБ), `GodotGeoCellSceneLoader.cs`, `GodotGeoModelLibrary.cs` | ~75 КБ |
| Индекс статической геометрии + рейкасты | melc: `Physics/WorldStaticGeometryIndex.cs` (89 КБ), `StaticGeometryVerticalIndex.cs` (14 КБ), `GodotGeoSurfaceResolver.cs` | ~103 КБ |
| Высота НПЦ по слоям | melc: `NpcHeightResolver.cs` (921 стр.), `NpcHeightState.cs`, `NpcWaterSurfaceOverrides.cs`, `NpcSpawnHeightOverrides.cs` | ~1 КБ LOC |
| Мотор НПЦ (опора, гравитация 18 м/с², луч вниз) | melc: `GodotGeoNpcMotor.cs` (23 КБ), `GodotGeoNavigator.cs` (28 КБ) | ~51 КБ |
| Статические тела кистей/вокселей в Jitter2 | melc: `PhysicsManager.cs` (2112 стр. против наших 922) | адаптация |
| Правила стансов воды (swim/stand/settle) | ArcheageZones: `ZoneWater.cs` (196 LOC) + `StaticCollisionOptions` (43 LOC) | ~240 LOC |
| Стены и обход препятствий | melc: `Docs/reports/2026-10-05-GodotGeo-Walls-Pathfinding-*` | отдельная работа (P6) |

### 3.3 Дефекты, которые надо закрыть в любом случае

1. `Physics/HeightMaps/Heightmap.cs:13` — `GetHeight` делит индекс ещё раз на 2, тогда как
   `PhysicsManager.UpdateHeightMapFromCellBody` (`PhysicsManager.cs:911-918`) пишет по
   неделённому индексу ⇒ треугольники рельефа строятся на ¼ разрешения сетки.
2. `Physics/HeightMaps/HeightmapTester.cs:93-173` — `RayCast` (DDA по heightmap) **написан, но ни
   разу не вызывается**, проверка границ на `:127` сломана (`WorldBoundingBox.Min.Z < 0` — константа).
3. `Physics/HeightMaps/HeightmapDetection.cs:24-84` — на каждый broad-phase проход для каждого тела
   `NarrowPhase.MprEpa` по **каждому кваду** AABB; ускорения на стороне рельефа нет; возврат
   `false` для всех пар (`:83`), контакты регистрируются вручную.
4. `Core/Managers/World/WorldManager.cs:757-784, 791-826` — высота `0f` трактуется как «не найдено»,
   хотя ноль — легитимная высота рельефа.
5. `Core/Managers/AiGeodataManager.cs:259-333` — линейный скан всех узлов навмеша ячейки на каждый
   вызов + проглатывание исключений.
6. `Core/Managers/World/ShipStaticBarrierBaiIngestor.cs:180` — все барьеры получают `ZoneKey = 0`,
   поэтому фильтр по зоне в `ShipStaticBarrierInteraction.cs:125` фактически мёртв.
7. Две независимые системы столкновений (Jitter-контакты рельефа и ручная SAT/полигонная логика
   кораблей) — их придётся держать согласованными.

**Статус на 2026-11-15 (закрыты 1–6, сверено с кодом и клиентом `Live-3500_30.10.2026`):**

| # | Что сделано |
|---|---|
| 1 | `Heightmap.GetHeight` больше не делит индекс на 2 (массив и так в `CELL_SIZE / 2` метрах). Снятое деление открыло выход за границу в `HeightmapDetection`: `maxX` был `Width - 1` при чтении `x + 1` — теперь `Width - 2`. |
| 2 | Проверка границ в `HeightmapTester.RayCast` исправлена (`x < 0 \|\| z < 0 \|\| x + 1 >= Width \|\| z + 1 >= Height`). Утверждение «ни разу не вызывается» **неверно**: класс реализует `Jitter2.Collision.IRayCastable`, и `RayCast` зовётся движком через интерфейс, а не из нашего кода. |
| 3 | `HeightmapDetection.Filter` отсеивает квады по полосе высот до `MprEpa`: если `[quadMin, quadMax]` не пересекает `[min.Y, max.Y]` тела (±0.5 м допуска), MPR не зовётся. Высоты квада читаются один раз вместо четырёх. Ручная регистрация контактов и `return false` сохранены. |
| 4 | Добавлены `WorldTemplate.TryGetRawHeightMapHeight` / `TryGetHeight` и `WorldManager.TryGetHeight`; `GetHeight(zoneKey, …)` и `GetReferenceHeight` больше не читают `0` как «не найдено». Попутно исправлен выход за границы (`cellX > CellX` → `>=`) и сохранён ленивый `VerifyCellLoaded()`. |
| 5 | `AiGeodataManager.GetHeight` сравнивает **квадрат** расстояния (без `sqrt` на горячем пути, порог `0.0001` = прежние `0.01` м) и логирует исключение вместо молчаливого `0`. Семантика возврата не менялась: ~31 вызывающий уже трактует `0` как «нет данных», и менять её отдельным рефакторингом дороже, чем польза. |
| 6 | `ShipStaticBarrierBaiIngestor` вычисляет `ZoneKey` барьера по центроиду его контура (`WorldManager.GetZoneId`); вне мира остаётся `0` (барьер активен везде). Фильтр в `ShipStaticBarrierInteraction` перестал быть мёртвым. |

Пункт 7 остаётся открытым (архитектурный).

### 3.5 P1 выполнен (2026-11-15): прокси-геометрия и A/B

Отчёт: [`2026-11-15-p1-proxy-vs-render.md`](./2026-11-15-p1-proxy-vs-render.md).

* В пакет добавлен `Source/CgfRenderReader.cs` (рендер-меш `.cgf`) и режим `StaticCollisionOptions.PhysicsSource`
  (`Proxy` | `Render` | `ProxyThenRender`, по умолчанию последний; `AAEMU_GEOMETRY_PHYSICS_SOURCE`).
* A/B на 30 ассетах ячейки `010_015`: 4 ассета (13 %) вообще не имеют прокси; у остальных прокси — это
  авторский trimesh (параллелепипедов «по умолчанию» нет); ровно у 6 — чистые боксы.
* Цена режимов на зоне 129: `Proxy` теряет **44 ассета и 952 размещения**, `ProxyThenRender` возвращает их за
  +0.79 MB мешей, `Render` — за +24 MB и ~3.5× по времени сборки.
* 11 новых тестов пакета (`AAEmu.UnitTests/Geometry`) зелёные.

### 3.6 P2–P7 (2026-11-15): итог

Отчёт: [`2026-11-15-p3-p7-implementation.md`](./2026-11-15-p3-p7-implementation.md).

| Фаза | Состояние | Ключевое |
|---|---|---|
| P2 индеке+рейкасты | **выполнено** | медиана шага на лестнице 0.144 м (критерий 0.25), p99 0.026 мс (критерий 0.2) |
| P3 вода | **выполнено** | `StanceRules` с порогами из `ZoneWater`; `ResolveStance`, `SurfaceSwimZ` |
| P4 высота НПЦ | **выполнено** | `NpcGroundResolver` был мёртвым кодом — зарегистрирован в DI и врезан в `GetReferenceHeight` |
| P5 физика | **выполнено** | fixed-step 25 Гц с потолком 0.25 с; `AddStaticGeometry` — по боксу на кисть |
| P6 стены | **частично** | `IsBlocked` и `Raycast` готовы и покрыты тестами; в живые поведения ИИ не врезано |
| P7 тесты | **выполнено** | 26 новых тестов геометрии; решение собирается без ошибок |

Её критичное изменение для живого сервера — **fixed-step физики**: раньше шаг зависел от джиттера
`Thread.Sleep`, теперь он ровно `1 / TargetPhysicsTps`.

**Инструмент проверки:** `tools/GeometryProbe` — офлайн-прогон геометрического слоя по клиенту
(`--game <распакованный game> | --pak <game_pak>`, режимы `--zone`, `--cell`, `--cgf`, `--list`).

### 3.4 P0 выполнен (2026-11-15): пакет `AAEmu.Geometry`

Геометрический слой вынесен из `AAEmu.Game/Physics/Static` в отдельный проект `AAEmu.Geometry`
(net10.0), подключённый в `AAEmu.slnx` и в `AAEmu.Game`. Ограничения §4.1 соблюдены и проверены поиском:
в пакете **нет** ни одной ссылки на `AAEmu.Game`, `Jitter2` или `NLog` (упоминания NLog остались только
в комментариях).

| Что | Куда |
|---|---|
| `IGeometryFileSource` (интерфейс доступа к клиентским файлам) | `Abstractions/` |
| `StaticCollisionCell`, `StaticGeometry` | `Abstractions/` |
| `CgfPhysicsReader`, `BrushPlacementFile` | `Source/` |
| `WaterVolumeRecord` (тип воды, свой читатель вместо `ObjectDataType11Water` из Game) | `Source/` |
| `CollisionMesh`, `StaticCollision`, `CollisionAssetCache`, `StaticCollisionOptions` | `Index/` |
| `ZoneWater` | `Water/` |
| `GeometryLog` (сменный приёмник логов; хост связывает с NLog через `GeometryLogBridge.Attach`) | корень пакета |

Адаптеры остались в дереве, как и требовал §8: `ClientFileCollisionAssetSource` / `DirectoryCollisionAssetSource`
поверх `ClientFileManager`. `StaticGeometryManager` и `NpcGroundResolver` работают через пакет.
**Критерий приёмки P0 пройден:** `tools/GeometryProbe` на клиенте `Live-3500_30.10.2026` даёт те же числа,
что и до переноса (зона 129 — 5 670 брашей / 549 ассетов / 2 158 704 треугольника / 17 объёмов воды; ячейка
`010_015` — 1 574 / 196 / 3 и 117 брашей в `visareas.dat`).

---

## 4. Целевая архитектура

```
┌──────────────────────────── общий пакет AAEmu.Geometry ────────────────────────────┐
│  Source/ (самодостаточно, копируется дословно)                                      │
│    CgfModelReader        — .cgf/.cga: чанки, вершины, индексы, узлы, LOD            │
│    CgfPhysicsProxyReader — .cgf: MeshPhysicsData 0x800 (box/trimesh/примитивы)      │
│    CellObjectReader      — object.dat → placements (brush/voxel) + asset-path         │
│    CellEntityReader      — entities/vis-areas                                       │
│    TerrainCellReader     — heightmap.dat → сетка                                     │
│  Index/                                                                             │
│    StaticGeometryIndex   — BVH по placement-ам: Raycast / Floor / AnyBoxOverlaps      │
│    VerticalIndex         — быстрый вертикальный поиск по слою                        │
│    CollisionModelCache   — LRU кэш моделей/прокси (общий на процесс)                  │
│  Water/                                                                             │
│    WaterVolumeIndex      — объёмы type 11, уровень моря, IsWater/Surface              │
│    StanceRules           — Swim/Stand/Settle/UnderwaterFish (из ZoneWater)           │
│  Abstractions: IGeometryFileSource, GeometryWorld, GeometryCell — без Jitter2/AAEmu  │
└──────────────────────────────────────────────────────────────────────────────────────┘
        ▲ (ProjectReference)                                        ▲ (ProjectReference)
┌───────┴──────────────────┐                            ┌──────────┴─────────────────────┐
│ AAC3500                 │                            │ melc game-server-3.5.0.3       │
│  • адаптер ClientFile    │                            │  • адаптер ClientFile          │
│    Manager → IGeometry   │                            │    Manager → IGeometry         │
│    FileSource            │                            │  • PhysicsManager (2112 стр.)  │
│  • PhysicsManager (922)  │                            │  • NpcHeightResolver + мотор   │
│  • NpcHeightResolver     │                            │  • протокол 3.5 (старее)        │
│  • протокол 3.5 (свежий) │                            │                                │
└──────────────────────────┘                            └────────────────────────────────┘
```

### 4.1 Пакет `AAEmu.Geometry`: контракт и границы

**Обязательные ограничения (иначе пакет не будет общим):**

* **Никаких зависимостей на Jitter2 и на `AAEmu.Game`.** Пакет работает в координатах игры
  (Z-up, CryEngine-native, метры) и отдаёт геометрию как данные (`float[]` вершин, `int[]`
  индексов, AABB). Адаптация к Jitter2 (`Jitter(X,Y,Z) = game(X,Z,Y)`, см.
  `PhysicsManager.cs:483`) делается в каждом дереве — это 30–50 строк на стороне.
* **Доступ к клиентским данным — через интерфейс**, а не через `ClientFileManager`:
  ```csharp
  public interface IGeometryFileSource
  {
      bool FileExists(string path);          // "game/worlds/main_world/cells/027_007/client/object.dat"
      Stream OpenRead(string path);
      IEnumerable<string> ListFiles(string directory, string pattern);
  }
  ```
  В каждом дереве — тонкий адаптер (AAC3500: 20 строк поверх `ClientFileManager`).
* **Никаких `WorldCell`/`WorldTemplate`/`NpcAi` в API.** Пакет оперирует своими типами:
  `GeometryWorld` (мир + зоны), `GeometryCell` (ячейка + placement-ы), `GeometryPlacement`.
  Маппинг «зона → ячейки» делает вызывающая сторона (в AAC3500 это уже есть:
  `WorldTemplate.XmlWorldZones` / `ZoneKeyByRegions`).

**Публичный API пакета (проектный):**

```csharp
namespace AAEmu.Geometry;

public sealed class GeometryWorld
{
    Task<GeometryCell> GetCellAsync(int cellX, int cellY, CancellationToken ct);
    Task<StaticGeometryIndex> GetIndexAsync(int cellX, int cellY, CancellationToken ct);
    StaticCollisionPolicy Policy { get; set; }   // биты: подземные полы, морская/пресная вода, стансы
}

public sealed class GeometryCell
{
    IReadOnlyList<GeometryPlacement> Brushes { get; }   // asset-path + матрица размещения
    IReadOnlyList<GeometryPlacement> Voxels { get; }
    IReadOnlyList<WaterVolume> Water { get; }
    TerrainGrid Terrain { get; }                        // сетка высот + коэффициент квантования
}

public sealed class StaticGeometryIndex                // read-only, потокобезопасен
{
    bool Raycast(Vector3 from, Vector3 to, out float fraction, out Vector3 normal, float minUpNormal = 0f);
    bool TryFloor(float x, float y, float above, float below, out float z);
    bool AnyBoxOverlaps(Vector3 min, Vector3 max);
}

public sealed class WaterVolumeIndex
{
    bool IsWater(Vector3 p, out Vector3 flow);
    float GetSurface(Vector3 p, out Vector3 flow);
    Stance ResolveStance(Vector3 p, float depth, float headAboveWater);
}
```

**Политика (из `StaticCollisionOptions` ArcheageZones):** битовые флаги
`UndergroundFloors`, `UndergroundStructureFloors`, `InlandWater`, `SeaWater`,
`InlandFishSwim`, `SwimStance`, `SettleNoRise`, `SurfaceDepthByModel` — переносимые как есть,
управляются конфигом `Configurations/Geometry.json` в каждом дереве.

---

## 5. Пофазный план (S3: общий пакет `AAEmu.Geometry`, цель — максимальная фиделита)

Оценки — человеко-дни на одного разработчика.

### P0. Каркас пакета + дословный перенос self-contained читателей (3–4 дня)

* Создать `AAEmu.Geometry` (net10.0, без Jitter2/AAEmu-зависимостей), включить в `AAEmu.slnx`
  обоих деревьев.
* Перенести **дословно** `Source/{CgfModelReader,CellObjectReader,CellEntityReader,TerrainCellReader}.cs`
  из melc-дерева (≈115 КБ; зависимости только `System.*`) → `AAEmu.Geometry/Source/`.
* Описать `IGeometryFileSource` + адаптер в AAC3500 и в melc-дереве.
* **Критерий приёмки:** для ячейки `027_007` из `E:\games\MAA-3500\game_pak` — те же числа, что
  в melc-документации («1189 моделей, 1 012 499 треугольников, 1186 brushes, 3 voxels»), найдена
  лестница `ha_palace_stairs.cgf` (`ObjectDat:1166071` / `1166830`).
* **Риск:** формат `.cgf` в 3.5 может отличаться от ожидаемого melc-кодом → ориентир:
  `Docs/reports/cgf_744_745_objectdat_static_geodata_2026-06-03.md` в melc-дереве.

### P1. Прокси-геометрия (CryPhysics `MeshPhysicsData 0x800`) (4–6 дней) — **цель фиделити**

* Перенести в пакет `CgfPhysicsProxyReader`:
  * melc-вариант: `Models/CryEngine/CgfConverter/**` (`MeshPhysicsData800Reader.cs`,
    `ChunkMeshPhysicsData_800.cs`, `PhysicsData`) — работает с их вендоренным CgfConverter;
  * ArcheageZones-вариант: `CgfPhysicsReader.cs` (379 LOC) — самодостаточен, читает
    box/trimesh/sphere/cylinder/capsule, умеет LOD-пропуск и ремап «физ-меш → рендер-меш»;
  * **решение:** взять самодостаточный `CgfPhysicsReader` как основу (меньше связности), а
    формат `MeshPhysicsData 0x800` из melc — как источник знаний о раскладке полей.
* Связать с `CollisionModelCache`: модель = «рендер-геометрия» (для визуала/проверки) + «прокси»
  (для коллизии); приоритет — прокси, fallback на рендер, если прокси нет.
* **Критерий приёмки:** для 20–30 ассетов (стены, мебель, причалы, скалы) коллизионная поверхность
  совпадает с авторским прокси, а не с параллелепипедом; A/B-таблица «прокси vs рендер» в отчёте.
* **Риск:** прокси местами грубее рендера (плоские «параллелепипеды») → возможна визуальная
  регрессия; решение — переключатель `Geometry.PhysicsSource = Proxy | Render` в конфиге.

### P2. Индекс статической геометрии и рейкасты (5–7 дней)

* Перенести в пакет `StaticGeometryIndex` (адаптация melc-овских 89 КБ + 14 КБ) и
  `GodotGeoSurfaceResolver` — с заменой типов melc-дерева на типы пакета.
* Добавить в пакет `GodotGeoModelLibrary`-эквивалент с **LRU-выгрузкой** (melc фиксировал рост
  working set до 5.5 ГБ).
* **Критерий приёмки:** луч вниз из точки над лестницей возвращает Z ступени (melc: 6 точек,
  отличие ≤ 0.204 м); на открытой земле — совпадение с `WorldTemplate.GetHeight` в пределах 0.05 м;
  p99 рейкаста < 0.2 мс.

### P3. Вода: объёмы, поверхность и правила стансов (3–5 дней) — **цель фиделити**

* Перенести `WaterBodies` → `WaterVolumeIndex` пакета (объёмы type 11 из `object.dat`,
  уровень моря, течение) и добавить **правила стансов** из `ZoneWater.cs`:
  `SwimStance`, `SettleNoRise`, `InlandFishSwim`, `SurfaceDepthByModel` — с теми же битами политики.
* **Критерий приёмки:** НПЦ в реке/озере переходит в станс Swim при глубине выше порога,
  на мелководье — Stand, на дне — Settle; поведение совпадает с ArcheageZones-правилами
  (они сверены с нативным dedicate 10.x).

### P4. Высота НПЦ по слоям + мотор (8–12 дней)

* Перенести в пакет «чистую» часть `NpcHeightResolver` (стратегия слоёв, якоря, water/air) —
  без зависимости от `NpcAi`/`Behavior`; слой glue остаётся в каждом дереве.
* В дереве AAC3500: `NpcHeightResolver` + `NpcHeightState` + `NpcWaterSurfaceOverrides` +
  `NpcSpawnHeightOverrides` (из melc) и врезка в точки запроса высоты:
  `Models/Game/AI/v2/Behaviors/BaseCombatBehavior.cs:359`, `Common/RoamingBehavior`,
  `Common/FollowPathBehavior`, `AIUtils.CalcNextRoamingPosition`,
  `Core/Managers/World/WorldManager.cs:828-864`, эффекты скиллов (`ImpulseEffect`, `KnockBack`,
  `Blink`, `TeleportToUnit`, `PhysicalExplosionEffect`), `Route/Track.cs:111`, `Route/Line.cs:124`.
* Мотор НПЦ (`GodotGeoNpcMotor`, 23 КБ): опора на каждом XY-подшаге ≤0.25 м, гравитация 18 м/с²,
  двусторонний луч из ног +0.7 м вниз до 40 м, выбор верхнего допустимого попадания.
* **Критерий приёмки:** «NPC идёт по видимой поверхности» (лестница, 6 точек спуска) + падение
  неподвижного НПЦ с 10 м; НПЦ под потолком не выталкиваются на верхний этаж.
* **Оговорка по частотам:** ИИ тикает 10 Гц (`AIManager.cs:27`), физика — 25 Гц
  (`TargetPhysicsTps`), поэтому опора НПЦ не может опираться на физический тик: только
  геометрические запросы (индекс статики), без Jitter2.

### P5. Корабли: brushes/voxels как коллизия (5–8 дней)

* Подключить пакет к `PhysicsManager` в обоих деревьях:
  * статические тела кистей/вокселей (melc: `PhysicsManager.cs:1868`, `:2061`),
  * fixed-step вместо переменного (`PhysicsManager.cs:215`),
  * трекинг terrain-body (`RegisterTerrainBody` / `UnregisterTerrainBody`).
* Сохранить нашу динамику: `Buoyancy`, импульсные взаимодействия, `ShipShipInteraction` (SAT) —
  **не** переносить `HullBody`/`HullContacts` из ArcheageZones (3-DOF, roll/pitch «level», калибровка
  «guess until another model grounds in a trace» — хуже нашей плавной динамики и ломает репликацию,
  завязанную на `ReplicationSmoothing`).
* **Критерий приёмки:** корабль не проходит через стену/причал/скалу; регрессии
  `BoatPhysicsManagerTests` (melc) + наши зелёные; `PhysicsManagerFixedStepTests` проходит.

### P6. Стены и обход препятствий (5–10 дней)

Спека melc: `Docs/reports/2026-10-05-GodotGeo-Walls-Pathfinding-{Design,Plan,Result}.md`.
Сейчас горизонтальные проверки в melc отключены — НПЦ проходят сквозь стены (признано допустимым).
Это отдельная фича, не блокер P0–P5.

### P7. Тесты и регрессии (4–6 дней)

* Перенести в `AAEmu.UnitTests` релевантные файлы melc:
  `Physics/WorldStaticGeometryIndexLifecycleTests`, `Physics/StaticGeometryVerticalIndexTests`,
  `Physics/GodotGeo/*` (11 файлов), `Models/Game/NPChar/GodotGeoNpcHeightTests`,
  `NpcHeightResolverVisibilityReconcileTests`, `NpcSpawnHeightOverridesLayeredHeightTests`,
  `Models/CryEngine/MeshPhysicsData800ReaderTests`, `Core/Managers/World/PhysicsManager*Tests` (5),
  `Utils/GodotGeoTestWorld`, `Utils/StaticGeometryTestUtils`.
* Тесты пакета — в `AAEmu.Geometry.Tests` (TUnit), чтобы их можно было гонять в обоих деревьях.

---

## 6. Чего делать **не** надо

| Вариант | Почему отклоняем |
|---|---|
| **A/B1. Перейти на стек ArcheageServer/ArcheageZones/zone-3.5** | Смена сервера и версии игры на 10.x-клиент. Теряем 3.5.0.3, свою БД, GM-скрипты и протокольный слой AAC3500 |
| **B2. Портировать dual-wire + V10-кодеки + ZoneHost** | Нужен нативный CN 10.0 dedicate (бинарей локально нет), второй клиент 10.0, контент эры 10.0, двойная истина по состоянию НПС. Оценка: месяцы |
| **Портировать `ZoneHeightmap` из ArcheageZones** | Дублирует наш `WorldCell.LoadCellHeightMapFromClientData` + `WorldTemplate.GetHeight`. Из него ценны только два фикса: квантование высокого рельефа >1024 м и low-res секторы — переносить точечно, если нужно |
| **Портировать `ZoneWater` как есть** | Объёмы воды у нас уже есть (`WaterBodies`). Из `ZoneWater` берём **только правила стансов** (P3) |
| **Заменять Jitter2-корабли на `HullSystem` (3-DOF)** | Хуже нашей плавной динамики, ломает репликацию; отчёт сам это отмечает |
| **Тянуть Jitter2 в пакет `AAEmu.Geometry`** | Связывает пакет с физическим движком и блокирует перенос в melc-дерево; пакет должен отдавать данные, а не тела |

---

## 7. Метрики и проверки

**Функциональные**
* Точность опоры: |Z поверхности − Z рельефа| ≤ 0.25 м в контрольных точках лестницы (melc: ≤0.204 м).
* На открытой земле: |Z нового пути − `WorldTemplate.GetHeight`| ≤ 0.05 м.
* Ни одного «провала» NPC: доля НПЦ, ушедших в Z < −50 от спавна = 0.
* Стансы: Swim/Stand/Settle переключаются на порогах, совпадающих с `ZoneWater` (сверено с нативом).

**Производительность (критично: melc фиксировал рост working set до 5.5 ГБ)**
* Построение сцены ячейки < 300 мс; кэш моделей — общий на процесс, с LRU-выгрузкой.
* RAM индекса геометрии на ячейку: цель < 40 МБ; ввести политику выгрузки по невидимым зонам.
* Горячий путь высоты НПЦ (terrain-only): не более +10 % к `AiGeoDataManager.GetHeight`
  (сейчас — линейный скан узлов `.bai`, O(n) на вызов).
* Тик физики: 25 TPS fixed-step, без роста p95 времени кадра.

**Регрессии**
* Зелёные: `AAEmu.UnitTests` обоих деревьев + `AAEmu.Geometry.Tests` + перенесённые тесты melc.
* Смоук: клиент 3.5 входит в мир, бежит по земле, заходит в интерьер, плавает, садится на корабль.

---

## 8. Разделение ответственности между деревьями (S3)

| Слой | Где живёт | Почему |
|---|---|---|
| Чтение клиентских файлов, `.cgf`/`.cga`, `object.dat`, `heightmap.dat` | **Пакет** `AAEmu.Geometry` | Одинаково для обеих линий; это и есть «физика зон» по сути |
| Прокси-геометрия, BVH-индекс, вертикальный индекс, кэш моделей | **Пакет** | Чистая геометрия, без игровых типов |
| Вода: объёмы, поверхность, **правила стансов** | **Пакет** | Правила — общие; вызовы — в каждом дереве |
| Адаптер `ClientFileManager → IGeometryFileSource` | **В каждом дереве** | У деревьев разные источники данных |
| `PhysicsManager` (Jitter2, корабли, статические тела) | **В каждом дереве** | Привязан к своему `WorldInstance`, репликации и тикам |
| `NpcHeightResolver` (чистая часть: слои, якоря, вода/воздух) | **Пакет** (логика) + **дерево** (glue к `NpcAi`) | Стратегия высоты общая, привязка к ИИ — разная |
| Мотор НПЦ (`GodotGeoNpcMotor`) | **Пакет** (интегратор) + **дерево** (связь с поведениями) | Физика движения общая |
| Протокол 3.5, пакеты, репликация, GM-команды, БД | **В каждом дереве** | Это и есть то, что разводит линии |
| Тесты геометрии | **`AAEmu.Geometry.Tests`** | Гоняются в обоих деревьях |

**Риск S3 (главный):** вилка двух кодовых баз. Снимается тем, что пакет — единственное место,
где живёт геометрия, а деревья остаются «протокольными/игровыми» оболочками; изменения геометрии
идут только через пакет и его тесты.

**Порядок слияния:** сначала пакет собирается в AAC3500 (где сейчас ничего нет), потом
подключается в melc-дереве (где уже есть своя копия — её заменяем вызовами пакета, поведение
не должно измениться; регрессионная база melc — 4176 тестов).

---

## 9. Открытые вопросы (остались после решений)

1. **Имя/расположение пакета и способ связи деревьев:** физическая папка в AAC3500 + `ProjectReference`
   из melc (нужен доступ к melc-репозиторию) или отдельный git-репозиторий `AAEmu.Geometry` с
   submodule/NuGet? Второй вариант чище для синхронизации, но требует CI-поддержки.
2. **Политика памяти:** LRU-выгрузка ячеек по «невидимости» — какой триггер (переход зоны, таймер,
   лимит)? Целевой лимит RAM на процесс?
3. **Переключатель источника коллизии** (`Proxy | Render`) — оставляем в проде или только в
   диагностике?
4. **Формат данных:** читать `.cgf` из `game_pak` на лету (35 ГБ, холодный старт) или
   предсобрать кэш ячеек (как `compact.sqlite3` для геймдата)? Для «максимальной фиделити» нужен
   именно оригинальный `.cgf` — значит, кэш должен хранить прокси-данные в исходном виде.
5. **Порядок переноса тестов melc:** они завязаны на его `WorldCell`/`Npc` — потребуется
   «тест-фреймворк» в пакете (собственный минимальный `GodotGeoTestWorld`).

---

## 10. Приложение: карта источников

**Наш проект (AAC3500)**
```
AAEmu.Game/Models/Game/World/WorldCell.cs            :135  LoadCellHeightMapFromClientData
AAEmu.Game/Models/Game/World/WorldCell.cs            :198  загрузка client/object.dat
AAEmu.Game/Models/Game/World/WorldTemplate.cs        :118/:174  GetRawHeightMapHeight / GetHeight
AAEmu.Game/Models/CryEngine/Objects/ObjectsFile.cs   :17   чтение object.dat
AAEmu.Game/Models/CryEngine/Objects/ObjectDataType1Brush.cs  трансформация @0x47, asset-index @0x7F
AAEmu.Game/Models/Game/World/WaterBodies.cs          :294/:401  IsWater / AddFromCellData
AAEmu.Game/Core/Managers/AiGeodataManager.cs          :259  GetHeight (navmesh → heightmap)
AAEmu.Game/Core/Managers/World/WorldManager.cs        :757/:791/:828  GetHeight / GetHeight(T) / GetReferenceHeight
AAEmu.Game/Core/Managers/World/PhysicsManager.cs      :101/:138/:215/:903  terrain, broadphase, tick, hmap update
AAEmu.Game/Core/Managers/World/ShipStaticBarrierBaiIngestor.cs :180  барьеры без ZoneKey
AAEmu.Game/Physics/HeightMaps/HeightmapTester.cs      :93   неиспользуемый DDA-рейкаст
AAEmu.Game/Models/Game/AI/v2/Behaviors/BaseCombatBehavior.cs :359  точка запроса высоты НПЦ
AAEmu.Game/Models/Game/NPChar/Npc.cs                 :1173 MoveTowards (ground snap через GetReferenceHeight)
AAEmu.Game/Core/Managers/AIManager.cs                 :27   тик ИИ 10 Гц
AAEmu.Game/IO/ClientFileManager.cs                          чтение из AAPak (game_pak)
AAEmu.Commons/Utils/AAPak/AAPak.cs                          AAPak-ридер (чтение на месте)
```

**Источник порта (melc game-server-3.5.0.3, ветка 3.5)**
```
AAEmu.Game/Physics/GodotGeo/Source/{CgfModelReader,CellObjectReader,CellEntityReader,TerrainCellReader}.cs
AAEmu.Game/Physics/GodotGeo/{GodotGeoModelLibrary,GodotGeoCellSceneLoader,GodotGeoCellObjectReader,
                             GodotGeoSurfaceResolver,GodotGeoTerrainHeight,GodotGeoNpcMotor,GodotGeoNavigator}.cs
AAEmu.Game/Physics/{WorldStaticGeometryIndex,StaticGeometryVerticalIndex}.cs
AAEmu.Game/Physics/HeightMaps/ImprovedHeightmapDetection.cs
AAEmu.Game/Models/Game/NPChar/{NpcHeightResolver,NpcHeightState,NpcWaterSurfaceOverrides,NpcSpawnHeightOverrides}.cs
AAEmu.Game/Models/CryEngine/CgfConverter/**  (MeshPhysicsData800Reader, ChunkMeshPhysicsData*)
AAEmu.Game/Core/Managers/World/{PhysicsManager,ClientPrefabRuntimeGeometryResolver}.cs
Docs/reports/2026-10-05-GodotGeo-Replacement-Result.md       — подтверждённый результат
Docs/superpowers/{plans,specs}/2026-10-03-godotgeo*.md
Docs/superpowers/specs/2026-09-22-x2living-cgf-physics-proxy-design.md
Docs/world-npc/{heightmap_vs_bai_discrepancy_analysis,npc_height_model_hotpath_policy}_2026-05-21.md
```

**Внешний (ArcheageZones, 10.x) — точечно, не целиком**
```
src/AAEmu.Zone/Core/Sim/Physics/CgfPhysicsReader.cs        — примитивы прокси (box/trimesh/примитивы), 379 LOC
src/AAEmu.Zone/Core/Sim/Physics/ZoneWater.cs               — правила стансов (P3), 196 LOC
src/AAEmu.Zone/Core/Sim/Physics/StaticCollisionOptions.cs  — биты политики (P3), 43 LOC
src/AAEmu.Zone/Core/Data/ZoneHeightmap.cs                  — фиксы квантования >1024 м и low-res секторов (опц.)
```
