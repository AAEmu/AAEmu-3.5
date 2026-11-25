using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills;

internal enum CombatDiceKind
{
    Melee = 1,
    Ranged = 2,
    Spell = 3,
    AlwaysHit = 4,
    MeleeUndefendable = 5,
    Heal = 6,
    EachEffectRollDice = 7,
    RangedUndefendable = 8
}

internal static class CombatDiceResolver
{
    internal static CombatDiceKind GetKind(int id, DamageType damageType) => id is >= 1 and <= 8
        ? (CombatDiceKind)id : damageType switch
        {
            DamageType.Melee => CombatDiceKind.Melee,
            DamageType.Ranged => CombatDiceKind.Ranged,
            DamageType.Magic => CombatDiceKind.Spell,
            DamageType.Heal => CombatDiceKind.Heal,
            _ => CombatDiceKind.AlwaysHit
        };

    internal static SkillHitType Roll(BaseUnit attacker, Unit target, CombatDiceKind kind, DamageType damageType,
        bool checkDamageImmunity = false)
    {
        if (attacker is not Unit unit)
            return SkillHitType.Invalid;

        damageType = kind switch
        {
            CombatDiceKind.Melee or CombatDiceKind.MeleeUndefendable => DamageType.Melee,
            CombatDiceKind.Ranged or CombatDiceKind.RangedUndefendable => DamageType.Ranged,
            CombatDiceKind.Spell => DamageType.Magic,
            CombatDiceKind.Heal => DamageType.Heal,
            _ => damageType
        };

        if (checkDamageImmunity && target.Buffs.CheckDamageImmune(damageType))
            return SkillHitType.Immune;

        if (damageType is not (DamageType.Melee or DamageType.Ranged or DamageType.Magic))
            return CombatDiceResults.GetHitType(damageType);

        var canDefend = (kind is CombatDiceKind.Melee or CombatDiceKind.Ranged or CombatDiceKind.EachEffectRollDice) &&
                        (damageType is DamageType.Melee or DamageType.Ranged) && MathUtil.IsFront(target, attacker);
        return Resolve(unit, target, kind, damageType, canDefend, Random.Shared.NextSingle());
    }

    internal static SkillHitType Resolve(Unit attacker, Unit target, CombatDiceKind kind, DamageType damageType,
        bool canDefend, float roll)
    {
        if (canDefend)
        {
            // Formula 24 converts Bull's Eye to a share of the attacker's facets.
            var bullsEye = attacker.BullsEye;
            var reduction = bullsEye > 0 ? CombatDiceResults.GetRatingReduction(bullsEye, attacker.Facets, 105f) : 0f;
            if (TakeOutcome(ref roll, target.DodgeRate - reduction))
                return damageType == DamageType.Melee ? SkillHitType.MeleeDodge : SkillHitType.RangedDodge;
            if (TakeOutcome(ref roll, target.BlockRate - reduction))
                return damageType == DamageType.Melee ? SkillHitType.MeleeBlock : SkillHitType.RangedBlock;
            var meleeParryChance = target.MeleeParryRate - reduction;
            if (meleeParryChance > 0f && (damageType == DamageType.Melee ||
                target.Buffs.CheckBuff((uint)BuffConstants.EquipDualwield) &&
                target.Buffs.CheckBuff((uint)BuffConstants.DualwieldProficiency)) && TakeOutcome(ref roll, meleeParryChance))
                return SkillHitType.MeleeParry;
            if (damageType == DamageType.Ranged && TakeOutcome(ref roll, target.RangedParryRate - reduction))
                return SkillHitType.RangedParry;
        }

        if (kind != CombatDiceKind.AlwaysHit)
        {
            var accuracy = damageType switch
            {
                DamageType.Melee => attacker.MeleeAccuracy * attacker.MeleeAntiMissMul,
                DamageType.Ranged => attacker.RangedAccuracy * attacker.RangedAntiMissMul,
                _ => attacker.SpellAccuracy * attacker.SpellAntiMissMul
            };
            if (TakeOutcome(ref roll, 100f - accuracy))
                return damageType switch
                {
                    DamageType.Melee => SkillHitType.MeleeMiss,
                    DamageType.Ranged => SkillHitType.RangedMiss,
                    _ => SkillHitType.SpellResist
                };
        }

        var critical = TakeOutcome(ref roll, CombatDiceResults.GetCriticalChance(attacker, target, damageType));
        return CombatDiceResults.GetHitType(damageType, critical);
    }

    private static bool TakeOutcome(ref float roll, float chancePercent)
    {
        if (!(chancePercent > 0f))
            return false;

        var chance = chancePercent / 100f;
        if (chance >= 1f || roll < chance)
            return true;

        // Keep the same draw, rescaled to the outcomes that are still possible.
        roll = (roll - chance) / (1f - chance);
        return false;
    }
}
