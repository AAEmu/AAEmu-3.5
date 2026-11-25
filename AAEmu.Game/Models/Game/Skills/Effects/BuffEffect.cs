using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class BuffEffect : EffectTemplate
{
    public int Chance { get; set; }
    public int Stack { get; set; }
    public int AbLevel { get; set; }
    public BuffTemplate Buff { get; set; }
    public override uint BuffId => Buff?.Id ?? 0;
    public override bool OnActionTime => Buff?.Tick > 0;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (Buff == null || target == null)
            return;

        if (target is Unit trg)
        {
            if (castObj is CastPlot { TargetInfo: { } plot })
            {
                if (plot.TryGetCombatHit(caster, trg, out var plotHit) && CombatDiceResults.PreventsHit(plotHit))
                    return;
            }
            else if (source != null && CombatDiceResults.PreventsHit(source.GetCombatHit(trg.ObjId)))
                return;
        }

        if (caster != null)
        {
            var chance = caster.ApplyBuffModifers(Buff, BuffAttribute.Chance, Chance);
            caster.ConditionChance = Random.Shared.NextSingle() * 100f < chance;
            if (!caster.ConditionChance)
                return;
        }

        if (!Buff.CanApplyTo(target, caster, source?.Skill))
            return;

        var abLevel = GetApplicationLevel(caster, source?.Skill);

        // TODO Doesn't let the quest work Id=2488 "A Mother's Tale", 13, "Lilyut Hills", "Nuian Main"
        // Safeguard to prevent accidental flagging
        //if (Buff.Kind == BuffKind.Bad && !caster.CanAttack(target) && caster != target)
        //    return;

        target.Buffs.AddBuff(new Buff(target, caster, casterObj, Buff, source?.Skill, time)
        {
            AbLevel = abLevel,
            Stack = Math.Max(1, Stack)
        });

        // Check if a bad buff was applied to a friendly faction (bloodlust)
        var relationToTarget = caster?.GetRelationStateTo(target) ?? RelationState.Neutral;
        if (Buff.Kind == BuffKind.Bad && target is not Npc && 
            relationToTarget == RelationState.Friendly && caster != target &&
            !target.Buffs.CheckBuff((uint)BuffConstants.Retribution))
        {
            (caster as Unit)?.SetCriminalState(true, target);
        }
    }

    private ushort GetApplicationLevel(BaseUnit caster, Skill skill)
    {
        if (AbLevel > 0)
            return (ushort)Math.Min(AbLevel, ushort.MaxValue);
        if (skill?.Template is not { } template)
            return 1;

        var abilityLevel = template.AbilityLevel;
        var step = template.LevelStep;
        if (step > 0 && (caster is Character || caster is Npc && Buff.LevelDuration != 0))
            abilityLevel = Math.Max(abilityLevel, ((Unit)caster).GetAbLevel(template.AbilityId) / step * step);

        var addedRanks = skill.GetEffectLevel(caster) - Math.Max(1, (int)skill.Level);
        return (ushort)Math.Clamp(abilityLevel + (long)addedRanks * step, 0, ushort.MaxValue);
    }
}
