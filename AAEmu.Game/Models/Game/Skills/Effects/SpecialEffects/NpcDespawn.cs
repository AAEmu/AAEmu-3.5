using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class NpcDespawn : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.NpcDespawn;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is Npc npc)
            npc.ParentWorld?.SpawnManager?.TryQueueEffectDespawn(npc, scheduleRespawn: false);
    }
}
