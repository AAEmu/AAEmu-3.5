# CashShopManager

## Описание

`CashShopManager` управляет внутриигровым магазином (ICS): загружает SKU,
товары и меню из MySQL, отправляет клиенту списки товаров и обрабатывает покупки.

## Источники данных

| Источник | Тип | Таблица |
|----------|-----|---------|
| MySQL | Таблица | `ics_skus` |
| MySQL | Таблица | `ics_shop_items` |
| MySQL | Таблица | `ics_menu` |
| MySQL | Таблица | `audit_ics_sales` (только для аудита/записи) |

`compact.sqlite3` / `compact.server.table.sqlite3` не используются.

## Контейнеры

```csharp
public Dictionary<uint, IcsSku> SKUs { get; set; } = [];
public Dictionary<uint, IcsItem> ShopItems { get; set; } = [];
public List<IcsMenu> MenuItems { get; set; } = [];
```

## Обрабатываемые пакеты

Имена пакетов взяты из `x2game_dump.dll` (3503 AAC).

| Направление | Имя в IDA | Opcode | Назначение |
|-------------|-----------|--------|------------|
| C2S | `CSICSMenuListRequest_0x106` | 0x106 | Запрос меню магазина |
| C2S | `CSICSGoodsListRequest_0x0CE` | 0x0CE | Запрос списка товаров |
| C2S | `CSICSBuyGoodRequest_0x0EC` | 0x0EC | Покупка товара |
| C2S | `CSIcsMoneyRequest_0x0AB` | 0x0AB | Запрос баланса |
| S2C | `SCICSMenuList_0x285` | 0x285 | Меню магазина |
| S2C | `SCICSGoodList_0x274` | 0x274 | Список товаров |
| S2C | `SCICSGoodDetail_0x0FF` | 0x0FF | Детали товара/SKU |
| S2C | `SCICSCheckTime_0x0E0` | 0x0E0 | Проверка времени/доступности |
| S2C | `SCICSBuyResult_0x093` | 0x093 | Результат покупки |
