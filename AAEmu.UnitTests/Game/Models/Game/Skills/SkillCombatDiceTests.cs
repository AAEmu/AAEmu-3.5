using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

[NotInParallel]
public sealed class SkillCombatDiceTests
{
    [Test]
    public async Task RestoreManaUsesModifiedRankWithoutChangingTheStoredRank()
    {
        var caster = new TestUnit { ObjId = 1, LevelDps = 100 };
        var target = new TestUnit { ObjId = 2, MaxMp = 1000 };
        var skill = new Skill(new SkillTemplate { Id = 12026, AbilityLevel = 20, LevelStep = 5 });
        caster.SkillModifiersCache.AddModifier(new SkillModifier
        {
            SkillId = skill.Id, SkillAttribute = SkillAttribute.SkillLevel, UnitModifierType = UnitModifierType.Value, Value = 1
        });
        var effect = new RestoreManaEffect { UseLevelValue = true, LevelMd = 1f };

        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), target, new SkillCastUnitTarget(target.ObjId),
            new CastSkill(skill.Id, 1), new EffectSource(skill), null, DateTime.UtcNow);

        await Assert.That(target.Mp).IsEqualTo(190);
        await Assert.That(skill.Level).IsEqualTo((byte)1);
    }

    [Test]
    public async Task BuffDamageModifierScalesDamageFromTheMatchingBuff()
    {
        var caster = new TestUnit { ObjId = 1 };
        var target = new TestUnit { ObjId = 2, Hp = 1000, MaxHp = 1000 };
        var template = new BuffTemplate { Id = 121 };
        var casterObject = new SkillCasterUnit(caster.ObjId);
        var active = new Buff(target, caster, casterObject, template, null, DateTime.UtcNow);
        caster.BuffModifiersCache.AddModifier(new BuffModifier
        {
            BuffId = template.Id, BuffAttribute = BuffAttribute.Damage, UnitModifierType = UnitModifierType.Percent, Value = 20
        });
        var effect = CreateDamage(DamageType.Magic);
        effect.FixedMin = effect.FixedMax = 100;

        effect.Apply(caster, casterObject, target, new SkillCastUnitTarget(target.ObjId), new CastBuff(active),
            new EffectSource(template), null, DateTime.UtcNow);

        await Assert.That(target.Hp).IsEqualTo(880);
    }

    [Test]
    public async Task NpcDiceConsumeCriticalAndAvoidanceFormulas()
    {
        using var formulas = new NpcFormulaScope();
        var npc = new Npc { Template = new NpcTemplate() };
        var attacker = new Unit();

        var critical = CombatDiceResolver.Resolve(npc, attacker, CombatDiceKind.Melee, DamageType.Melee, false, 0.05f);
        var dodge = CombatDiceResolver.Resolve(attacker, npc, CombatDiceKind.Melee, DamageType.Melee, true, 0.02f);
        npc.AddBonus(1, new Bonus { Template = new BonusTemplate { Attribute = UnitAttribute.DodgeMul }, Value = 100 });
        var boostedDodge = CombatDiceResolver.Resolve(attacker, npc, CombatDiceKind.Melee, DamageType.Melee, true, 0.1f);

        await Assert.That(critical).IsEqualTo(SkillHitType.MeleeCritical);
        await Assert.That(dodge).IsEqualTo(SkillHitType.MeleeDodge);
        await Assert.That(boostedDodge).IsEqualTo(SkillHitType.MeleeDodge);
        await Assert.That(npc.MeleeCriticalBonus).IsEqualTo(50f);
        await Assert.That(npc.HealCriticalBonus).IsEqualTo(50f);
    }

    [Test]
    [Arguments(0f, 975)]
    [Arguments(100f, 938)]
    public async Task AuthoredCriticalDamageBonusAppliesOnCriticalHitsOnly(float criticalChance, int remainingHealth)
    {
        using var manager = new SkillManagerScope();
        var caster = new TestUnit { ObjId = 1, MeleeCritical = criticalChance, MeleeCriticalBonus = 50 };
        var target = new TestUnit { ObjId = 2, Hp = 1000, MaxHp = 1000 };
        var skill = CreateSkill(4, DamageType.Melee);
        var damage = CreateDamage(DamageType.Melee);
        damage.CriticalBonus = 100;
        AddEffect(skill, damage);

        Apply(skill, caster, target);

        await Assert.That(target.Hp).IsEqualTo(remainingHealth);
    }

    [Test]
    [Arguments(-500, 990)]
    [Arguments(-1000, 1000)]
    public async Task SiegeDamageUsesTheTargetsSiegeModifier(int siegeModifier, int remainingHealth)
    {
        using var manager = new SkillManagerScope();
        var caster = new TestUnit { ObjId = 1 };
        var target = new TestUnit { ObjId = 2, Hp = 1000, MaxHp = 1000, Armor = 5300, IncomingDamageMul = 0.8f };
        target.AddBonus(1, new Bonus
        {
            Template = new BonusTemplate { Attribute = UnitAttribute.IncomingSiegeDamageMul }, Value = siegeModifier
        });
        var skill = CreateSkill(4, DamageType.Siege);
        AddEffect(skill, CreateDamage(DamageType.Siege));

        Apply(skill, caster, target);

        await Assert.That(target.Hp).IsEqualTo(remainingHealth);
    }

    [Test]
    [Arguments(UnitAttribute.MeleeAntiMissMul, DamageType.Melee, SkillHitType.MeleeMiss, SkillHitType.MeleeHit)]
    [Arguments(UnitAttribute.RangedAntiMissMul, DamageType.Ranged, SkillHitType.RangedMiss, SkillHitType.RangedHit)]
    [Arguments(UnitAttribute.SpellAntiMissMul, DamageType.Magic, SkillHitType.SpellResist, SkillHitType.SpellHit)]
    public async Task AccuracyModifierChangesOnlyItsDamageFamily(UnitAttribute attribute, DamageType type,
        SkillHitType miss, SkillHitType hit)
    {
        var caster = new Unit();
        var target = new Unit();
        caster.AddBonus(1, new Bonus { Template = new BonusTemplate { Attribute = attribute }, Value = -700 });

        var reduced = CombatDiceResolver.Resolve(caster, target, CombatDiceKind.EachEffectRollDice, type, false, 0.5f);
        var guaranteed = CombatDiceResolver.Resolve(caster, target, CombatDiceKind.AlwaysHit, type, false, 0.5f);
        var otherType = type == DamageType.Magic ? DamageType.Melee : DamageType.Magic;
        var other = CombatDiceResolver.Resolve(caster, target, CombatDiceKind.EachEffectRollDice, otherType, false, 0.5f);
        caster.RemoveBonus(1, attribute);
        var recovered = CombatDiceResolver.Resolve(caster, target, CombatDiceKind.EachEffectRollDice, type, false, 0.5f);

        await Assert.That(reduced).IsEqualTo(miss);
        await Assert.That(guaranteed).IsEqualTo(hit);
        await Assert.That(other).IsEqualTo(otherType == DamageType.Melee ? SkillHitType.MeleeHit : SkillHitType.SpellHit);
        await Assert.That(recovered).IsEqualTo(hit);
    }

    [Test]
    public async Task PositiveAccuracyModifierScalesTheBaseChance()
    {
        var caster = new Unit { RangedAccuracy = 70 };
        caster.AddBonus(1, new Bonus { Template = new BonusTemplate { Attribute = UnitAttribute.RangedAntiMissMul }, Value = 200 });
        var target = new Unit();

        var atTwentyPercent = CombatDiceResolver.Resolve(caster, target, CombatDiceKind.Ranged, DamageType.Ranged, false, 0.2f);
        var atTenPercent = CombatDiceResolver.Resolve(caster, target, CombatDiceKind.Ranged, DamageType.Ranged, false, 0.1f);

        await Assert.That(atTwentyPercent).IsEqualTo(SkillHitType.RangedHit);
        await Assert.That(atTenPercent).IsEqualTo(SkillHitType.RangedMiss);
    }

    [Test]
    [Arguments(4, DamageType.Magic, SkillHitType.SpellHit)]
    [Arguments(6, DamageType.Magic, SkillHitType.SpellHit)]
    [Arguments(3, DamageType.Magic, SkillHitType.SpellResist)]
    [Arguments(5, DamageType.Melee, SkillHitType.MeleeMiss)]
    [Arguments(8, DamageType.Melee, SkillHitType.RangedHit)]
    public async Task RollUsesTheAuthoredDiceKind(int kind, DamageType damageType, SkillHitType expected)
    {
        var caster = new Unit { MeleeAccuracy = 0, SpellAccuracy = 0, RangedAccuracy = 100 };
        var target = new Unit { DodgeRate = 100 };
        target.Transform.Local.SetPosition(0, 2, 0);
        target.Transform.Local.SetZRotation(MathF.PI);
        var skill = CreateSkill(kind, damageType);

        await Assert.That(skill.RollCombatDice(caster, target)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(false, SkillHitType.MeleeHit)]
    [Arguments(true, SkillHitType.MeleeDodge)]
    public async Task MeleeAvoidanceUsesTheDefendersFacing(bool facingAttacker, SkillHitType expected)
    {
        var caster = new Unit();
        var target = new Unit { DodgeRate = 100 };
        target.Transform.Local.SetPosition(0, 2, 0);
        target.Transform.Local.SetZRotation(facingAttacker ? MathF.PI : 0);
        var skill = CreateSkill(1, DamageType.Melee);

        await Assert.That(skill.RollCombatDice(caster, target)).IsEqualTo(expected);
    }

    [Test]
    public async Task AlwaysHitAppliesOnlyToTheMarkedDamageEffect()
    {
        using var manager = new SkillManagerScope();
        var caster = new TestUnit { ObjId = 1, SpellAccuracy = 0 };
        var target = new TestUnit { ObjId = 2, Hp = 1000, MaxHp = 1000 };
        var skill = CreateSkill(3, DamageType.Magic);
        AddEffect(skill, CreateDamage(DamageType.Magic), alwaysHit: true);
        AddEffect(skill, CreateDamage(DamageType.Magic));

        Apply(skill, caster, target);

        await Assert.That(target.Hp).IsEqualTo(975);
        await Assert.That(caster.LastHit).IsEqualTo(SkillHitType.SpellResist);
    }

    [Test]
    public async Task PositionalDamageRollsEvenWithoutAHostileTargetType()
    {
        using var manager = new SkillManagerScope();
        var caster = new TestUnit { ObjId = 1, SpellAccuracy = 0 };
        var target = new TestUnit { ObjId = 2, Hp = 1000, MaxHp = 1000 };
        var skill = CreateSkill(3, DamageType.Magic, SkillTargetType.Pos);
        AddEffect(skill, CreateDamage(DamageType.Magic));

        Apply(skill, caster, target);

        await Assert.That(target.Hp).IsEqualTo(1000);
        await Assert.That(caster.LastHit).IsEqualTo(SkillHitType.SpellResist);
    }

    [Test]
    public async Task EachEffectUsesItsOwnDamageFamily()
    {
        using var manager = new SkillManagerScope();
        var caster = new TestUnit { ObjId = 1, MeleeAccuracy = 0, SpellAccuracy = 100 };
        var target = new TestUnit { ObjId = 2, Hp = 1000, MaxHp = 1000 };
        var skill = CreateSkill(7, DamageType.Magic, SkillTargetType.AnyUnit);
        AddEffect(skill, CreateDamage(DamageType.Melee));
        AddEffect(skill, CreateDamage(DamageType.Magic));

        Apply(skill, caster, target);

        await Assert.That(target.Hp).IsEqualTo(975);
        await Assert.That(caster.LastHit).IsEqualTo(SkillHitType.MeleeMiss);
    }

    [Test]
    [Arguments(1, 900)]
    [Arguments(7, 925)]
    public async Task CriticalRollIsSharedOnlyByCastWideDice(int kind, int remainingHealth)
    {
        using var manager = new SkillManagerScope();
        var caster = new TestUnit { ObjId = 1, MeleeCritical = 100, MeleeCriticalBonus = 100 };
        var target = new TestUnit { ObjId = 2, Hp = 1000, MaxHp = 1000 };
        caster.Events.OnDamage += (_, _) => caster.MeleeCritical = 0;
        var skill = CreateSkill(kind, DamageType.Melee);
        AddEffect(skill, CreateDamage(DamageType.Melee));
        AddEffect(skill, CreateDamage(DamageType.Melee));

        Apply(skill, caster, target);

        await Assert.That(target.Hp).IsEqualTo(remainingHealth);
    }

    [Test]
    public async Task AnEffectCriticalBonusAppliesOnlyToItsOwnDamage()
    {
        using var manager = new SkillManagerScope();
        var caster = new TestUnit { ObjId = 1, MeleeCriticalBonus = 100 };
        var target = new TestUnit { ObjId = 2, Hp = 1000, MaxHp = 1000 };
        var skill = CreateSkill(1, DamageType.Melee);
        var enhancedDamage = CreateDamage(DamageType.Melee);
        enhancedDamage.Bonuses.Add(new BonusTemplate { Attribute = UnitAttribute.MeleeCritical, Value = 100 });
        AddEffect(skill, enhancedDamage);
        AddEffect(skill, CreateDamage(DamageType.Melee));

        Apply(skill, caster, target);

        await Assert.That(target.Hp).IsEqualTo(925);
        await Assert.That(caster.MeleeCritical).IsEqualTo(0);
    }

    [Test]
    public async Task BullsEyeUsesTheAttackersFacetsForAvoidance()
    {
        var caster = new Unit { BullsEye = 100, Facets = 105_000 };
        var target = new Unit { DodgeRate = 40 };

        var hit = CombatDiceResolver.Resolve(caster, target, CombatDiceKind.Melee, DamageType.Melee, canDefend: true, roll: 0.35f);

        await Assert.That(hit).IsEqualTo(SkillHitType.MeleeHit);
    }

    [Test]
    public async Task FlexibilityUsesTheDefendersFacetsForCriticalChance()
    {
        var caster = new Unit { SpellCritical = 40 };
        var target = new Unit { Flexibility = 100, Facets = 90_000 };

        var hit = CombatDiceResolver.Resolve(caster, target, CombatDiceKind.Spell, DamageType.Magic, canDefend: false, roll: 0.35f);

        await Assert.That(hit).IsEqualTo(SkillHitType.SpellHit);
    }

    private static Skill CreateSkill(int diceKind, DamageType damageType, SkillTargetType targetType = SkillTargetType.Hostile) =>
        new(new SkillTemplate { Id = 1, CombatDiceId = diceKind, DamageTypeId = (uint)damageType, TargetType = targetType });

    private static DamageEffect CreateDamage(DamageType damageType) => new()
    {
        DamageType = damageType, WeaponSlotId = -1, UseFixedDamage = true, FixedMin = 25, FixedMax = 25
    };

    private static void AddEffect(Skill skill, EffectTemplate effect, bool alwaysHit = false) => skill.Template.Effects.Add(new SkillEffect
    {
        ApplicationMethod = SkillEffectApplicationMethod.Target, EndLevel = 99, Chance = 100, AlwaysHit = alwaysHit, Template = effect
    });

    private static void Apply(Skill skill, Unit caster, Unit target) => skill.ApplyEffects(caster,
        new SkillCasterUnit(caster.ObjId), target, new SkillCastUnitTarget(target.ObjId), null);

    private sealed class NpcFormulaScope : IDisposable
    {
        private readonly FieldInfo _field = typeof(Singleton<FormulaManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object _previous;

        public NpcFormulaScope()
        {
            var manager = new FormulaManager();
            typeof(FormulaManager).GetProperty(nameof(FormulaManager.CalculationEngine))!.SetValue(manager, new Jace.CalculationEngine());
            _previous = _field.GetValue(null);
            _field.SetValue(null, manager);
            var formulas = new Dictionary<UnitFormulaKind, UnitFormula>();
            Add(UnitFormulaKind.Facet, "200000");
            Add(UnitFormulaKind.Dex, "40");
            Add(UnitFormulaKind.Int, "60");
            Add(UnitFormulaKind.MeleeCritical, "(dex * 4 * npc_kind) * 100", id: 18);
            Add(UnitFormulaKind.Dodge, "(dex + int) / 2 * 100");
            Add(UnitFormulaKind.MeleeCriticalBonus, "1500");
            Add(UnitFormulaKind.HealCriticalBonus, "1500");
            typeof(FormulaManager).GetField("_unitFormulas", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(manager, new Dictionary<FormulaOwnerType, Dictionary<UnitFormulaKind, UnitFormula>> { [FormulaOwnerType.Npc] = formulas });
            typeof(FormulaManager).GetField("_unitVariables", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager,
                new Dictionary<uint, Dictionary<UnitFormulaVariableType, Dictionary<uint, UnitFormulaVariable>>>
                {
                    [18] = new() { [UnitFormulaVariableType.NpcKind] = new() { [0] = new UnitFormulaVariable { Value = 1 } } }
                });

            void Add(UnitFormulaKind kind, string text, uint id = 0)
            {
                var formula = new UnitFormula { Id = id, Kind = kind, Owner = FormulaOwnerType.Npc, TextFormula = text };
                if (!formula.Prepare())
                    throw new InvalidOperationException($"Invalid test formula for {kind}");
                formulas.Add(kind, formula);
            }
        }

        public void Dispose() => _field.SetValue(null, _previous);
    }

    private sealed class SkillManagerScope : IDisposable
    {
        private readonly FieldInfo _field = typeof(Singleton<SkillManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object _previous;

        public SkillManagerScope()
        {
            var manager = new SkillManager(null, null);
            typeof(SkillManager).GetField("_skillReagents", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(manager, new Dictionary<uint, SkillReagent>());
            typeof(SkillManager).GetField("_skillProducts", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(manager, new Dictionary<uint, SkillProduct>());
            _previous = _field.GetValue(null);
            _field.SetValue(null, manager);
        }

        public void Dispose() => _field.SetValue(null, _previous);
    }

    private sealed class TestUnit : Unit
    {
        public SkillHitType LastHit { get; private set; }

        public override float MeleeCritical
        {
            get => (float)CalculateWithBonuses(base.MeleeCritical, UnitAttribute.MeleeCritical);
            set => base.MeleeCritical = value;
        }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            if (packet is SCUnitDamagedPacket damaged)
                LastHit = damaged.HitType;
        }
    }
}
