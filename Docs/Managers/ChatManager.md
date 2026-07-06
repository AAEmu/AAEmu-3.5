# ChatManager

## Описание

`ChatManager` управляет игровыми чат-каналами: фракция, нация, зона, группа,
рейд, гильдия и семья. Каналы создаются по требованию и хранятся в памяти.

## Источники данных

Нет внешних источников данных.

`compact.sqlite3` / `compact.server.table.sqlite3` не используются.

## Контейнеры

```csharp
private ConcurrentDictionary<FactionsEnum, ChatChannel> FactionChannels { get; } = new();
private ConcurrentDictionary<long, ChatChannel> NationChannels { get; } = new();
private ConcurrentDictionary<long, ChatChannel> ZoneChannels { get; } = new();
private ConcurrentDictionary<long, ChatChannel> PartyChannels { get; } = new();
private ConcurrentDictionary<long, ChatChannel> RaidChannels { get; } = new();
private ConcurrentDictionary<FactionsEnum, ChatChannel> GuildChannels { get; } = new();
private ConcurrentDictionary<long, ChatChannel> FamilyChannels { get; } = new();
```

## Обрабатываемые пакеты

`ChatManager` сам не обрабатывает пакеты. Связанные пакеты чата (x2game 3.5.0.3):

- `CSSendChatMessagePacket`
- `SCChatMessagePacket`
- `SCChatFailedPacket`
- `SCChatBubblePacket`
