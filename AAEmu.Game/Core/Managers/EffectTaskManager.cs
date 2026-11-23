using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Планирует задачи эффектов (например, dispel) через <see cref="ITaskManager"/>.
/// Метод <c>Load()</c> отсутствует; данные не загружаются из compact.sqlite3 / compact.server.table.sqlite3.
/// </summary>
public class EffectTaskManager(ITaskManager taskManager) : Singleton<EffectTaskManager>, IEffectTaskManager
{
    internal AuraTask AddAuraTask(Buff buff, double interval)
    {
        var task = new AuraTask(buff);
        taskManager.Schedule(task, TimeSpan.FromMilliseconds(interval));
        return task;
    }

    /// <summary>
    /// Pre-Variant of dispel effects...
    /// </summary>
    /// <param name="buff"></param>
    /// <param name="interval">milliseconds</param>
    public DispelTask AddDispelTask(Buff buff, double interval)
    {
        var task = new DispelTask(buff);
        taskManager.Schedule(task, TimeSpan.FromMilliseconds(interval));
        return task;
    }
}
