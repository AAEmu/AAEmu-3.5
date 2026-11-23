using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class Cooldown : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.Cooldown;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int cooldownTime,
        int value2,
        int value3,
        int value4)
    {
        if (cooldownTime <= 0 || caster is not Unit unit || skill is not { Template: { Id: > 0 } template })
            return;

        var modified = unit.ApplySkillModifiers(skill, SkillAttribute.Cooldown, cooldownTime);
        if (!double.IsFinite(modified) || modified < 1)
            return;

        var duration = (uint)Math.Min(modified, uint.MaxValue);
        lock (unit.GcdLock)
        {
            if (skill.Cancelled || skill.ActivePlotState?.CancellationRequested() == true)
                return;

            if (unit is Character player)
            {
                if (!player.IgnoreSkillCooldowns)
                    unit.Cooldowns.AddSkillCooldown(template, duration);
            }
            else
            {
                unit.Cooldowns.AddCooldown(template.Id, duration);
            }
        }
    }
}
