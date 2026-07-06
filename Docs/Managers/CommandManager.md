# CommandManager

## Описание

`CommandManager` регистрирует и выполняет GM/административные команды,
получаемые из чата. Уровень доступа к команде проверяется через
`AccessLevelManager`.

## Источники данных

- C#-скрипты команд: `AAEmu.Game/Scripts/Commands`
- Уровни доступа: `AppConfiguration.AccessLevel`

`compact.sqlite3` / `compact.server.table.sqlite3` не используются.

## Контейнеры

```csharp
private readonly Dictionary<string, ICommand> _commands = [];
private readonly Dictionary<string, string> _commandAliases = [];
```

## Обрабатываемые пакеты

| Направление | Имя в IDA | Примечание |
|-------------|-----------|------------|
| C2S | `CSSendChatMessagePacket` | Клиент отправляет сообщение/команду |
