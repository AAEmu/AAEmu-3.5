using AAEmu.Game.Models.Game.Units;
using UnitBuffs = AAEmu.Game.Models.Game.Units.Buffs;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ExplodeBuff : SpecialEffectAction
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
        if (caster is not Unit attacker || target is not Unit { Hp: > 0, Buffs: UnitBuffs buffs } victim ||
            ReferenceEquals(attacker, victim) || value1 <= 0)
            return;

        if (skill?.SkillMissed(victim.ObjId) == true || !attacker.CanAttack(victim) || !buffs.TryConsumeBeneficialBuff())
        {
            attacker.ConditionChance = false;
            return;
        }
        attacker.ConditionChance = true;

        // The NPC tooltip rows use the same spread on both sides of the base damage.
        var lower = Math.Max(0L, 100L - Math.Max(0, value2));
        var upper = 100L + Math.Max(0, value3);
        var damage = new DamageEffect
        {
            DamageTypeId = (int)DamageType.Magic,
            DamageType = DamageType.Magic,
            WeaponSlotId = -1,
            UseFixedDamage = true,
            FixedMin = (int)(value1 * lower / 100),
            FixedMax = (int)Math.Min((long)value1 * upper / 100, int.MaxValue)
        };
        var buff = (castObj as CastBuff)?.Buff;
        var source = new EffectSource(skill, buff?.Template) { IsTrigger = buff != null, Amount = 1000 };
        damage.Apply(attacker, casterObj, victim, targetObj, castObj, source, skillObject, time);
    }
}
