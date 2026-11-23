using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Core.Managers;

public interface IEffectTaskManager
{
    DispelTask AddDispelTask(Buff buff, double interval);
}
