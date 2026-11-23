using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class BuffSteal : SpecialEffectAction
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
        if (caster is not Unit stealer || target is not Unit victim || ReferenceEquals(stealer, victim) ||
            !stealer.CanAttack(victim))
            return;

        // In 3.5 the three Leech rows put the number of beneficial effects in value1.
        var reverse = value1 == 1 && value2 == 1;
        var count = value3 > 0 ? value3 : value1;
        var donor = reverse ? stealer : victim;
        var recipient = reverse ? victim : stealer;
        var tagId = (uint)Math.Max(0, value4);
        List<Buff> held = [];
        if (reverse)
            donor.Buffs.GetAllBuffs([], held, [], includeAllPassives: false);
        else
            donor.Buffs.GetAllBuffs(held, [], [], includeAllPassives: false);

        held.Sort(static (left, right) => left.Index.CompareTo(right.Index));
        foreach (var buff in held)
        {
            if (count <= 0)
                break;
            if (buff.Passive || buff.Template.System ||
                (tagId != 0 && !SkillManager.Instance.GetBuffTags(buff.Template.Id).Contains(tagId)) ||
                recipient.Buffs.CheckBuff(buff.Template.Id) || !buff.Template.CanApplyTo(recipient, stealer, skill))
                continue;

            var taken = buff.TakeForSteal(recipient, stealer, skill, out var duration);
            if (taken == null)
                continue;

            buff.CompleteDispel();
            recipient.Buffs.AddBuff(taken, forcedDuration: duration);
            count--;
        }
    }
}
