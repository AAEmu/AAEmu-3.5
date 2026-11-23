using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

[NotInParallel]
public class PortedSkillEffectTests
{
    private static readonly FieldInfo s_skillManagerInstance = typeof(Singleton<SkillManager>)
        .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
    private SkillManager _previousSkillManager;

    [Before(Test)]
    public void Setup()
    {
        var manager = new SkillManager(null, null);
        typeof(SkillManager).GetField("_taggedBuffs", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, new Dictionary<uint, List<uint>>());
        typeof(SkillManager).GetField("_skillTags", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, new Dictionary<uint, List<uint>>());
        _previousSkillManager = (SkillManager)s_skillManagerInstance.GetValue(null);
        s_skillManagerInstance.SetValue(null, manager);
    }

    [After(Test)]
    public void RestoreSkillManager() => s_skillManagerInstance.SetValue(null, _previousSkillManager);

    [Test]
    public async Task LevelUpEffectPromotesTheCharacterAndOnlyEquippedAbilities()
    {
        var singleton = typeof(Singleton<ExperienceManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = singleton.GetValue(null);
        var experience = new ExperienceManager();
        var loader = Mock.Of<IExperienceLevelTemplateLoader>();
        loader.Load().Returns(new[]
        {
            new ExperienceLevelTemplate { Level = 1, TotalExp = 0, TotalMateExp = 0 },
            new ExperienceLevelTemplate { Level = 2, TotalExp = 100, TotalMateExp = 100 },
            new ExperienceLevelTemplate { Level = 3, TotalExp = 250, TotalMateExp = 250 },
            new ExperienceLevelTemplate { Level = 4, TotalExp = 500, TotalMateExp = 500 }
        });
        experience.Load(loader.Object, 4, 4);
        singleton.SetValue(null, experience);
        try
        {
            var character = new IsolatedCharacter
            {
                Level = 1, Ability1 = AbilityType.Fight, Ability2 = AbilityType.Wild, Ability3 = AbilityType.None
            };
            character.Abilities = new CharacterAbilities(character);
            character.Abilities.Abilities[AbilityType.Wild].Exp = 20;
            character.Abilities.Abilities[AbilityType.Magic].Exp = 70;

            Apply(new LevelUpEffect { Level = 3 }, character, character);
            Apply(new LevelUpEffect { Level = 2 }, character, character);

            await Assert.That(character.Level).IsEqualTo((byte)3);
            await Assert.That(character.Experience).IsEqualTo(250);
            await Assert.That(character.Abilities.Abilities[AbilityType.Fight].Exp).IsEqualTo(250);
            await Assert.That(character.Abilities.Abilities[AbilityType.Wild].Exp).IsEqualTo(250);
            await Assert.That(character.Abilities.Abilities[AbilityType.Magic].Exp).IsEqualTo(70);
        }
        finally
        {
            singleton.SetValue(null, previous);
        }
    }

    [Test]
    public async Task HealEffect_UsesTargetsMaximumHealthAndIncomingHealModifier()
    {
        var caster = new Unit();
        var target = new IsolatedUnit { Hp = 100, MaxHp = 2000, IncomingHealMul = 0.5f };
        var effect = new HealEffect
        {
            Percent = true, UseFixedHeal = true, FixedMin = 30, FixedMax = 30
        };

        Apply(effect, caster, target);

        await Assert.That(target.Hp).IsEqualTo(400);
    }

    [Test]
    public async Task HealEffect_UsesCasterSkillModifierForComposedHeal()
    {
        var caster = new IsolatedUnit { HDps = 100_000 };
        var target = new IsolatedUnit { Hp = 100, MaxHp = 1000 };
        var skill = new Skill(new SkillTemplate { Id = 97 });
        caster.SkillModifiersCache.AddModifier(new SkillModifier
        {
            SkillId = skill.Id,
            SkillAttribute = SkillAttribute.Heal,
            UnitModifierType = UnitModifierType.Value,
            Value = 25
        });

        Apply(new HealEffect { DpsMultiplier = 1 }, caster, target, new EffectSource(skill));

        await Assert.That(target.Hp).IsEqualTo(225);
    }

    [Test]
    [Arguments(false, true)]
    [Arguments(true, false)]
    public async Task HealEffect_IgnoreHealAggroControlsThreat(bool ignoreHealAggro, bool expectThreat)
    {
        var caster = new IsolatedUnit { ObjId = 1 };
        var target = new IsolatedUnit { ObjId = 2, Hp = 100, MaxHp = 1000 };
        var hostile = new IsolatedUnit { ObjId = 3 };
        hostile.AddUnitAggro(AggroKind.Damage, target, 100);

        Apply(new HealEffect { UseFixedHeal = true, FixedMin = 25, FixedMax = 25, IgnoreHealAggro = ignoreHealAggro },
            caster, target);

        await Assert.That(target.Hp).IsEqualTo(125);
        await Assert.That(hostile.AggroTable.ContainsKey(caster.ObjId)).IsEqualTo(expectThreat);
    }

    [Test]
    [Arguments(20, 150, 350)]
    [Arguments(20, 950, 1000)]
    [Arguments(-50, 900, 400)]
    [Arguments(-60, 200, 0)]
    public async Task RestoreManaEffect_AppliesSignedPercentageWithinManaPool(int percent, int start, int expected)
    {
        var target = new IsolatedUnit { Hp = 100, MaxMp = 1000, Mp = start };
        var effect = new RestoreManaEffect
        {
            Percent = true, UseFixedValue = true, FixedMin = percent, FixedMax = percent
        };

        Apply(effect, new Unit(), target);

        await Assert.That(target.Mp).IsEqualTo(expected);
    }

    [Test]
    [Arguments(10000, 500, 800, 300)]
    [Arguments(500, 500, 990, 300)]
    [Arguments(10000, 50, 950, 0)]
    public async Task ManaBurnEffect_ConvertsOnlyBurnedManaToHealthDamage(
        int ratio, int mana, int expectedHealth, int expectedMana)
    {
        var target = new IsolatedUnit { Hp = 1000, Mp = mana };
        var effect = new ManaBurnEffect { BaseMin = 200, BaseMax = 200, DamageRatio = ratio };

        Apply(effect, new Unit(), target);

        await Assert.That(target.Hp).IsEqualTo(expectedHealth);
        await Assert.That(target.Mp).IsEqualTo(expectedMana);
    }

    [Test]
    public async Task ConversionEffect_ConvertsHealthIntoManaUsingAmountTaken()
    {
        var target = new IsolatedUnit { Hp = 800, MaxHp = 1000, Mp = 100, MaxMp = 500 };
        var effect = new ConversionEffect
        {
            CategoryId = 2,
            SourceCategoryId = 2,
            SourceValue = 20,
            TargetCategoryId = 4,
            TargetValue = 50
        };

        Apply(effect, target, target);

        await Assert.That(target.Hp).IsEqualTo(600);
        await Assert.That(target.Mp).IsEqualTo(200);
    }

    [Test]
    public async Task ConversionEffect_ConvertsAvailableManaWithoutOverhealing()
    {
        var target = new IsolatedUnit { Hp = 900, MaxHp = 1000, Mp = 50, MaxMp = 500 };
        var effect = new ConversionEffect
        {
            CategoryId = 1,
            SourceCategoryId = 2,
            SourceValue = 20,
            TargetCategoryId = 2,
            TargetValue = 50
        };

        Apply(effect, target, target);

        await Assert.That(target.Hp).IsEqualTo(1000);
        await Assert.That(target.Mp).IsEqualTo(0);
    }

    [Test]
    public async Task AggroEffect_RecordsThreatFromNonPlayerCaster()
    {
        var caster = new Unit { ObjId = 100 };
        var target = new Npc { ObjId = 200, Template = new NpcTemplate() };
        var effect = new AggroEffect { UseFixedAggro = true, FixedMin = 125, FixedMax = 125 };

        Apply(effect, caster, target);

        await Assert.That(target.AggroTable.TryGetValue(caster.ObjId, out var entry)).IsTrue();
        await Assert.That(entry?.TotalAggro ?? 0).IsEqualTo(125);
        await Assert.That(entry!.DamageAggro).IsEqualTo(0);
    }

    [Test]
    public async Task CinemalEffect_SetsCinemaIdForClientCompletionEvent()
    {
        var caster = new Character(new UnitCustomModelParams());

        Apply(new CinemalEffect { CinemaId = 42 }, caster, caster);

        await Assert.That(caster.CurrentlyPlayingCinemaId).IsEqualTo(42u);
    }

    [Test]
    public async Task BuffEffect_MissingTemplateIsSafeForActionTimeAndCast()
    {
        var target = new IsolatedUnit();
        var effect = new BuffEffect();

        Apply(effect, target, target);

        await Assert.That(effect.BuffId).IsEqualTo(0u);
        await Assert.That(effect.OnActionTime).IsFalse();
    }

    [Test]
    public async Task DamageEffectUsesAuthoredHeightFormulaForVariableDamage()
    {
        var singleton = typeof(Singleton<FormulaManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = singleton.GetValue(null);
        var formulas = new FormulaManager();
        var engine = new Jace.CalculationEngine(new Jace.JaceOptions
        {
            CultureInfo = System.Globalization.CultureInfo.InvariantCulture
        });
        engine.AddFunction("if_negative", (double value, double negative, double nonNegative) => value < 0 ? negative : nonNegative);
        typeof(FormulaManager).GetProperty(nameof(FormulaManager.CalculationEngine))!
            .SetValue(formulas, engine);
        singleton.SetValue(null, formulas);
        try
        {
            var key = (uint)FormulaKind.DamageMultiplierByHeight;
            var formula = new Formula("if_negative(range - 1, 1, 1.05 + (min(range, 100) / 100))") { Id = key };
            typeof(FormulaManager).GetField("_formulas", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(formulas, new Dictionary<uint, Formula> { [key] = formula });
            var caster = new IsolatedUnit { ObjId = 1, DpsInc = 1000 };
            var target = new IsolatedUnit { ObjId = 2, Hp = 1000, MaxHp = 1000 };
            caster.Transform.Local.SetPosition(10, 10, 40);
            target.Transform.Local.SetPosition(10, 10, 20);

            Apply(new DamageEffect
            {
                DamageType = DamageType.Melee, WeaponSlotId = -1, DpsIncMultiplier = 100,
                Multiplier = 1, AdjustDamageByHeight = true
            }, caster, target);

            await Assert.That(target.Hp).IsEqualTo(875);
        }
        finally
        {
            singleton.SetValue(null, previous);
        }
    }

    [Test]
    public async Task DamageEffectCombinesTheTargetHealthBandAndTaggedBonus()
    {
        var caster = new IsolatedUnit { ObjId = 1 };
        var target = new IsolatedUnit { ObjId = 2, Hp = 80, MaxHp = 1000 };
        var buffs = Mock.Of<IBuffs>();
        buffs.CheckBuffTag(5).Returns(true);
        buffs.GetAbsorptionEffects().Returns(Array.Empty<Buff>());
        target.Buffs = buffs.Object;

        Apply(new DamageEffect
        {
            DamageType = DamageType.Melee, WeaponSlotId = -1, UseFixedDamage = true, FixedMin = 20, FixedMax = 20,
            TargetHealthMin = 1, TargetHealthMax = 10, TargetHealthMul = 1.5f, TargetHealthAdd = 5,
            TargetBuffTagId = 5, TargetBuffBonusMul = 1, TargetBuffBonus = 10
        }, caster, target);

        await Assert.That(target.Hp).IsEqualTo(35);
    }

    [Test]
    [Arguments(1000, 220)]
    [Arguments(0, 100)]
    public async Task ActabilityAppliesOnlyWholeAuthoredSteps(int step, int expected)
    {
        var amount = EffectResourceMath.ApplyActability(100, 2500, step, 5, 0.5f);
        await Assert.That(amount).IsEqualTo(expected);
    }

    [Test]
    [Arguments(SkillHitType.SpellMiss)]
    [Arguments(SkillHitType.SpellResist)]
    public async Task DamageEffectDoesNotRollPastTheResolvedSpellDefense(SkillHitType result)
    {
        var caster = new IsolatedUnit { ObjId = 1 };
        var target = new IsolatedUnit { ObjId = 2, Hp = 500 };
        var skill = new Skill(new SkillTemplate { Id = 105 });
        skill.HitTypes[target.ObjId] = result;

        Apply(new DamageEffect { DamageType = DamageType.Magic, WeaponSlotId = -1, UseFixedDamage = true, FixedMin = 100, FixedMax = 100 },
            caster, target, new EffectSource(skill));

        await Assert.That(target.Hp).IsEqualTo(500);
    }

    [Test]
    public async Task FlaggedPlotHitsDiminishPerUnitAndSkillUntilReset()
    {
        var singleton = typeof(Singleton<PlotManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = singleton.GetValue(null);
        var plots = new PlotManager();
        typeof(PlotManager).GetProperty("AoeDiminishingRates", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(plots, new float[] { 100, 90, 80 });
        singleton.SetValue(null, plots);
        try
        {
            var caster = new IsolatedUnit { ObjId = 1 };
            var target = new IsolatedUnit { ObjId = 2, Hp = 1000, MaxHp = 1000 };
            var otherTarget = new IsolatedUnit { ObjId = 2, Hp = 1000, MaxHp = 1000 };
            var source = new EffectSource(new Skill(new SkillTemplate { Id = 104 })) { AoeDiminishing = true };
            var other = new EffectSource(new Skill(new SkillTemplate { Id = 105 })) { AoeDiminishing = true };
            var damage = new DamageEffect { DamageType = DamageType.Melee, WeaponSlotId = -1, UseFixedDamage = true, FixedMin = 100, FixedMax = 100 };

            Apply(damage, caster, target, source);
            Apply(damage, caster, target, source);
            Apply(damage, caster, target, other);
            Apply(damage, caster, otherTarget, source);

            await Assert.That(target.Hp).IsEqualTo(710);
            await Assert.That(otherTarget.Hp).IsEqualTo(900);

            Apply(new ResetAoeDiminishingEffect(), caster, target);
            Apply(damage, caster, target, source);

            await Assert.That(target.Hp).IsEqualTo(610);
        }
        finally
        {
            singleton.SetValue(null, previous);
        }
    }

    [Test]
    public async Task AoeCounterClampsAtTheLastAuthoredRateAndExpires()
    {
        var target = new IsolatedUnit();
        var rates = new float[] { 100, 90, 80, 70, 55, 40, 25, 10, 10, 10 };
        var time = DateTime.UtcNow;
        for (var hit = 0; hit < 12; hit++)
            await Assert.That(AoeDiminishing.Advance(target, 105, time.AddSeconds(hit), rates))
                .IsEqualTo(rates[Math.Min(hit, rates.Length - 1)] * 0.01f);

        await Assert.That(AoeDiminishing.Advance(target, 105, time.AddSeconds(22), rates)).IsEqualTo(1f);
    }

    [Test]
    [Arguments(true, 510)]
    [Arguments(false, 450)]
    public async Task DamageEffectAddsTheAuthoredPercentageOfVictimHealth(bool useCurrentHealth, int remaining)
    {
        var caster = new IsolatedUnit { ObjId = 1, Hp = 1900, MaxHp = 2000 };
        var target = new IsolatedUnit { ObjId = 2, Hp = 600, MaxHp = 1200 };

        Apply(new DamageEffect
        {
            DamageType = DamageType.Melee, WeaponSlotId = -1, UseFixedDamage = true,
            FixedMin = 30, FixedMax = 30, UsePercentDamage = true, UseCurrentHealth = useCurrentHealth,
            PercentMin = 10, PercentMax = 10
        }, caster, target);

        await Assert.That(target.Hp).IsEqualTo(remaining);
    }

    [Test]
    public async Task DamageEffect_WithManaDamageLeavesHealthUntouched()
    {
        var caster = new IsolatedUnit { ObjId = 1 };
        var target = new IsolatedUnit { ObjId = 2, Hp = 500, Mp = 100 };
        var effect = new DamageEffect
        {
            DamageType = DamageType.Melee,
            ManaDamage = true,
            UseFixedDamage = true,
            FixedMin = 25,
            FixedMax = 25,
            WeaponSlotId = -1
        };

        Apply(effect, caster, target);

        await Assert.That(target.Hp).IsEqualTo(500);
        await Assert.That(target.Mp).IsEqualTo(75);
    }

    [Test]
    public async Task DamageEffect_WithCombatDisabledLeavesUnitsOutOfBattle()
    {
        var caster = new IsolatedUnit { ObjId = 1 };
        var target = new IsolatedUnit { ObjId = 2, Hp = 500 };
        var effect = new DamageEffect
        {
            DamageType = DamageType.Melee,
            EngageCombat = false,
            UseFixedDamage = true,
            FixedMin = 25,
            FixedMax = 25,
            WeaponSlotId = -1
        };

        Apply(effect, caster, target);

        await Assert.That(target.Hp).IsEqualTo(475);
        await Assert.That(target.IsInBattle).IsFalse();
        await Assert.That(caster.IsInBattle).IsFalse();
    }

    [Test]
    public async Task DamageEffect_ConsumesTargetsAuthoredChargeWithoutAnOriginatingSkill()
    {
        var caster = new IsolatedUnit { ObjId = 1 };
        var target = new IsolatedUnit { ObjId = 2, Hp = 500 };
        var charged = new Buff(target, caster, new SkillCasterUnit(caster.ObjId),
            new BuffTemplate { Id = 3930 }, null, DateTime.UtcNow)
        {
            Charge = 3,
            InUse = true,
            State = EffectState.Acting
        };
        var buffCollection = Mock.Of<IBuffs>();
        buffCollection.GetEffectFromBuffId(3930).Returns(charged);
        buffCollection.GetAbsorptionEffects().Returns(Array.Empty<Buff>());
        buffCollection.GetBuffsRequiring(3930).Returns(Array.Empty<Buff>());
        target.Buffs = buffCollection.Object;
        var effect = new DamageEffect
        {
            DamageType = DamageType.Melee,
            Multiplier = 1,
            WeaponSlotId = -1,
            UseTargetChargedBuff = true,
            TargetChargedBuffId = 3930,
            TargetChargedMul = 60
        };

        Apply(effect, caster, target);

        await Assert.That(target.Hp).IsEqualTo(320);
        await Assert.That(charged.IsEnded()).IsTrue();
    }

    [Test]
    public async Task LoseTarget_ClearsTheAffectedUnitsSelection()
    {
        var caster = new IsolatedUnit { ObjId = 1 };
        var selectedUnit = new Unit { ObjId = 3 };
        var target = new IsolatedUnit { ObjId = 2, CurrentTarget = selectedUnit };
        caster.CurrentTarget = target;

        Apply(new SpecialEffect { SpecialEffectTypeId = SpecialType.LoseTarget }, caster, target);

        await Assert.That(target.CurrentTarget).IsNull();
        await Assert.That(caster.CurrentTarget).IsEqualTo(target);
    }

    [Test]
    [Arguments(DamageType.Melee)]
    [Arguments(DamageType.Magic)]
    [Arguments(DamageType.Ranged)]
    [Arguments(DamageType.Siege)]
    public async Task DamageEffectPublishesTheMatchingOutgoingDamageEvent(DamageType damageType)
    {
        var caster = new IsolatedUnit { ObjId = 1 };
        var target = new IsolatedUnit { ObjId = 2, Hp = 500 };
        DamageType? receivedType = null;
        OnDamageArgs received = null;
        caster.Events.OnDamageMelee += (_, args) => Capture(DamageType.Melee, args);
        caster.Events.OnDamageSpell += (_, args) => Capture(DamageType.Magic, args);
        caster.Events.OnDamageRanged += (_, args) => Capture(DamageType.Ranged, args);
        caster.Events.OnDamageSiege += (_, args) => Capture(DamageType.Siege, args);

        Apply(new DamageEffect { DamageType = damageType, UseFixedDamage = true, FixedMin = 25, FixedMax = 25, WeaponSlotId = -1 }, caster, target);

        await Assert.That(receivedType).IsEqualTo(damageType);
        await Assert.That(received!.Receiver).IsSameReferenceAs(target);
        await Assert.That(received.Amount).IsEqualTo(25);

        void Capture(DamageType type, OnDamageArgs args)
        {
            receivedType = type;
            received = args;
        }
    }

    private static void Apply(EffectTemplate effect, BaseUnit caster, BaseUnit target, EffectSource source = null)
    {
        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), target, new SkillCastUnitTarget(target.ObjId),
            new CastSkill(1, 1), source ?? new EffectSource(), new SkillObject(), DateTime.UtcNow);
    }

    private sealed class IsolatedUnit : Unit
    {
        public override void BroadcastPacket(GamePacket packet, bool self)
        {
        }
    }

    private sealed class IsolatedCharacter() : Character(new UnitCustomModelParams())
    {
        public override void BroadcastPacket(GamePacket packet, bool self)
        {
        }
    }
}
