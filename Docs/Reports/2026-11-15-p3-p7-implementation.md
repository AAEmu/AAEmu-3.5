# P3–P7: вода, высота НПЦ, физика кистей, стены — итог

**Дата:** 2026-11-15
**План:** [`2026-11-15-zone-physics-implementation-plan.md`](./2026-11-15-zone-physics-implementation-plan.md)
**Предыдущие фазы:** [P1](./2026-11-15-p1-proxy-vs-render.md), [P2](./2026-11-15-p2-index-raycast.md)
**Состояние:** код в репозитории; сборка решения — 0 ошибок; тесты 1122/1124 (2 падения в баффах были до работы).

---

## P3. Правила стансов воды

`ZoneWater` уже знал объёмы, поверхность и `TrySurface`, но **не решал, какой станс** получает юнит.
Добавлено:

| Что                                                        | Где                                   | Содержание                                                                                              |
| ---------------------------------------------------------- | ------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| `WaterStance` (`Unchanged`/`Stand`/`Swim`/`Settle`)        | `AAEmu.Geometry/Water/StanceRules.cs` | Результат правил                                                                                        |
| `StanceRules.Resolve(depth, heightAboveFloor, floorKnown)` | там же                                | Пороги: `MinDepth 0.3`, `OffBed 0.3`, `BedDepth 1.25` — те же, что вписал `ZoneWater` по трассам натива |
| `ZoneWater.ResolveStance(x, y, z, floorZ)`                 | `Water/ZoneWater.cs`                  | Правила от самой геометрии зоны                                                                         |
| `ZoneWater.SurfaceSwimZ(...)`                              | там же                                | Глубина плавания: море — по модели (`SurfaceSwimDepth`), пресная вода — `MinDepth`                      |
| `NpcGroundResolver.WaterStanceAt(...)`                     | `AAEmu.Game/Physics/Static`           | Станс для НПЦ через `SolidFloor`                                                                        |

Ключевое правило: в толще воды (`≥ 1.25 м` под поверхностью) — **Swim даже сидя на дне**; у дна на
мелководье — **Settle**, если под юнитом меньше `0.3 м`; без известного дна — не Settle, а Swim.
Все пороги — константы из `ZoneWater`, а не копии, поэтому разойтись не могут.

Тесты: `StanceRulesTests` — 7 штук (пороги, над поверхностью, в толще, у дна, мелководье, неизвестное дно,
shorthand по трём Z).

---

## P4. Слой высоты НПЦ

`NpcGroundResolver` был **мёртвым кодом** — его никто не создавал и не вызывал. Подключено:

-   `Program.cs` — `AddSingleton<StaticGeometryManager>` и `AddSingleton<NpcGroundResolver>`;
-   `GameService.StartAsync` — `GeometryLogBridge.Attach()` (логи пакета → NLog) и передача резолвера в
    `WorldManager.NpcGroundResolver`;
-   `WorldManager.GetReferenceHeight` — слой из плана **перед** старым путём:

```
1. летающий НПЦ → высота спавнера
2. HoldPosition / Idle → высота спавнера
3. NpcGroundResolver.Ground(...)   ← новый слой: пол кисти → подземный зал → рельеф → навмеш
4. TryGetHeight(zoneKey, ...)      ← прежний путь, если резолвер не зарегистрирован
5. высота спавнера
```

Резолвер — **опциональное** свойство: если слой статики не зарегистрирован, сервер ведёт себя как раньше.
`NavHeight` берёт высоту навмеша, только когда включён `GeoDataMode`.

Попутно исправлен `TerrainHeight` в резолвере: он читал `h <= 0f` как «нет данных» — теперь использует
`WorldTemplate.TryGetHeight` (дефект §3.3 п.4 в новом месте).

---

## P5. Физика: fixed-step и тела кистей

**Fixed-step.** Было `_physWorld.Step((float)physicsTotalDelta.TotalSeconds, false)` — переменный шаг,
зависящий от джиттера `Thread.Sleep`. Стало: накопление `accumulatedPhysicsTime` и цикл шагов ровно по
`1 / TargetPhysicsTps` (25 Гц ⇒ 0.04 с), с потолком `MaxCatchUpSeconds = 0.25` — после долгой остановки
мир не пытается отыграть целую секунду контактов сразу.

**Тела кистей.** `PhysicsManager.AddStaticGeometry(collision)` создаёт по одному статическому
`RigidBody` с `BoxShape` на каждый инстанс кисти. Источник — `StaticCollision.StaticShapes()`
(новый метод пакета: центр + полуразмеры world-бокса, в игровой системе координат Z-up).
Координаты переводятся в Jitter2 тем же свопом `(X, Z, Y)`, что использует весь `PhysicsManager`.

Почему бокс, а не trimesh: натив-дядюшка физикализует бокс кисти, и это то же число тел, что и
размещений (иначе 2.6 млн треугольников зоны 129 превратились бы в миллионы hull-тел).

**Событие `StaticGeometryManager.ZoneBuilt`** — хук, чтобы зона, построенная для высоты НПЦ, могла
попасть и в физику.

---

## P6. Стены

-   `StaticGeometryManager.Raycast(template, zoneKey, from, to, out fraction, out normal, floorOnly)` —
    луч по статике зоны. `floorOnly: true` отбрасывает всё круче 60° (`FloorMinUpNormal`), `false` видит и стены.
-   `NpcGroundResolver.IsBlocked(template, zoneKey, fromX, fromY, toX, toY, height)` — горизонтальный луч
    на высоте корпуса юнита. Вызывающая сторона (поведения ИИ) решает, отменять ли шаг.

Полное подключение к `AiNavigator`/`RoamingBehavior` не сделано: это меняет поведение живых НПЦ и
требует прогона на сервере, а не только на геометрии. Примитив готов и покрыт тестами
(`Raycast_SeesAWallButFloorQueryDoesNot`).

---

## P7. Тесты и проверки

| Набор                         | Штук | Статус                                                                 |
| ----------------------------- | ---- | ---------------------------------------------------------------------- |
| `StaticCollisionOptionsTests` | 5    | зелёные                                                                |
| `CollisionAssetCacheTests`    | 8    | зелёные                                                                |
| `StanceRulesTests`            | 7    | зелёные                                                                |
| `StaticCollisionQueryTests`   | 6    | зелёные                                                                |
| остальные (`AAEmu.UnitTests`) | 1098 | 1096 зелёных, **2 падения в баффах** (`buff.AbLevel`) — были до работы |

`StaticCollisionQueryTests` строит **синтетический `object.dat`** (`ObjectDat.WithBoxBrushes`) и проверяет
на нём всю цепочку: размещения → бокс → `TryFloor` → `Raycast` → `AnyBoxOverlaps` → `StaticShapes`.
Клиентские данные для тестов не нужны.

Итог `dotnet build` решения — **0 ошибок**.

---

## Что осталось открытым

1. **P6 в живом ИИ** — подключить `IsBlocked` к шагу поведения (нужен серверный прогон).
2. **LRU для мешей** — `CollisionAssetCache` держит слабые ссылки, явного лимита RAM нет (вопрос §9 п.2 плана).
3. **P5 для кораблей на живой зоне** — тела добавляются через событие, но на боевом сервере ещё не
   проверялось, что 5–6 тыс. статических тел на зону не просаживают 25 Гц.
4. Семантика `AiGeodataManager.GetHeight` (`0` vs «нет данных») — рефакторинг 31 вызывающего.
