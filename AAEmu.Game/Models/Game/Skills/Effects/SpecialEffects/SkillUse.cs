using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class SkillUse : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.SkillUse;

    internal static bool PassesChanceRoll(int chance, int roll) => chance == 0 || chance >= 100 || roll < chance;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int skillId,
        int delay,
        int chance,
        int value4)
    {
        if (caster is not Unit unit)
            return;

        var roll = chance is > 0 and < 100 ? Random.Shared.Next(100) : 0;
        if (!PassesChanceRoll(chance, roll) || delay < 0 || skillId <= 0 ||
            SkillManager.Instance.GetSkillTemplate((uint)skillId) is not { } template)
        {
            unit.ConditionChance = false;
            return;
        }
        unit.ConditionChance = true;

        var useSkill = new Skill(template);
        targetObj = new SkillCastUnitTarget(target?.ObjId ?? 0);
        // A buff tick can outlive its item and a plot can select a different actor.
        if ((castObj is CastBuff && casterObj is SkillItem) ||
            (castObj is CastPlot && casterObj?.ObjId != unit.ObjId))
            casterObj = new SkillCasterUnit(unit.ObjId);
        // Buff-triggered casts can drive the same buff's ongoing behavior, such as the Yata dance spread.
        if (castObj is not CastBuff)
            caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.UseSkill);
        TaskManager.Instance.Schedule(new UseSkillTask(useSkill, caster, casterObj, target, targetObj, skillObject), TimeSpan.FromMilliseconds(delay));
    }
}
