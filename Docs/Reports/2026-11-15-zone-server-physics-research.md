# Исследование: «сервер зон» и физика — может ли он заменить нашу (Jitter2)?

**Дата:** 2026-11-15
**Автор:** агентская сессия (GLM, OpenCode)
**Статус:** исследовательский отчёт; код в репозитории не менялся.
**Проект:** `AAEmu.Game` 3.5.0.3 — репозиторий `NL0bP/AAC3500`, рабочая копия
`D:\DEVELOPMENT\archeage\SRC\_AAClassic\AAC3500`.

> **Редакция 2 (2026-11-15).** Первая редакция отчёта описывала соседнюю 3.5-линию
> (`_melcosoft\archeage\game-server-3.5.0.3`, ветка `3.5`) как «наш проект»: в ней упомянуты
> `AAEmu.Game/Physics/GodotGeo/*`, `Source/CgfModelReader.cs` и
> `AAEmu.Game/Services/NpcSpawnAuthoringImportService.cs` — **в AAC3500 этих файлов нет**
> (проверено по дереву и по всей git-истории: `git log --all -- "*GodotGeo*"` — 0 совпадений).
> Редакция 2 переписана по фактическому состоянию AAC3500; ссылки на соседнюю линию вынесены в
> §7.3 и явно помечены как «другое дерево». План внедрения —
> [`2026-11-15-zone-physics-implementation-plan.md`](./2026-11-15-zone-physics-implementation-plan.md).

**Исходный вопрос:** проект `ArcheageZones` (и родственные стеки) — может ли он заменить физику
(корабли, НПС) в нашем `AAEmu.Game` 3.5.0.3? Если да — быстрее ли «приделать сервер зон»
к нашему серверу, чем переносить физику модулями?

---

## 1. Что проверялось (все копии локальные, под `_AA-retail\`)

| Проект | Путь | Что это |
|---|---|---|
| ArcheageZones | `D:\DEVELOPMENT\archeage\SRC\_AA-retail\ArcheageZones` | Headless зонехост для AAEmu 10.x («AAEmu Headless Zone Host»); публичный репозиторий `NL0bP/ArcheageZones` не существует (GitHub 404), локальная копия — git-репозиторий с `origin=https://github.com/NL0bP/ArcheageZones` |
| ArcheageServer | `D:\DEVELOPMENT\archeage\SRC\_AA-retail\ArcheageServer` | World-сервер 10.x-форка AAEmu (`AAEmu.WorldServer` + `AAEmu.Game` как библиотека), в паре с ArcheageZones |
| zone-3.5 | `D:\DEVELOPMENT\archeage\SRC\_AA-retail\zone-3.5` | Ветка `feature/zone-authority-3.5aac`: **dual-wire** — клиент AAClassic 3.5 + нативный CN 10.0.2.13 dedicate как «сервер зон» |
| **Наш проект** | `D:\DEVELOPMENT\archeage\SRC\_AAClassic\AAC3500` | `AAEmu.Game` 3.5.0.3: сервер 3.5, свой протокол пакетов, Jitter2-корабли, ИИ v2 по навмешу `.bai` |
| Наше дерево `npc-spawners` | `D:\DEVELOPMENT\archeage\SRC\_melcosoft\archeage\npc-spawners` | Авторское дерево спавнеров (5196 файлов) — источник данных, а не код сервера |
| **Соседняя 3.5-линия** | `D:\DEVELOPMENT\archeage\SRC\_melcosoft\archeage\game-server-3.5.0.3` (ветка `3.5`, `gitlab.melc.cc/archeage/game-server-3.0.git`) | **Другое дерево**, общей git-истории с AAC3500 нет; там уже есть подсистема статической геометрии (§7.3) |

Сопутствующий: публичный форк `NL0bP/AAEmu-10.x-Zone` (другой вариант зонехоста, «ZoneServer»,
отличается от локального headless-варианта) — использован как вспомогательный источник документации.

---

## 2. ArcheageZones (headless зонехост 10.x): что там за «физика»

Путь в репо: `src/AAEmu.Zone/Core/Sim/Physics/` (8 файлов, ~84 КБ) + `Core/Sim/Hulls/` (4 файла).

