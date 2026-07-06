# CrimeManager

## Описание

`CrimeManager` управляет событиями преступлений: загружает их из MySQL,
создаёт улики, начисляет очки преступления и сохраняет изменения.

## Источники данных

| Источник | Тип | Таблица |
|----------|-----|---------|
| MySQL | Таблица | `crime` |

`compact.sqlite3` / `compact.server.table.sqlite3` не используются.

## Таблица `crime`

| Поле в БД | Свойство `CrimeEvent` |
|-----------|-----------------------|
| `id` | `Id` |
| `criminal` | `Criminal` |
| `victim` | `Victim` |
| `reporter` | `Reporter` |
| `doodad_template` | `DoodadTemplate` |
| `crime_type` | `CrimeKind` |
| `zone_key` | `ZoneKey` |
| `x`, `y`, `z` | `Position` |
| `crime_time` | `CrimeTime` |
| `report_time` | `ReportTime` |
| `arg1` | `Arg1` |
| `arg2` | `Arg2` |
| `arg3` | `Arg3` |
| `msg` | `Msg` |

## Контейнеры

```csharp
private ConcurrentDictionary<ulong, CrimeEvent> CrimeEvents { get; } = [];
private List<ulong> DeletedEventIds { get; } = [];
private List<ulong> UpdatedEventIds { get; } = [];
```

## Обрабатываемые пакеты

`CrimeManager` сам не обрабатывает пакеты. Улики и очки преступления
обрабатываются через системы doodad и персонажа.
