using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Procs;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class HealEffect : EffectTemplate
{
    public bool UseFixedHeal { get; set; }
    public int FixedMin { get; set; }
    public int FixedMax { get; set; }
    public bool UseLevelHeal { get; set; }
    public float LevelMd { get; set; }
    public int LevelVaStart { get; set; }
    public int LevelVaEnd { get; set; }
    public bool Percent { get; set; }
    public bool UseChargedBuff { get; set; }
    public uint ChargedBuffId { get; set; }
    public float ChargedMul { get; set; }
    public bool SlaveApplicable { get; set; }
    public bool IgnoreHealAggro { get; set; }
    public float DpsMultiplier { get; set; }
    public uint ActabilityGroupId { get; set; }
    public int ActabilityStep { get; set; }
    public float ActabilityMul { get; set; }
    public float ActabilityAdd { get; set; }
    public string ExtraEffects { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Trace("HealEffect {0}", Id);

        if (caster is not Unit healer || target is not Unit { Hp: > 0 } trg)
            return;

        var min = 0.0f;
        var max = 0.0f;

        var levelMin = 0.0f;
        var levelMax = 0.0f;

        if (UseLevelHeal)
        {
            var lvlMd = healer.LevelDps * LevelMd;
            var levelModifier = (((source?.Skill?.Level ?? 1) - 1) / 49 * (LevelVaEnd - LevelVaStart) + LevelVaStart) * 0.01f;

            levelMin += lvlMd - levelModifier * lvlMd + 0.5f;
            levelMax += (levelModifier + 1) * lvlMd + 0.5f;
        }

        max += healer.HDps * 0.001f * DpsMultiplier;

        var minCastBonus = 1000f;
        // Hack null-check on skill
        var castTimeMod = source?.Skill?.Template.CastingTime ?? 0; // This mod depends on casting_inc too!
        if (castTimeMod <= 1000)
            minCastBonus = min > 0 ? min : minCastBonus;
        else
            minCastBonus = castTimeMod;

        var variableDamage = max * minCastBonus * 0.001f;
        min = variableDamage + levelMin;
        max = variableDamage + levelMax;

        if (!UseFixedHeal && source?.Skill is { } skill)
        {
            min = (float)healer.ApplySkillModifiers(skill, SkillAttribute.Heal, min);
            max = (float)healer.ApplySkillModifiers(skill, SkillAttribute.Heal, max);
        }

        var tickModifier = 1.0f;
        if (source?.Buff?.TickEffects.Count > 0 && source.Buff.Duration != 0)
        {
            tickModifier = (float)(source.Buff.Tick / source.Buff.Duration);
        }

        min *= tickModifier;
        max *= tickModifier;

        if (UseChargedBuff)
        {
            var effect = caster.Buffs.GetEffectFromBuffId(ChargedBuffId);
            if (effect != null)
            {
                min += ChargedMul * effect.Charge;
                max += ChargedMul * effect.Charge;
                effect.Exit();
            }
        }

        var criticalHeal = Random.Shared.Next(0f, 100f) < healer.HealCritical;

        var percentageHeal = Percent && UseFixedHeal;
        var value = percentageHeal
            ? EffectResourceMath.RollPercentageOfPool(trg.MaxHp, FixedMin, FixedMax)
            : (int)Random.Shared.Next(min, max);

        if (criticalHeal)
        {
            value = (int)(value * (1 + healer.HealCriticalBonus / 100));
            healer.CombatBuffs.TriggerCombatBuffs(healer, trg, SkillHitType.SpellCritical, true, source?.Skill);
            if (!ReferenceEquals(healer, trg))
                trg.CombatBuffs.TriggerCombatBuffs(healer, trg, SkillHitType.SpellCritical, true, source?.Skill);
        }

        value = (int)(value * trg.IncomingHealMul);

        if (UseFixedHeal && !percentageHeal)
        {
            value = Random.Shared.Next(FixedMin, FixedMax);
            if (source?.Buff != null && source.IsTrigger)
            {
                value = (int)(value / 1000.0f * source.Amount);
            }
            else
                value = (int)(value * tickModifier);
        }

        value = (int)(value * healer.HealMul);

        // Check if Healing is based on proficiency
        if (caster is Character player && ActabilityGroupId > 0 &&
            player.Actability?.Actabilities.TryGetValue(ActabilityGroupId, out var actability) == true)
            value = EffectResourceMath.ApplyActability(value, actability.Point, ActabilityStep, ActabilityAdd, ActabilityMul);

        var healHitType = criticalHeal ? HealHitType.CriticalHealHit : HealHitType.HealHit;

        var packet = new SCUnitHealedPacket(castObj, casterObj, target.ObjId, HealType.Health, healHitType, value);
        if (packetBuilder != null)
            packetBuilder.AddPacket(packet);
        else
            trg.BroadcastPacket(packet, true);

        var oldHp = trg.Hp;
        trg.Hp = (int)Math.Min((long)trg.Hp + value, trg.MaxHp);
        trg.BroadcastPacket(new SCUnitPointsPacket(trg.ObjId, trg.Hp, trg.Mp, trg.HighAbilityRsc), true);

        // PvP assist tracking: a healer on the target's side earns assist credit
        // for the target's next PvP kill (within 30s window).
        if (caster is Character healerChar && trg is Character targetChar &&
            healerChar.Id != targetChar.Id && targetChar.IsInBattle)
        {
            targetChar.RecordPvpHealFrom(healerChar);
        }

        trg.Events.OnHealed(this, new OnHealedArgs
        {
            Healer = healer,
            HealAmount = value,
            IgnoreHealAggro = IgnoreHealAggro
        });
        trg.PostUpdateCurrentHp(trg, oldHp, trg.Hp, KillReason.Unknown);

        if (value > 0 && skillObject?.IsItemProc != true && source?.Skill?.IsItemProc != true)
        {
            var triggeringSkill = source?.Skill;
            healer.Procs?.RollProcsForKind(ProcChanceKind.HitHeal, trg, triggeringSkill);
            if (criticalHeal)
                healer.Procs?.RollProcsForKind(ProcChanceKind.HitHealCrit, trg, triggeringSkill);
        }
    }
}