| Модуль | Содержание |
|---|---|
| `StaticCollision.cs` | BVH над расставленными брашами (инстансинг мешей по ассетам); рейкаст отрезка, `TryFloor`, `AnyBoxOverlaps`; read-only, шарится между зонами |
| `CgfPhysicsReader.cs` | Читает **настоящие CryPhysics-прокси** из `.cgf`/`.cga`: box, trimesh, sphere, cylinder, capsule; ремап на рендер-меш; LOD-пропуск |
| `ZoneWater.cs` | Пресные объёмы воды, правила станса Swim, уровень моря |
| `DoodadCollision.cs` | Коллизия дудадов |
| `Core/Data/ZoneHeightmap.cs` + `HeightmapSectors.cs` | Читатель `heightmap.dat` CryEngine, интерполяция 1:1 как у натива (low-res секторы, квантование >1024 м) |
| `Core/Sim/Hulls/*` | Корабль: `HullBody` — 3-DOF кинематика (surge/sway/yaw + пружина кватершифа), коэффициенты подогнаны по трейсам натива; `HullContacts` — неупругие импульсные контакты корпус–берег/браш/дудад/корпус с моментом инерции и выталкиванием |

**Ключевой факт:** физдвижка в проекте нет вообще. Их план прямо фиксирует: владелец отказался от
физдвижка (кандидат был BepuPhysics v2), «no physics engine package was added». Твёрдотельные
гейммики (падающие шары) помечены как «a physics engine's job, not done».
Всё сверено с трейсами нативного dedicate 10.x (в т.ч. независимое сравнение 21 рейкаста с движком).

**Важное для переноса (проверено по коду):** файлы `Core/Sim/Physics/*` и `Core/Data/ZoneHeightmap.cs`
почти не завязаны на их кодовую базу — их `using` ограничены `System.Numerics`, `NLog`,
`AAEmu.Game.Models.CryEngine.Objects` и `AAEmu.Game.Models.ClientData`. То есть
`CgfPhysicsReader.cs` (379 LOC) и `CollisionMesh.cs` (251 LOC) переносимы **дословно**, а
`StaticCollision.cs` (353 LOC) требует их тип `ZoneLevelInfo` и загрузчик файлов с диска.
`ZoneWater.cs` (196 LOC) и `StaticCollisionOptions.cs` (43 LOC) — правила стансов, переносимы
почти как есть.

---

## 3. ArcheageServer (10.x-форк): кто что симулирует

Архитектура: `клиент ⇄ AAEmu.World (:1239) ⇄ Zone (:1240, WZ/ZW)`,
`ZoneHostSupervisor` запускает `AAEmu.Zone.exe` на каждую зону (warm pool, circuit breaker).

Проверенные факты:

- В форковском `AAEmu.Game` **нет папки `Physics/`** и **нет `Models/Game/AI/`** — физдвижок и
  NPC-AI вырезаны из Game и живут в зонехосте.
- Корабли в Game — это ~14 классов правил **без единого тела**: `BoatSeamHandoffRules`,
  `BoatSeamBlendRules`, `BoatSeamPredictRules`, `BoatWaterlineDriveRules`, `HullPoseSpaceRules` и др.
  Это dead-reckoning и передача хулла через «швы» зон (глитч-гейты: ускорение >10 м/с² отбрасывается).
- От «физики» в Game остались плоские запросы высоты для порталов/GM: `WorldManager.GetHeight`,
  `GeoData.GetHeight`.
- Игроки — client-authoritative (`SimPlayer.cs`: «the host only mirrors the last reported position»),
  как и в оригинале.

Формула их стека: **CryEngine-симуляция зоны → managed-симуляция (heightmap + рейкасты +
подогнанная кинематика)**. «Полная физика» достигнута не движком, а сменой парадигмы.

Поверхность стыка World↔Zone в их репо: `Core/ZoneHost` — 36 файлов, `Core/Relay` — 36 файлов,
`Core/Packets/Wz` — 35 пакетов, `Core/Zone` — 8, зональная сторона `Core/Net` — 39 файлов. ~150
файлов клея, написанных против **их** типов Game. Протокол — внутренний контракт пары
(«Server and zone host must come from matching commits», Docs/Setup.md).

---

