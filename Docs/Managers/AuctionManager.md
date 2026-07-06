# AuctionManager

## Описание

`AuctionManager` управляет активными лотами аукциона: загрузкой из MySQL,
созданием новых лотов, ставками, поиском и отправкой почты при завершении.

## Источники данных

| Источник | Тип | Таблица/файл |
|----------|-----|--------------|
| MySQL | Таблица | `auction_house` |

`compact.sqlite3` / `compact.server.table.sqlite3` не используются.

## Таблица `auction_house`

Поля, используемые при загрузке:

| Поле | Тип в C# | Примечание |
|------|----------|------------|
| `id` | `ulong` | Идентификатор лота |
| `duration` | `AuctionDuration` | 8=6ч, 9=12ч, 10=24ч, 11=48ч |
| `item_id` | `uint` | Ссылка на предмет |
| `post_date` | `DateTime` | Дата размещения |
| `end_time` | `DateTime` | Дата окончания |
| `world_id` | `byte` | Мир |
| `client_id` | `uint` | Продавец |
| `client_name` | `string` | Имя продавца |
| `start_money` | `int` | Стартовая цена |
| `direct_money` | `int` | Цена выкупа |
| `bid_world_id` | `byte` | Мир ставки |
| `bidder_id` | `uint` | Текущий покупатель |
| `bidder_name` | `string` | Имя покупателя |
| `bid_money` | `int` | Текущая ставка |
| `extra` | `int` | Дополнительное поле |

## Контейнеры

```csharp
public ConcurrentDictionary<ulong, AuctionLot> AuctionLots { get; } = [];
public ConcurrentBag<long> _deletedAuctionItemIds { get; } = [];
```

## Обрабатываемые пакеты

Имена пакетов взяты из `x2game_dump.dll` (3503 AAC).

| Направление | Имя в IDA | Opcode | Назначение |
|-------------|-----------|--------|------------|
| C2S | `CSAuctionPost_0x136` | 0x136 | Размещение лота |
| C2S | `CSAuctionSearch_0x033` | 0x033 | Поиск лотов |
| C2S | `CSAuctionMyBidList_0x0EA` | 0x0EA | Список ставок игрока |
| C2S | `CSAuctionLowestPrice_0x15A` | 0x15A | Запрос минимальной цены |
| S2C | `SCAuctionPosted_0x0E1` | 0x0E1 | Подтверждение размещения |
| S2C | `SCAuctionCanceled_0x0BB` | 0x0BB | Отмена лота |
| S2C | `SCAuctionBid_0x264` | 0x264 | Результат ставки |
| S2C | `SCAuctionSearched_0x219` | 0x219 | Результат поиска |
| S2C | `SCAuctionLowestPrice_0x0EF` | 0x0EF | Минимальная цена |
