using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class EffectSource
{
    public Skill Skill { get; set; }
    public BuffTemplate Buff { get; set; }
    public int Amount { get; set; }
    public bool IsTrigger { get; set; }
    internal bool AoeDiminishing { get; init; }
    internal SkillHitType? CombatHit { get; init; }

    internal SkillHitType GetCombatHit(uint targetId) => CombatHit ??
        (Skill?.HitTypes.TryGetValue(targetId, out var hit) == true ? hit : SkillHitType.Invalid);

    public EffectSource()
    {
    }

    public EffectSource(Skill skill)
    {
        Skill = skill;
    }

    public EffectSource(BuffTemplate buff)
    {
        Buff = buff;
    }

    public EffectSource(Skill skill, BuffTemplate buff)
    {
        Skill = skill;
        Buff = buff;
    }
}
