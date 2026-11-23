using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class FakeDeath : SpecialEffectAction
{
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
        if (target is not Unit unit || unit.ParentWorld is not { } world)
            return;

        foreach (var npc in world.GetAllNpcs())
        {
            if (!npc.AggroTable.ContainsKey(unit.ObjId))
                continue;
            npc.ClearAggroOfUnit(unit);
            if (npc.AggroTable.IsEmpty)
                npc.IsInBattle = false;
        }
    }
}
