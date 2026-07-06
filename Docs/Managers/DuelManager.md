# DuelManager

## Описание

`DuelManager` управляет дуэлями между персонажами: отправкой вызова,
началом боя, проверкой расстояния до флага, проверкой здоровья и завершением.

## Источники данных

Нет внешних источников данных.

`compact.sqlite3` / `compact.server.table.sqlite3` не используются.

## Контейнеры

```csharp
private readonly ConcurrentDictionary<uint, Duel> _duels = new();
public Dictionary<uint, FactionsEnum> SaveFactions { get; set; } = [];
```

## Обрабатываемые пакеты

Имена пакетов взяты из `x2game_dump.dll` (3503 AAC).

| Направление | Имя в IDA | Opcode | Назначение |
|-------------|-----------|--------|------------|
| C2S | `CSChallengeDuel_0x02B` | 0x02B | Вызов на дуэль |
| C2S | `CSStartDuel_0x00E` | 0x00E | Принятие дуэли |
| S2C | `SCDuelChallenged_0x0BD` | 0x0BD | Уведомление о вызове |
| S2C | `SCDuelStartCountdown_0x184` | 0x184 | Обратный отсчёт |
| S2C | `SCDuelStarted_0x100` | 0x100 | Дуэль началась |
| S2C | `SCDuelEnded_0x22A` | 0x22A | Дуэль завершена |
| S2C | `SCDuelState_0x193` | 0x193 | Состояние дуэли |
