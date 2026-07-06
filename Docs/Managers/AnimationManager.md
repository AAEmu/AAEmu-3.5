# AnimationManager

## Описание

`AnimationManager` загружает шаблоны анимаций из `compact.sqlite3` и дополняет их
длительностями из клиентского файла `game/combat_sync_event_list.g`.
Менеджер сам не обрабатывает сетевые пакеты; анимации используются через
`SkillManager` и `PlotManager`.

## Источники данных

| Источник | Тип | Путь/таблица |
|----------|-----|--------------|
| SQLite | Таблица | `compact.sqlite3`.`anims` |
| Клиентские данные | Файл | `ClientData/game/combat_sync_event_list.g` |

## Таблица `anims`

Проверена по `AAEmu.Game/Data/compact.sqlite3`.

| Поле в БД | Тип в БД | Свойство `Anim` | Тип в C# |
|-----------|----------|-----------------|----------|
| `id` | int | `Id` | `uint` |
| `category_id` | int | `Category` | `AnimCategory` |
| `hang_ub` | text | `HangUB` | `string` |
| `loop` | bool | `Loop` | `bool` |
| `move_ub` | text | `MoveUB` | `string` |
| `name` | text | `Name` | `string` |
| `relaxed_ub` | text | `RelaxedUB` | `string` |
| `ride_ub` | text | `RideUB` | `string` |
| `swim_move_ub` | text | `SwimMoveUB` | `string` |
| `swim_ub` | text | `SwimUB` | `string` |

## Контейнеры

```csharp
private Dictionary<uint, Anim> _animations = [];
private Dictionary<string, Anim> _animationsByName = [];
```

- `_animations` — поиск анимации по идентификатору (`GetAnimation(uint id)`).
- `_animationsByName` — поиск анимации по имени (`GetAnimation(string name)`).

## Пакеты, обрабатываемые менеджером

`AnimationManager` не содержит обработчиков пакетов.

## Связанные пакеты (через потребителей)

Имена пакетов взяты из `x2game-dev` (10.0.2.13-cn), структуры сверены с `x2game_dump.dll` (3503 AAC).

| Направление | Имя в IDA | Opcode | Потребитель |
|-------------|-----------|--------|-------------|
| C2S | `CSStartSkill_0x1A4` | 0x1A4 | `SkillManager` |
| S2C | `SCSkillStarted_0x1DB` | 0x1DB | `SkillManager` |
| S2C | `SCSkillFired_0x11B` | 0x11B | `SkillManager` |
| S2C | `SCSkillEnded_0x1BC` | 0x1BC | `SkillManager` |
| S2C | `SCSkillStopped_0x2AB` | 0x2AB | `SkillManager` |

### Структуры (кратко)

- `CSStartSkill_0x1A4`: `type` (uint32), далее вложенные блоки skill-данных.
- `SCSkillStarted_0x1DB`: блок `st` (`type` uint32), блок `sid` (`tl` uint16),
  вложенные skill-данные, `castSynergy` (bool).

## Примечания

- В таблице `anims` встречаются дубли по имени (`name`), они пропускаются
  (`_animationsByName.ContainsKey(template.Name)`).
- Длительность анимации (`Duration`, `CombatSyncTime`) берётся только для
  модели `nuian_male` в `combat_sync_event_list.g`.
