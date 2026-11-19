using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.AI.v2.Framework;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Управляет тиками NPC AI.
/// Метод <c>Load()</c> отсутствует; данные не загружаются из compact.sqlite3 / compact.server.table.sqlite3.
/// </summary>
public class AIManager : Singleton<AIManager>, IAIManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _initialized = false;

    private List<NpcAi> _npcAis;
    private object _aiLock;

    public void Initialize()
    {
        if (_initialized)
            return;

        _npcAis = [];
        _aiLock = new object();
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(100), true);

        _initialized = true;
    }

    public void AddAi(NpcAi ai)
    {
        lock (_aiLock)
        {
            _npcAis.Add(ai);
        }
    }

    public void Tick(TimeSpan delta)
    {
        // Снимок под замком, тик — вне него. Держать _aiLock на время тика нельзя: тик боя убивает юнита
        // (Skill → DoDie → NpcSpawner.DoDespawn, которому нужен замок спавнера), а спавн (NpcSpawner.DoSpawn →
        // NpcManager.Create) добавляет AI через AddAi и ждёт этот же _aiLock. Две стороны брали замки в обратном
        // порядке и вешали весь сервер. Зона тикает AI без общего замка списка — так же делаем и здесь.
        NpcAi[] snapshot;
        lock (_aiLock)
            snapshot = _npcAis.ToArray();

        foreach (var npcai in snapshot)
        {
            try
            {
                if (npcai.Owner != null)
                    npcai.Tick(delta);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }
    }

    public void Stop()
    {
        Logger.Debug($"Stopping AIManager");
        TickManager.Instance.OnTick.UnSubscribe(Tick);
    }
}
