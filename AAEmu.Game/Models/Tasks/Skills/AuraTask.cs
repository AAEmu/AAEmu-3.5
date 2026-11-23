using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Tasks.Skills;

public sealed class AuraTask(Buff buff) : Task
{
    private readonly WeakReference<Buff> _buff = new(buff);

    public override void Execute()
    {
        if (_buff.TryGetTarget(out var effect))
            effect.RunAuraPulse(this);
    }
}
