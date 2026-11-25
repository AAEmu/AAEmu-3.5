using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class CombatDice : SpecialEffectAction
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
        if (castObj is not CastPlot { TargetInfo: { } plot } || caster is not Unit attacker ||
            target is not Unit victim || skill?.Template == null)
            return;

        var damageType = (DamageType)skill.Template.DamageTypeId;
        var kind = value1 is >= 1 and <= 8 ? (CombatDiceKind)value1 : CombatDiceResolver.GetKind(skill.Template.CombatDiceId, damageType);
        var hit = CombatDiceResolver.Roll(attacker, victim, kind, damageType, checkDamageImmunity: true);
        // Plots can author a guaranteed hit even when the skill has no damage family.
        if (hit == SkillHitType.Invalid && kind == CombatDiceKind.AlwaysHit)
            hit = SkillHitType.MeleeHit;
        plot.SetCombatHit(attacker, victim, hit, prepared: true);
    }
}
