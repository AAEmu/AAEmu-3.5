using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items.Procs;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class DamageEffect : EffectTemplate
{
    // update to 3.5.0.3
    public float ActabilityAdd { get; set; }
    public uint ActabilityGroupId { get; set; }
    public float ActabilityMul { get; set; }
    public int ActabilityStep { get; set; }
    public bool AdjustDamageByHeight { get; set; }
    public float AggroMultiplier { get; set; }
    public uint ChargedBuffId { get; set; }
    public float ChargedLevelMul { get; set; }
    public float ChargedMul { get; set; }
    public bool CheckCrime { get; set; } // TODO: no DB column, stale
    public int CriticalBonus { get; set; }
    public int DamageTypeId { get; set; }
    public DamageType DamageType { get; set; }
    public float DpsIncMultiplier { get; set; }
    public float DpsMultiplier { get; set; }
    public bool EngageCombat { get; set; } = true;
    public string ExtraEffects { get; set; }
    public bool FireProc { get; set; } = true;
    public int FixedMax { get; set; }
    public int FixedMin { get; set; }
    public int HealthStealRatio { get; set; }
    public float HighAbilityResourceDpsMd { get; set; }
    public float HighAbilityResourceLevelMd { get; set; }
    public float HighAbilityResourceMd { get; set; }
    public uint HitAnimTimingId { get; set; }
    public float LevelMd { get; set; }
    public int LevelVaEnd { get; set; }
    public int LevelVaStart { get; set; }
    public bool ManaDamage { get; set; }
    public int ManaStealRatio { get; set; }
    public float Multiplier { get; set; }
    public int PercentMax { get; set; }
    public int PercentMin { get; set; }
    public bool Synergy { get; set; }
    public int TargetBuffBonus { get; set; }
    public float TargetBuffBonusMul { get; set; }
    public uint TargetBuffTagId { get; set; }
    public uint TargetChargedBuffId { get; set; }
    public float TargetChargedMul { get; set; }
    public int TargetHealthAdd { get; set; }
    public int TargetHealthMax { get; set; }
    public int TargetHealthMin { get; set; }
    public float TargetHealthMul { get; set; }
    public bool UseChargedBuff { get; set; }
    public bool UseCurrentHealth { get; set; }
    public bool UseFixedDamage { get; set; }
    public bool UseHighAbilityResource { get; set; }
    public bool UseLevelDamage { get; set; }
    public bool UseMainhandWeapon { get; set; }
    public bool UseOffhandWeapon { get; set; }
    public bool UsePercentDamage { get; set; }
    public bool UseRangedWeapon { get; set; }
    public bool UseTargetChargedBuff { get; set; }
    public int WeaponSlotId { get; set; }
    public List<BonusTemplate> Bonuses { get; set; } = [];

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Trace("DamageEffect");

        var trg = target as Unit;
        if (trg == null || trg.Hp <= 0)
        {
            return;
        }

        var fromBuffTrigger = source?.IsTrigger == true;

        trg.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.AttackedEtc);
        caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.AttackEtc);
        if (fromBuffTrigger)
        {
            trg.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.AttackedBuffTrigger);
            caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.AttackBuffTrigger);
        }

        if (target.Buffs.CheckDamageImmune(DamageType))
        {
            target.BroadcastPacket(new SCUnitDamagedPacket(castObj, casterObj, caster.ObjId, target.ObjId, 1, 0)
            {
                HitType = SkillHitType.Immune
            }, false);
            return;
        }

        var weapon = ((Unit)caster).Equipment.GetItemBySlot(WeaponSlotId);
        var holdable = (WeaponTemplate)weapon?.Template;

        var hitType = SkillHitType.Invalid;
        if ((source?.Skill?.HitTypes.TryGetValue(trg.ObjId, out hitType) ?? false)
            && (source?.Skill.SkillMissed(trg.ObjId) ?? false))
        {
            var missPacket = new SCUnitDamagedPacket(castObj, casterObj, caster.ObjId, target.ObjId, 0, 0)
            {
                HoldableId = (byte)(holdable?.HoldableTemplate?.Id ?? 0),
                HitType = hitType
            };
            caster.CombatBuffs.TriggerCombatBuffs(caster, trg, hitType, false, source.Skill);
            if (!ReferenceEquals(caster, trg))
                trg.CombatBuffs.TriggerCombatBuffs(caster, trg, hitType, false, source.Skill);
            caster.BroadcastPacket(missPacket, true);
            return;
        }

        // Test-drive restriction damage is authored on a friendly vehicle.
        if (castObj is not CastBuff { Buff.Template.Id: 6151 } && !caster.CanAttack(trg))
            return;

        if (Bonuses != null)
        {
            foreach (var bonus in Bonuses)
                caster.AddBonus(uint.MaxValue, new Bonus { Template = bonus, Value = bonus.Value });
        }

        float flexibilityRateMod = trg.Flexibility / 1000 * 3;
        switch (DamageType)
        {
            case DamageType.Melee:
                if (Random.Shared.Next(0f, 100f) < ((Unit)caster).MeleeCritical - flexibilityRateMod)
                    hitType = SkillHitType.MeleeCritical;
                else
                    hitType = SkillHitType.MeleeHit;
                break;
            case DamageType.Magic:
                if (Random.Shared.Next(0f, 100f) < ((Unit)caster).SpellCritical - flexibilityRateMod)
                    hitType = SkillHitType.SpellCritical;
                else
                    hitType = SkillHitType.SpellHit;
                break;
            case DamageType.Ranged:
                if (Random.Shared.Next(0f, 100f) < ((Unit)caster).RangedCritical - flexibilityRateMod)
                    hitType = SkillHitType.RangedCritical;
                else
                    hitType = SkillHitType.RangedHit;
                break;
            case DamageType.Siege:
                hitType = SkillHitType.RangedHit;//No siege type?
                break;
            default:
                hitType = SkillHitType.Invalid;
                break;
        }

        var min = 0.0f;
        var max = 0.0f;

        // Used for NPCs, I think
        var levelMin = 0.0f;
        var levelMax = 0.0f;
        if (UseLevelDamage)
        {
            var lvlMd = ((Unit)caster).LevelDps * LevelMd;
            // Hack null-check on skill
            var levelModifier = (((source.Skill?.Level ?? 1) - 1) / 49 * (LevelVaEnd - LevelVaStart) + LevelVaStart) * 0.01f;

            levelMin += lvlMd - levelModifier * lvlMd + 0.5f;
            levelMax += (levelModifier + 1) * lvlMd + 0.5f;
        }

        // Stats/Weapon DPS
        var dpsInc = 0;
        switch (DamageType)
        {
            case DamageType.Melee:
                dpsInc = ((Unit)caster).DpsInc;
                break;
            case DamageType.Magic:
                dpsInc = ((Unit)caster).MDps + ((Unit)caster).MDpsInc;
                break;
            case DamageType.Ranged:
                dpsInc = ((Unit)caster).RangedDpsInc;
                break;
        }

        max += dpsInc * 0.001f * DpsIncMultiplier;
        var weaponDamage = 0.0f;

        if (UseMainhandWeapon)
            weaponDamage = ((Unit)caster).Dps * 0.001f; // TODO : Use only weapon value!
        if (UseOffhandWeapon)
            weaponDamage = ((Unit)caster).OffhandDps * 0.001f + weaponDamage;
        if (UseRangedWeapon)
            weaponDamage = ((Unit)caster).RangedDps * 0.001f + weaponDamage; // TODO : Use only weapon value!

        max = DpsMultiplier * weaponDamage + max;

        var minCastBonus = 1000f;
        // Hack null-check on skill
        var castTimeMod = source.Skill?.Template.CastingTime ?? 0; // This mod depends on casting_inc too!
        if (castTimeMod <= 1000)
            minCastBonus = min > 0 ? min : minCastBonus;
        else
            minCastBonus = castTimeMod;

        var variableDamage = max * minCastBonus * 0.001f;
        // TODO : Handle NPC
        if (WeaponSlotId < 0)
        {
            min = variableDamage + levelMin;
            max = variableDamage + levelMax;
        }
        else
        {
            if (weapon != null)
            {
                var scaledDamage = holdable.HoldableTemplate.DamageScale * variableDamage * 0.01f;
                min = levelMin + (variableDamage - scaledDamage);
                max = levelMax + (variableDamage + scaledDamage);
            }
        }

        min *= Multiplier;
        max *= Multiplier;

        if (AdjustDamageByHeight && caster.Transform?.World is { } casterTransform &&
            trg.Transform?.World is { } targetTransform)
        {
            var advantage = casterTransform.Position.Z - targetTransform.Position.Z;
            if (advantage >= 1f && FormulaManager.Instance.GetFormula((uint)FormulaKind.DamageMultiplierByHeight) is { } heightFormula)
            {
                var factor = heightFormula.Evaluate(new Dictionary<string, double>(1) { ["range"] = advantage });
                if (double.IsFinite(factor) && factor > 0)
                {
                    min *= (float)factor;
                    max *= (float)factor;
                }
            }
        }

        var damageMultiplier = DamageType switch
        {
            DamageType.Melee => ((Unit)caster).MeleeDamageMul,
            DamageType.Magic => ((Unit)caster).SpellDamageMul,
            DamageType.Ranged => ((Unit)caster).RangedDamageMul,
            DamageType.Siege => 1.0f, // TODO
            _ => 1f
        };

        min = MathF.Floor(min * damageMultiplier);
        max = MathF.Ceiling(max * damageMultiplier);

        if (source.Skill != null)
        {
            min = (float)caster.SkillModifiersCache.ApplyModifiers(source.Skill, SkillAttribute.Damage, min);
            max = (float)caster.SkillModifiersCache.ApplyModifiers(source.Skill, SkillAttribute.Damage, max);
        }

        if (source.Buff?.TickEffects.Count > 0)
        {
            if (source.Buff.Duration != 0)
            {
                min = (float)(min * (source.Buff.Tick / source.Buff.Duration));
                max = (float)(max * (source.Buff.Tick / source.Buff.Duration));
            }

            caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.DamageEtcDot);
            trg.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.DamagedEtcDot);

            if (DamageType == DamageType.Magic)
            {
                caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.DamageSpellDot);
                trg.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.DamagedSpellDot);
            }
        }

        if (UseChargedBuff && source.Skill != null)
        {
            var effect = caster.Buffs.GetEffectFromBuffId(ChargedBuffId);
            var charges = effect?.Charge ?? 0;

            min += charges * (ChargedMul + source.Skill.Level * ChargedLevelMul);
            max += charges * (ChargedMul + source.Skill.Level * ChargedLevelMul);
            effect?.Exit();
        }

        Buff targetChargedEffect = null;
        if (UseTargetChargedBuff && TargetChargedBuffId != 0)
        {
            targetChargedEffect = trg.Buffs.GetEffectFromBuffId(TargetChargedBuffId);
            var chargeDamage = (targetChargedEffect?.Charge ?? 0) * TargetChargedMul;
            min += chargeDamage;
            max += chargeDamage;
        }

        if (UseFixedDamage)
        {
            min = FixedMin;
            max = FixedMax;
        }

        if (UsePercentDamage)
        {
            // In the 3.5 schema this flag chooses the victim's current or maximum HP.
            var health = UseCurrentHealth ? trg.Hp : trg.MaxHp;
            var percentDamage = Random.Shared.Next((float)PercentMin, PercentMax) * 0.01f * health;
            min += percentDamage;
            max += percentDamage;
        }

        var finalDamage = Random.Shared.Next(min, max);
        if (TargetHealthMax > 0)
        {
            var health = trg.Hpp;
            if (health >= TargetHealthMin && health <= TargetHealthMax)
                finalDamage = finalDamage * TargetHealthMul + TargetHealthAdd;
        }
        if (finalDamage > 0 && source?.Buff?.TickEffects.Count is not > 0 &&
            source?.Skill?.Template is { } skill &&
            (source.AoeDiminishing || (skill.Plot?.EventTemplate?.AoeDiminishing == true &&
                                      (skill.TargetAreaRadius > 0 || skill.TargetAreaCount > 1))))
            finalDamage *= AoeDiminishing.Advance(trg, skill.Id, time, PlotManager.Instance.AoeDiminishingRates);

        // Buff tag increase (Hellspear's impale combo, for ex)
        if (TargetBuffTagId > 0 && target.Buffs.CheckBuffTag(TargetBuffTagId))
        {
            finalDamage = finalDamage * TargetBuffBonusMul + TargetBuffBonus;
        }

        // Toughness reduction (PVP Only)
        if (caster is Character && trg is Character)
            finalDamage *= 1 - trg.BattleResist / (8000f + trg.BattleResist);

        // Do Critical Dmgs
        switch (hitType)
        {
            case SkillHitType.MeleeCritical:
                finalDamage *= 1 + (((Unit)caster).MeleeCriticalBonus - trg.Flexibility / 100) / 100;
                break;
            case SkillHitType.RangedCritical:
                finalDamage *= 1 + (((Unit)caster).RangedCriticalBonus - trg.Flexibility / 100) / 100;
                break;
            case SkillHitType.SpellCritical:
                finalDamage *= 1 + (((Unit)caster).SpellCriticalBonus - trg.Flexibility / 100) / 100;
                break;
            default:
                break;
        }

        // Reduction
        var reductionMul = 1.0f;

        if (target is Unit targetUnit)
        {
            float armor;
            switch (DamageType)
            {
                case DamageType.Melee:
                    armor = Math.Max(0f, targetUnit.Armor - ((Unit)caster).DefensePenetration);
                    reductionMul = 1.0f - armor / (armor + 5300.0f);
                    finalDamage = finalDamage * targetUnit.IncomingMeleeDamageMul;
                    break;
                case DamageType.Ranged:
                    armor = Math.Max(0f, targetUnit.Armor - ((Unit)caster).DefensePenetration);
                    reductionMul = 1.0f - armor / (armor + 5300.0f);
                    finalDamage = finalDamage * targetUnit.IncomingRangedDamageMul;
                    break;
                case DamageType.Magic:
                    armor = Math.Max(0f, targetUnit.MagicResistance - ((Unit)caster).MagicPenetration);
                    reductionMul = 1.0f - armor / (armor + 5300.0f);
                    finalDamage = finalDamage * targetUnit.IncomingSpellDamageMul;
                    break;
                default:
                    finalDamage = finalDamage * targetUnit.IncomingDamageMul;
                    break;
            }
        }
        var value = (int)(finalDamage * reductionMul);
        if (caster is Character crafter && ActabilityGroupId > 0 &&
            crafter.Actability?.Actabilities.TryGetValue(ActabilityGroupId, out var actability) == true)
            value = EffectResourceMath.ApplyActability(value, actability.Point, ActabilityStep, ActabilityAdd, ActabilityMul);
        var absorbed = (int)(finalDamage * (1.0f - reductionMul));
        var healthStolen = (int)(value * (HealthStealRatio / 100.0f));
        var manaStolen = (int)(value * (ManaStealRatio / 100.0f));

        targetChargedEffect?.Exit();

        var manaDamage = 0;
        if (ManaDamage)
        {
            var previousMana = trg.Mp;
            trg.ReduceCurrentMp(caster, value);
            manaDamage = Math.Max(0, previousMana - trg.Mp);
        }
        else
        {
            trg.ReduceCurrentHp(caster, value, damageType: DamageType);
        }
        ((Unit)caster).SummarizeDamage += value;

        if (healthStolen > 0 || manaStolen > 0)
        {
            ((Unit)caster).Hp = Math.Min(((Unit)caster).MaxHp, ((Unit)caster).Hp + healthStolen);
            ((Unit)caster).Mp = Math.Min(((Unit)caster).MaxMp, ((Unit)caster).Mp + manaStolen);
            caster.BroadcastPacket(new SCUnitPointsPacket(caster.ObjId, ((Unit)caster).Hp, ((Unit)caster).Mp, ((Unit)caster).HighAbilityRsc), true);
        }

        if (Bonuses != null)
        {
            ((Unit)caster).ClearBonuses(uint.MaxValue);
        }

        var trgCharacter = trg as Character;
        var attacker = caster as Unit;

        if (CheckCrime && caster.GetRelationStateTo(trg) == RelationState.Friendly)
        {
            // Set Purple state
            if (!trg.Buffs.CheckBuff((uint)BuffConstants.Retribution))
            {
                ((Unit)caster).SetCriminalState(true, trg);
            }

            // Mark the owner of this unit as being assaulted
            var targetOwner = trg.GetOwnerCharacter();
            var sourceOwner = caster.GetOwnerCharacter();
            if (targetOwner != null && sourceOwner != null)
            {
                // If both players haven't interacted with each other yet, then generate evidence for the initiator 
                if ((!sourceOwner.AssaultedBy.Contains(targetOwner.Id)) && (!targetOwner.AssaultedBy.Contains(sourceOwner.Id)))
                {
                    // Update assault list
                    targetOwner.AssaultedBy.Add(sourceOwner.Id);
                    sourceOwner.AssaultOn.Add(targetOwner.Id);
                    // Generate evidence
                    _ = CrimeManager.Instance.GenerateEvidenceFromDamage(caster, trg);
                }
            }
        }

        // set for all combatants, for RegenTick
        if (EngageCombat)
        {
            trg.IsInBattle = trg.Hp > 0;
            trg.LastCombatActivity = DateTime.UtcNow;
        }

        if (trgCharacter != null)
        {
            //trgCharacter.IsInBattle |= trg.Hp > 0;
            //trgCharacter.LastCombatActivity = DateTime.UtcNow;
            if (attacker is Character attackerCharacter)
            {
                trgCharacter.SetHostileActivity(attackerCharacter);
            }
        }

        if (attacker != null)
        {
            if (EngageCombat)
            {
                attacker.IsInBattle |= trg.Hp > 0;
                attacker.LastCombatActivity = DateTime.UtcNow;
            }
        }

        if (FireProc && value > 0 && skillObject?.IsItemProc != true && source?.Skill?.IsItemProc != true)
        {
            var triggeringSkill = source?.Skill;
            attacker?.Procs?.RollDamageProcs(trg, triggeringSkill, DamageType, hitType, received: false);
            if (!trg.IsDead)
                trg.Procs?.RollDamageProcs(attacker, triggeringSkill, DamageType, hitType, received: true);
        }

        caster.CombatBuffs.TriggerCombatBuffs(caster, trg, hitType, false, source.Skill);
        if (!ReferenceEquals(caster, trg))
            trg.CombatBuffs.TriggerCombatBuffs(caster, trg, hitType, false, source.Skill);
        var packet = new SCUnitDamagedPacket(castObj, casterObj, caster.ObjId, target.ObjId,
            ManaDamage ? 0 : value, ManaDamage ? 0 : absorbed)
        {
            _manaBurn = manaDamage,
            HoldableId = (byte)(holdable?.HoldableTemplate?.Id ?? 0),
            HitType = hitType
        };

        if (packetBuilder != null)
            packetBuilder.AddPacket(packet);
        else
            trg.BroadcastPacket(packet, true);

        if (trg is Npc npc)
        {
            trg.SendPacketToPlayers([trg, caster], new SCAiAggroPacket(trg.ObjId, 1, caster.ObjId, ((Unit)caster).SummarizeDamage, 0, 0));
            npc.OnDamageReceived((Unit)caster, value);
        }

        //Invoke even if damage is 0
        ((Unit)caster).Events.OnAttack(this, new OnAttackArgs
        {
            Attacker = (Unit)caster,
            Receiver = trg
        });
        trg.Events.OnAttacked(this, new OnAttackedArgs { Attacker = caster, Receiver = trg });

        if (value > 0)
        {
            var sourceUnit = (Unit)caster;
            sourceUnit.Events.OnDamage(this, new OnDamageArgs
            {
                Attacker = (Unit)caster,
                Receiver = trg,
                Amount = value
            });
            caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.DamageEtc);
            if (fromBuffTrigger)
                caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.DamageBuffTrigger);
            trg.Events.OnDamaged(this, new OnDamagedArgs
            {
                Attacker = (Unit)caster,
                Receiver = trg,
                Amount = value
            });

            var dealt = new OnDamageArgs { Attacker = sourceUnit, Receiver = trg, Amount = value };
            var received = new OnDamagedArgs { Attacker = sourceUnit, Receiver = trg, Amount = value };
            switch (DamageType)
            {
                case DamageType.Melee:
                    sourceUnit.Events.OnDamageMelee(this, dealt);
                    trg.Events.OnDamagedMelee(this, received);
                    break;
                case DamageType.Ranged:
                    sourceUnit.Events.OnDamageRanged(this, dealt);
                    trg.Events.OnDamagedRanged(this, received);
                    break;
                case DamageType.Magic:
                    sourceUnit.Events.OnDamageSpell(this, dealt);
                    trg.Events.OnDamagedSpell(this, received);
                    break;
                case DamageType.Siege:
                    sourceUnit.Events.OnDamageSiege(this, dealt);
                    trg.Events.OnDamagedSiege(this, received);
                    break;
            }

            trg.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.DamagedEtc);
            if (fromBuffTrigger)
                trg.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.DamagedBuffTrigger);
        }
    }
}
