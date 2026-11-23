using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using UnitBuffs = AAEmu.Game.Models.Game.Units.Buffs;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class RedeemBuff : SpecialEffectAction
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
        // Absorb Effect's authored row applies to the source and restores 10–15% of 4,000 MP.
        if (caster is not Character { IsDead: false, Buffs: UnitBuffs buffs } character || !ReferenceEquals(character, target))
            return;

        var amount = value2 > 0 ? EffectResourceMath.RollPercentageOfPool(value1, value2, Math.Max(value2, value3)) : value1;
        if (amount <= 0 || !buffs.TryConsumeBeneficialBuff())
            return;

        lock (buffs.SyncRoot)
        {
            var next = (int)Math.Clamp((long)character.Mp + amount, 0L, Math.Max(0, character.MaxMp));
            if (next <= character.Mp)
                return;

            character.Mp = next;
        }

        character.BroadcastPacket(new SCUnitPointsPacket(character.ObjId, character.Hp, character.Mp, character.HighAbilityRsc), true);
    }
}
