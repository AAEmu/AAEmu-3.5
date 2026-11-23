using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Tasks.Skills;

public class DispelTask(Buff buff) : Task
{
    private readonly WeakReference<Buff> _buff = new(buff);

    public override void Execute()
    {
        if (_buff.TryGetTarget(out var effect))
            effect.RunTimer(this);
    }
}
