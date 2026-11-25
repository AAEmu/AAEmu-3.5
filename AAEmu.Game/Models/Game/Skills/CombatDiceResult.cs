using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

internal enum CombatDiceResult : byte
{
    Invalid = 0,
    Hit = 1,
    Critical = 2,
    Miss = 3,
    Dodge = 4,
    Block = 5,
    Parry = 6,
    Resist = 7,
    Immune = 8
}

internal static class CombatDiceResults
{
    internal static CombatDiceResult FromHitType(SkillHitType hitType) => hitType switch
    {
        SkillHitType.MeleeHit or SkillHitType.RangedHit or SkillHitType.SpellHit => CombatDiceResult.Hit,
        SkillHitType.MeleeCritical or SkillHitType.RangedCritical or SkillHitType.SpellCritical => CombatDiceResult.Critical,
        SkillHitType.MeleeMiss or SkillHitType.RangedMiss or SkillHitType.SpellMiss => CombatDiceResult.Miss,
        SkillHitType.MeleeDodge or SkillHitType.RangedDodge => CombatDiceResult.Dodge,
        SkillHitType.MeleeBlock or SkillHitType.RangedBlock => CombatDiceResult.Block,
        SkillHitType.MeleeParry or SkillHitType.RangedParry => CombatDiceResult.Parry,
        SkillHitType.SpellResist => CombatDiceResult.Resist,
        SkillHitType.Immune => CombatDiceResult.Immune,
        _ => CombatDiceResult.Invalid
    };

    internal static bool MatchesMask(SkillHitType hitType, int mask)
    {
        var result = FromHitType(hitType);
        return result != CombatDiceResult.Invalid && (mask & (1 << ((int)result - 1))) != 0;
    }

    internal static bool PreventsHit(SkillHitType hitType) => FromHitType(hitType) >= CombatDiceResult.Miss;

    internal static bool MatchesDamageType(SkillHitType hit, DamageType type) => type switch
    {
        DamageType.Melee => hit is SkillHitType.MeleeHit or SkillHitType.MeleeCritical,
        DamageType.Ranged => hit is SkillHitType.RangedHit or SkillHitType.RangedCritical,
        DamageType.Magic => hit is SkillHitType.SpellHit or SkillHitType.SpellCritical,
        DamageType.Siege => hit == SkillHitType.RangedHit,
        _ => false
    };

    internal static SkillHitType RollCritical(Unit attacker, Unit target, DamageType type)
    {
        var reduction = target.Flexibility / 1000 * 3;
        return type switch
        {
            DamageType.Melee => IsCritical(attacker.MeleeCritical) ? SkillHitType.MeleeCritical : SkillHitType.MeleeHit,
            DamageType.Ranged => IsCritical(attacker.RangedCritical) ? SkillHitType.RangedCritical : SkillHitType.RangedHit,
            DamageType.Magic => IsCritical(attacker.SpellCritical) ? SkillHitType.SpellCritical : SkillHitType.SpellHit,
            DamageType.Siege => SkillHitType.RangedHit,
            _ => SkillHitType.Invalid
        };

        bool IsCritical(float chance) => Random.Shared.NextSingle() * 100f < chance - reduction;
    }
}