## 4. zone-3.5 (`feature/zone-authority-3.5aac`): «сервер зон», уже приделанный к 3.5

```
AAClassic 3.5 клиент ──CS/SC 3.5──▶ AAEmu.World (:1239, Game внутри процесса)
                                        │
                                        └──ZW/WZ 10.0──▶ Zone (:1240) = НАТИВНЫЙ dedicate
```

- Зона — **настоящий retail CryEngine**: `dedicatedserver.exe` + `x2game-dev_dedicate.dll` из
  CN-клиента 10.0.2.13, запускаемый через их Rust-бутстрап `AAEmu.ZoneHost.exe`
  (`AAEmu.ZoneHost.Native/main.rs`) или WPF-гуи `AAEmu.ZoneManager`, либо «World AutoZone».
- Dual-wire (`Docs/CLASSIC_3.5_DUAL_WIRE.md`): 3.5 Classic CS/SC на клиентском краю + 10.0 WZ/ZW
  на зонном; перекодировка на лету (`ConvertPosition` 9 байт ↔ `ConvertPositionV10` 11 байт,
  `MoveType.ReadV10/WriteV10`, FallVel `0x8000`↔`0x80`). Чеклист документа — весь `[x]`,
  включая combat/skill wire.
- Зона — симуляционный авторитет (NPC, движение, бой); World — лобби + релей
  (`Docs/ZONE_AUTHORITY_START.md:14`). Zone down ⇒ клиенты в мире отключаются.
