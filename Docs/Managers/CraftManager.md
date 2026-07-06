# CraftManager

## Описание

`CraftManager` загружает рецепты крафта, их продукты и материалы из
`compact.sqlite3`.

## Источники данных

| Источник | Тип | Таблица |
|----------|-----|---------|
| SQLite | Таблица | `compact.sqlite3`.`crafts` |
| SQLite | Таблица | `compact.sqlite3`.`craft_products` |
| SQLite | Таблица | `compact.sqlite3`.`craft_materials` |
| SQLite | Таблица | `compact.sqlite3`.`craft_pack_crafts` |

## Таблица `crafts`

| Поле в БД | Используется | Свойство `Craft` |
|-----------|--------------|------------------|
| `id` | Да | `Id` |
| `cast_delay` | Да | `CastDelay` |
| `tool_id` | Да | `ToolId` |
| `skill_id` | Да | `SkillId` |
| `wi_id` | Да | `WiId` |
| `req_doodad_id` | Да | `ReqDoodadId` |
| `need_bind` | Да | `NeedBind` |
| `ac_id` | Да | `AcId` |
| `actability_limit` | Да | `ActabilityLimit` |
| `show_upper_crafts` | Да | `ShowUpperCraft` |
| `recommend_level` | Да | `RecommendLevel` |
| `visible_order` | Да | `VisibleOrder` |
| `desc` | Нет | — |
| `products_pack_id` | Нет | — |
| `title` | Нет | — |
| `use_only_actability` | Нет | — |

## Таблица `craft_products`

| Поле в БД | Свойство `CraftProduct` |
|-----------|-------------------------|
| `id` | `Id` |
| `craft_id` | `CraftId` |
| `item_id` | `ItemId` |
| `amount` | `Amount` |
| `rate` | `Rate` |
| `show_lower_crafts` | `ShowLowerCrafts` |
| `use_grade` | `UseGrade` |
| `item_grade_id` | `ItemGradeId` |

## Таблица `craft_materials`

| Поле в БД | Используется | Свойство `CraftMaterial` |
|-----------|--------------|--------------------------|
| `id` | Да | `Id` |
| `craft_id` | Да | `CraftId` |
| `item_id` | Да | `ItemId` |
| `amount` | Да | `Amount` |
| `main_grade` | Да | `MainGrade` |
| `require_grade` | Нет | — |
| `upper_grade` | Нет | — |

## Таблица `craft_pack_crafts`

| Поле в БД | Примечание |
|-----------|------------|
| `craft_id` | Отмечает крафт как упаковочный (`Craft.IsPack = true`) |

## Контейнеры

```csharp
private Dictionary<uint, Craft> _crafts;
```

## Обрабатываемые пакеты

`CraftManager` сам не обрабатывает пакеты. Крафт запускается через `SkillManager`.
