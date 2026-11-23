using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills;

internal sealed class CrowdBuffTask : Task
{
    public override void Execute() => Buffs.RecheckCrowdBuffs();
}
