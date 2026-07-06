using AAEmu.Commons.Utils;
using AAEmu.Game.Models;
using Microsoft.Extensions.Options;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class AccessLevelManager(IOptions<AppConfiguration> options) : Singleton<AccessLevelManager>, IAccessLevelManager
{
    private readonly List<Command> CMD = [];
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Загружает уровни доступа к GM-командам из конфигурации приложения.
    /// Источник данных: <see cref="AppConfiguration.AccessLevel"/> (словарь «имя команды → уровень доступа»).
    /// Таблицы compact.sqlite3: не используются.
    /// Таблицы compact.server.table.sqlite3: не используются.
    /// Связанные сетевые пакеты (сверено с IDA Pro MCP, x2game-dev.dll 10.0.2.13-cn):
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       CSSendChatMessagePacket (C2G, 0x098 @ 0x39c770a0) — клиент отправляет сообщение/команду в чат.
    ///       Поля: cliLocale (u8), chat (s64), target (string[128]), targetWorldId (u8), msg (string[1023]), LanguageType (u8).
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       SCChatMessagePacket (G2C, 0x102 @ 0x39c71c30) — сервер рассылает сообщение другим клиентам.
    ///       Поля: cliLocale (u8), chat (s64), quantized (3 bit @ offset 32), type (s64), LanguageType (u8), CharRace (u8), type (u32), name (string[1024]), msg (string[1023]), ability (s32), option (u8), worldId (u8), isUserChat (bool).
    ///     </description>
    ///   </item>
    /// </list>
    /// </summary>
    public void Load()
    {
        Logger.Info("Loading CommandAccessLevels...");

        foreach (var (cmdName, cmdLevel) in options.Value.AccessLevel)
            CMD.Add(new Command { CommandName = cmdName, CommandLevel = cmdLevel });

        Logger.Info($"Loaded {CMD.Count} CommandAccessLevels");
    }

    public int GetLevel(string commandStr)
    {
        var result = CMD.Find(o => o.CommandName == commandStr);
        if (result != null)
            return result.CommandLevel;

        return 100;
    }
}

public class Command
{
    public string CommandName { get; set; }
    public int CommandLevel { get; set; }
}
