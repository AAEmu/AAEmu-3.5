using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class CombatDice : SpecialEffectAction
{
    private const int AlwaysHit = 4;
    private const int MeleeUndefendable = 5;
    private const int RangedUndefendable = 8;

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

        var damageType = value1 switch
        {
            MeleeUndefendable => DamageType.Melee,
            RangedUndefendable => DamageType.Ranged,
            _ => (DamageType)skill.Template.DamageTypeId
        };
        var hit = victim.Buffs.CheckDamageImmune(damageType) ? SkillHitType.Immune : value1 switch
        {
            AlwaysHit when damageType is < DamageType.Melee or > DamageType.Heal => SkillHitType.MeleeHit,
            AlwaysHit when damageType == DamageType.Heal => SkillHitType.SpellHit,
            AlwaysHit => CombatDiceResults.RollCritical(attacker, victim, damageType),
            MeleeUndefendable when Random.Shared.NextSingle() * 100f >= attacker.MeleeAccuracy => SkillHitType.MeleeMiss,
            RangedUndefendable when Random.Shared.NextSingle() * 100f >= attacker.RangedAccuracy => SkillHitType.RangedMiss,
            MeleeUndefendable or RangedUndefendable => CombatDiceResults.RollCritical(attacker, victim, damageType),
            _ => skill.RollCombatDice(attacker, victim)
        };
        plot.SetCombatHit(attacker, victim, hit, prepared: true);
    }
}
