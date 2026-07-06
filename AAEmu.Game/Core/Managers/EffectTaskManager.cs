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
    /// <summary>
    /// Pre-Variant of dispel effects...
    /// </summary>
    /// <param name="buff"></param>
    /// <param name="interval">milliseconds</param>
    public void AddDispelTask(Buff buff, double interval)
    {
        var task = new DispelTask(buff);
        taskManager.Schedule(task, TimeSpan.FromMilliseconds(interval)); // TODO create normal effect schedule
    }
}