- **«Совмещает 3.5 и 10.0» означает: 3.5 на проводе, 10.0 в контенте.** Зона читает дерево
  CN-клиента 10.0.2.13 (`game\`, `npc_spawners.g`, `game_decrypted.sqlite3` — 10.0 БД); World
  загружает `AAEmu.Game\Data\game.sqlite3` = переименованная копия 10.0-й БД. Маппинга id между
  10.0 и 3.5 в World-сервере не найдено — 3.5-клиент играет 10.0-контент через 3.5-пакеты.
  «3.5» в названии ветки = клиентский провод + плеерская БД (`aaemu_game_35aac`), не контент 3.5.
- Зонехосту нужен **второй клиент** (CN 10.0.2.13, раздают Google Drive/MEGA); dedicate —
  Windows-only; в WZ-дудадах/жилье известные швы (патч-хуки на `x2game-dev_dedicate.dll`).
- Их клиентский провод — «AAClassic 3.5.3.0» (коммит `fix(crypto): align AAC 3.5.3.0
  game-channel cry constants`); совместимость с нашим клиентом 3.5.0.3 не проверена.

**Проверка по локальному диску:** в `_AA-retail\zone-3.5\dedicate\` лежат только скрипты
(`run_zone*.bat`, `system.cfg`, `zone_map_names.txt`) — **бинарей нативного dedicate нет**,
CN-клиент 10.0.2.13 на машине отсутствует. То есть вариант B нельзя начать «с нуля» без
приобретения/скачивания второго клиента и нативных бинарей.

---

## 5. Наш проект AAC3500: что у нас с физикой и геометрией сегодня

### 5.1. Jitter2 — только корабли

* **21 файл, ~4 500 LOC** в `AAEmu.Game/Physics/`: `ShipController`, `ShipShoreInteraction`,
  `ShipCliffInteraction`, `ShipShipInteraction`, `ShipDoodadInteraction`,
  `ShipStaticBarrierInteraction` + `ShipStaticBarrierSpatialGrid`, `ShipStaticObstacleContact`,
  `ShipHarpoonTowPhysics`, `ShipSiegeAoEHit`, `ReplicationSmoothing`, `Forces/{Buoyancy,
  ForceGenerator,OneShotVelocityKick}`, `HeightMaps/{Heightmap,HeightmapTester,HeightmapDetection,
  CollisionTriangle}`, `Util/PhysicsUtil`, `Debug/*`.
* **Единственные collision shapes во всём сервере** — `BoxShape` корабля
  (`Physics/ShipController.cs:288-290`, локальный +X = ширина, +Y = высота, +Z = длина) и
  прокси рельефа `HeightmapTester`.
* **Тел НПС нет вообще**: ветка НПС в `Core/Managers/World/PhysicsManager.cs:222-231`
  закомментирована; `EnqueueAddBody`/`EnqueueRemoveBody` (`:836-858`) вызываются только из
  `AddShip`/`RemoveShip` — наследие прежней designs. Единственное свойство `RigidBody` в моделях —
  `Slave.RigidBody` (`Models/Game/Units/Slave.cs:104`).
* Один `Jitter2.World` на инстанс мира, отдельный поток `"Physics-<world>"`
  (`PhysicsManager.cs:152-157, 162-446`), target **25 TPS**
  (`Configurations/World.json → TargetPhysicsTps`), шаг **переменный**
  (`_physWorld.Step(deltaSeconds, false)`, `:215`), гравитация `(0,-9.81,0)`.
* Рельеф как коллизия — broad-phase фильтр `HeightmapDetection` (`:24-84`): для каждого квадра
  сетки в AABB тела строится два треугольника и зовётся `NarrowPhase.MprEpa`; контакты
  регистрируются вручную, пара всегда отклоняется (`return false`, `:83`).
* Ручная коллизия кораблей идёт **параллельно** Jitter-контактам рельефа: SAT корабль↔корабль
  (`ShipShipInteraction`), AABB/OBB корабль↔дудад (`ShipDoodadInteraction`), полилинии
  корабль↔BAI-барьеры (`ShipStaticBarrierInteraction` + `ShipStaticBarrierBaiIngestor`),
  плоские запросы высоты — берег (`ShipShoreInteraction`) и скалы (`ShipCliffInteraction`).
  Две системы придётся держать согласованными.

### 5.2. Высота НПЦ — плоская, без слоёв

* Запрос идёт из `Npc.MoveTowards` (`Models/Game/NPChar/Npc.cs:1226`) →
  `WorldManager.GetReferenceHeight` (`Core/Managers/World/WorldManager.cs:828-864`) →
  `WorldManager.GetHeight(zoneKey, x, y, z)` (`:757-784`) → `AiGeoDataManager.GetHeight`
  (`Core/Managers/AiGeodataManager.cs:259-333`): ближайший узел навмеша `.bai`
  (`NodeDescriptor`/`ObstacleDataDescriptor`), иначе — `worldTemplate.GetRawHeightMapHeight`.
* Отдельного «резолвера высоты» у нас **нет** (в отчёте 1-й редакции упоминался
  `NpcHeightResolver` — такого файла в AAC3500 нет; `Models/Game/NPChar/` содержит
  `Npc.cs`, `NpcSpawner*.cs`, `NpcTemplate*.cs`, `Aggro*.cs` и т.п., но не `NpcHeightResolver`,
  `NpcHeightState`, `NpcWaterSurfaceOverrides`, `NpcSpawnHeightOverrides`).
* Следствия: НПЦ не стоят на мостах/в интерьерах/на втором этаже (берётся навмеш или рельеф),
  нет потолков (НПЦ выталкиваются на верхний этаж), нет правил станса воды.
* Путь ищется A* по навмешу `.bai` (`Models/Game/AI/AStar/PathNode.cs:74-154`), запреты —
  `AiGeoDataManager.CheckImpossibleWalk` (`:33-48`). Тик ИИ — **10 Гц**
  (`Core/Managers/AIManager.cs:27`, `TimeSpan.FromMilliseconds(100)`), базовый цикл `TickManager` — 20 мс.
* Ошибки в `AiGeoDataManager.GetHeight` проглатываются с возвратом `0` (`:325-328`), а сам поиск —
  линейный скан узлов навмеша ячейки на каждый вызов.

### 5.3. Какие данные клиента мы вообще читаем

| Путь в `game_pak` | Читатель | Назначение |
|---|---|---|
| `game/worlds/<w>/world.xml` | `Models/Game/World/Xml/XmlWorld.cs` | размер мира, уровень моря, список зон, ячейки/секторы |
| `game/worlds/<w>/cells/<XXX_YYY>/client/terrain/heightmap.dat` | `Models/Game/World/WorldCell.cs:135-225` (`Hmap`/`NodeCell`) | высоты рельефа (сетка 512×512 на ячейку, 0.5 м) |
| `game/worlds/<w>/cells/<XXX_YYY>/client/object.dat` | `WorldCell.cs:198-212` → `Models/CryEngine/Objects/ObjectsFile.cs` | префабы: кисти (`ObjectDataType1Brush`), воксели, **объёмы воды** (`ObjectDataType11Water`) |
| `game/worlds/<w>/zone/<zoneKey>/*.bai` и `.../paths/<X>_<Y>/*.bai` | `Models/CryEngine/Loaders/BaseBaiLoader.cs:27-241` | навмеш: узлы, связи, запрещённые зоны, **барьеры для кораблей** |
| `game/worlds/<w>/level_design/zone/<id>/client/transfer_path.xml` | `GameData/TransferGameData.cs:148-220` | маршруты карет/дирижаблей |

**Не читается вообще:** `.cgf`/`.cga` (меши и физ-прокси), `.x2d`, `npc_spawners.g`, `doodad.g`,
`visareas`. Единственный «cgf» в коде — локальный шим `namespace CgfConverter.Structs`
(`Models/CryEngine/CgfConverterStructsShim.cs`: одна структура `Matrix3x4`), внешнего пакета
CgfConverter у нас нет.

**Спавнеры НПС** приходят из SQLite-таблицы `npc_spawners`
(`GameData/NpcGameData.cs:107`, `SELECT * FROM npc_spawners`) — в AAC3500 **нет** парсера
`.g`-файлов спавнеров и нет `Services/NpcSpawnAuthoringImportService.cs` (он есть только в
соседней линии, §7.3). Наше авторское дерево `npc-spawners` — это **источник данных** для
импорта, а не код сервера.

**Доступ к данным:** AAPak читается **на месте**, без распаковки
(`AAEmu.Commons/Utils/AAPak/AAPak.cs` + `AAEmu.Game/IO/ClientFileManager.cs`,
`IO/ClientSource.cs`: `Directory` либо `GamePak`). Локально 3.5-клиент есть:
`E:\games\MAA-3500\game_pak` — 35 ГБ одним файлом. Конфигурация:
`Configurations/ClientData.json` → `ClientData.Sources` (`Config.Local.json`, user-secrets).

### 5.4. Зоны

Зоны 129..405 — это **zone key** из клиентского `world.xml`:
`Models/Game/World/Xml/XmlWorldZone.cs:33` (`worldTemplate.ZoneKeys.Add(Id)`) и
`WorldTemplate.ZoneKeyByRegions[sectorX, sectorY]` (`XmlWorldSector.cs:24`);
`WorldManager.GetZoneId` (`WorldManager.cs:732-747`) отдаёт этот ключ, он же едет в
`Transform.ZoneId` и в `ShipMoveType.ZoneId`. В SQLite-таблице `zones` — **другая** нумерация
(`ZoneManager._zoneIdToKey`, `Core/Managers/World/ZoneManager.cs:88-109`): id базы ≠ zone key.
Это важно для любого переноса: у ArcheageZones `ZoneLevelInfo` — это как раз world.xml-структура.

### 5.5. Репликация кораблей (что нельзя ломать)

`PhysicsManager.SendUpdatedMovementData` (`:700-814`) → `SCOneUnitMovementPacket` с
`ShipMoveType` (`Models/Game/Units/Movements/ShipMoveType.cs`: позиция, скорости, углы,
`Steering`, `Throttle`, `Rpm`, `ZoneId`, `Stuck`). Сглаживание — `ReplicationSmoothing`
(разные λ для свободного движения и контакта: горизонталь 22/11, вертикаль 8/4, крен 4/2),
плюс визуальные надбавки (крен в повороте, волновая pitch, trim у берега). Любая замена
динамики кораблей бьёт по этому пути.

---

## 6. Наше дерево `npc-spawners` — тот же формат, что читают зонные стеки

`D:\DEVELOPMENT\archeage\SRC\_melcosoft\archeage\npc-spawners` (5196 файлов):

```
<world>/
├── level_design/zone/129..405 (111 зон main_world)
│   ├── editor/npc_spawners.g + npc.xml        ← 115 файлов
│   ├── client/quest_sign_sphere.g, transfer_path.xml
│   └── world_server/district.xml, housing_area.g,
│       quest_area_sphere.g, return_point.g, spawn_point.g
├── level_design/cells/<cell>/doodad.g         ← 2998 .g всего
└── world.xml (13 МБ) + doodad/slave/transfer spawns.json
```

- Формат `npc_spawners.g` — тот же, что ожидает dedicate в zone-3.5: их
  `AAEmu.WorldServer/Docs/WORLD_DEDICATE_RUNBOOK.md:27` требует
  `Game/worlds/<world>/level_design/zone/<id>/zone_server/npc_spawners.g` (в CN 10.0-пак'е это
  есть; извлекается AAPakCLI). У нас `zone_server/`-вариант существует только для
  `instance_sal_temple_hard/zone/319`; в main_world спавнеры лежат под `editor/`.
- **Уточнение по нашему серверу:** в AAC3500 спавнеры импортируются в SQLite-таблицу
  `npc_spawners` (`GameData/NpcGameData.cs:107`), а `.g`-парсер живёт в **соседней линии**
  (`Services/NpcSpawnAuthoringImportService.cs`, §7.3), не здесь. То есть «наш контент-пайплайн
  уже говорит на языке зонных стеков» — верно только про формат данных, но не про код AAC3500.
- В дереве нет террейна (нет `heightmap.dat`; только 6 `.x2d`, 144 `.bai`) — это спавнер-авторинг,
  а не полный `game\`-дерево клиента.

---

## 7. Выводы: варианты и оценки

### 7.1. Варианты (переоценка для AAC3500)

| Вариант | Суть | Оценка для AAC3500 |
|---|---|---|
| **A. Перейти на их стек целиком** (ArcheageServer+ArcheageZones или zone-3.5) | Смена сервера и версии игры на 10.x-клиент | Не «замена физики», а отказ от 3.5.0.3 (теряем свою БД, протокол, GM-скрипты) |
| **B1. Жить на форке zone-3.5** | Их World+Game+Login+нативный dedicate вместо нашего сервера | Бинарей нативного dedicate локально нет, CN-клиент 10.0.2.13 отсутствует, контентная эра — 10.0. **Отклонено** |
| **B2. Портировать их слой к нам** | Dual-wire + V10-кодеки + ZoneHost-супервизор в наш 3.5.0.3; зона = 10.0 dedicate | Сохраняем свой код и БД, но зонный контент всё равно 10.0; ~150 файлов клея против наших моделей; месяцы. **Отклонено** |
| **C. Геометрия внутрь монолита** | Перенос геометрии (heightmap + кисти/воксели + прокси + вода) в наш сервер | **Реалистичный путь, единственный, сохраняющий контентную эру 3.5.** Недели |

### 7.2. Ключевые аргументы

1. **B не быстрее C в один шаг.** Первый майлстоун B («их читатели понимают наши 3.5-данные») —
   это в точности первая задача C. B = C + клей (~150 файлов) + двойная истина для NPC-состояния
   (квесты/лут/скиллы/аггро) + маппинг контента + ops.
2. Для НПС кинематический подход (heightmap + рейкасты) — не «альтернатива», а воспроизведение
   оригинала: NPC в ArcheAge всегда были кинематикой. В AAC3500 Jitter2 для НПС и не
   применялся — тел НПС нет; значит, переход на рейкасты **ничего не ломает** в текущем коде,
   а наоборот закрывает дефицит слоёв (мосты/интерьеры/потолки/стансы воды).
3. Для кораблей C сохраняет нашу динамику (Buoyancy, импульсные взаимодействия, `ShipMoveType` +
   `ReplicationSmoothing`). Переход на их `HullSystem` — отдельное решение по фиделити (3-DOF,
   roll/pitch «level», киль/фриборд масштабируются «guess until another model grounds in a trace» —
   их собственная оговорка) и ломает нашу репликацию.
4. **Решающий вопрос для B-путей — контентная эра:** «физика как в оригинале» в зонной архитектуре
   достигается ценой 10.0-контента (CN-дерево) при 3.5-клиенте. Если эра 3.5 обязательна — только C.

### 7.3. Существенно: вариант C уже реализован в соседней 3.5-линии

В `_melcosoft\archeage\game-server-3.5.0.3` (ветка `3.5`) **тот же вариант C уже сделан** и
подтверждён в бою:

| Слой | Файл(ы) | Размер |
|---|---|---|
| Чтение геометрии ячеек 3.5 | `Physics/GodotGeo/Source/{CgfModelReader,CellObjectReader,CellEntityReader,TerrainCellReader}.cs` | ~115 КБ |
| Чтение CryPhysics-прокси | `Models/CryEngine/CgfConverter/**` (`MeshPhysicsData800Reader.cs`, `ChunkMeshPhysicsData*.cs`) | ~5 КБ |
| Индекс статической геометрии + рейкасты | `Physics/WorldStaticGeometryIndex.cs`, `Physics/StaticGeometryVerticalIndex.cs` | ~103 КБ |
| Высота НПЦ по слоям | `Models/Game/NPChar/{NpcHeightResolver,NpcHeightState,NpcWaterSurfaceOverrides,NpcSpawnHeightOverrides}.cs` | ~1 КБ LOC |
| Мотор НПЦ (опора, гравитация, луч вниз) | `Physics/GodotGeo/{GodotGeoNpcMotor,GodotGeoNavigator}.cs` | ~51 КБ |
| Тесты | ~40 файлов (4176 зелёных тестов) | — |

Результат, зафиксированный в их отчёте (`Docs/reports/2026-10-05-GodotGeo-Replacement-Result.md`):
«NPC идёт по видимой поверхности» (лестницы многоэтажки, ячейка `027_007`, 6 контрольных точек,
отличие ≤ 0.204 м), при рабочем сете ~5.5 ГБ.

**Практический вывод:** переносить из ArcheageZones имеет смысл только недостающие куски —
`CgfPhysicsReader` (примитивы прокси: box/trimesh/sphere/cylinder/capsule, 379 LOC) и
`ZoneWater`/`StaticCollisionOptions` (правила стансов воды, ~240 LOC). Остальное (heightmap,
объёмы воды, размещение кистей, индекс, рейкасты) в нашей 3.5-линии уже есть или уже написано
в соседнем дереве.

---

## 8. Предложенный спайк

Спайк переписан под AAC3500 (детали — в
[`2026-11-15-zone-physics-implementation-plan.md`](./2026-11-15-zone-physics-implementation-plan.md), §5):

1. **P0 — вертикаль на реальной ячейке.** Перенести self-contained читатели
   (`CgfModelReader`, `CellObjectReader`, `CellEntityReader`, `TerrainCellReader`) в песочницу и
   прочитать ячейку `027_007` из `E:\games\MAA-3500\game_pak`: сверить числа
   (1189 моделей, 1 012 499 треугольников, 1186 brushes, 3 voxels), найти
   `ha_palace_stairs.cgf` (`ObjectDat:1166071` / `1166830`), выгрузить реальные треугольники в
   OBJ/CSV и сравнить с рельефом (`WorldTemplate.GetHeight`).
2. **P1 — прокси против рендера.** Подключить `CgfPhysicsReader` (ArcheageZones) и/или
   `MeshPhysicsData` (соседняя линия) и снять A/B-таблицу «прокси vs рендер» на 20–30 ассетах:
   стены, мебель, причалы, скалы.
3. **P2 — индекс и рейкаст.** Построить BVH по placement-ам кистей/вокселей и проверить луч вниз
   на лестнице (ожидаемая погрешность ≤ 0.25 м) и на открытой земле (≤ 0.05 м против heightmap).
4. **P3 — вода.** Перенести правила стансов (`ZoneWater`) и проверить переходы
   Swim/Stand/Settle на реке/озере.
5. Параллельно — **выбор формы интеграции**: общий пакет `AAEmu.Geometry` без зависимости от
   Jitter2 и `AAEmu.Game` (доступ к клиентским данным через интерфейс), чтобы одна реализация
   обслуживала и AAC3500, и соседнюю 3.5-линию.

**Критерий развилки:** если P0 даёт расхождение с соседней линией (формат `.cgf` в 3.5 отличается)
— приоритет P1/P2 уходит на собственный парсер; если P0/P1 зелёные — цель C достижима в
AAC3500 без зонехоста вообще.

---

## 9. Ссылки

**Наш проект (AAC3500) — физика и геометрия**
```
AAEmu.Game/Core/Managers/World/PhysicsManager.cs                       :82/:101/:138/:162/:215/:474/:903
AAEmu.Game/Physics/ShipController.cs                                    :270 Build (BoxShape), :305 ApplyForceAndTorque
AAEmu.Game/Physics/Forces/Buoyancy.cs                                  :201 PreStep, :248 ApplyDrag
AAEmu.Game/Physics/ShipShoreInteraction.cs                              :77/:127/:136
AAEmu.Game/Physics/ShipCliffInteraction.cs                             :66/:120
AAEmu.Game/Physics/ShipShipInteraction.cs                              :83 ResolveAllPairs (SAT)
AAEmu.Game/Physics/ShipStaticBarrierInteraction.cs + ShipStaticBarrierBaiIngestor.cs
AAEmu.Game/Physics/HeightMaps/{Heightmap.cs:13, HeightmapTester.cs:93, HeightmapDetection.cs:24}
AAEmu.Game/Models/Game/World/WorldCell.cs                              :135 heightmap.dat, :198 object.dat
AAEmu.Game/Models/Game/World/WorldTemplate.cs                          :118/:174 высоты рельефа
AAEmu.Game/Models/CryEngine/Objects/ObjectsFile.cs                     :17 чтение object.dat
AAEmu.Game/Models/CryEngine/Objects/ObjectDataType1Brush.cs            трансформация @0x47, asset-index @0x7F
AAEmu.Game/Models/Game/World/WaterBodies.cs                            :294/:401 объёмы воды
AAEmu.Game/Core/Managers/AiGeodataManager.cs                            :259 высота НПЦ
AAEmu.Game/Core/Managers/World/WorldManager.cs                          :732/:757/:791/:828 зоны и высоты
AAEmu.Game/Models/Game/NPChar/Npc.cs                                   :1173 MoveTowards
AAEmu.Game/Models/Game/AI/AStar/PathNode.cs                           :74 FindPath (A* по .bai)
AAEmu.Game/Core/Managers/AIManager.cs                                  :27 тик ИИ 10 Гц
AAEmu.Game/Models/Game/Units/Movements/ShipMoveType.cs                 репликация корабля
AAEmu.Game/IO/ClientFileManager.cs, AAEmu.Commons/Utils/AAPak/AAPak.cs доступ к game_pak
```

**Соседняя 3.5-линия (источник переносимого кода)**
```
AAEmu.Game/Physics/GodotGeo/Source/{CgfModelReader,CellObjectReader,CellEntityReader,TerrainCellReader}.cs
AAEmu.Game/Physics/{WorldStaticGeometryIndex,StaticGeometryVerticalIndex}.cs
AAEmu.Game/Models/Game/NPChar/{NpcHeightResolver,NpcHeightState,NpcWaterSurfaceOverrides,NpcSpawnHeightOverrides}.cs
AAEmu.Game/Models/CryEngine/CgfConverter/**  (MeshPhysicsData800Reader)
AAEmu.Game/Services/NpcSpawnAuthoringImportService.cs
Docs/reports/2026-10-05-GodotGeo-Replacement-Result.md
```

**ArcheageZones (10.x) — точечные заимствования**
```
src/AAEmu.Zone/Core/Sim/Physics/CgfPhysicsReader.cs        — примитивы прокси (379 LOC)
src/AAEmu.Zone/Core/Sim/Physics/ZoneWater.cs               — правила стансов воды (196 LOC)
src/AAEmu.Zone/Core/Sim/Physics/StaticCollisionOptions.cs  — биты политики (43 LOC)
src/AAEmu.Zone/Core/Data/ZoneHeightmap.cs                  — фиксы квантования >1024 м и low-res секторов
src/AAEmu.Zone/Core/Sim/Hulls/{HullBody,HullContacts}.cs   — 3-DOF хулл (не переносим, см. §7.2 п.3)
```

**Внешние данные**
```
D:\DEVELOPMENT\archeage\SRC\_melcosoft\archeage\npc-spawners   — наше авторское дерево спавнеров
E:\games\MAA-3500\game_pak                                   — 3.5-клиент (AAPak, 35 ГБ), читается на месте
```
