using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

[NotInParallel]
public class HighAbilityResourceTests
{
    [Test]
    public async Task EffectChangesTargetResourceAndClampsToItsCapacity()
    {
        var caster = new TestUnit { ObjId = 1, Hp = 100, HighAbilityRsc = 100 };
        var target = new TestUnit { ObjId = 2, Hp = 100, HighAbilityRsc = 390, MaxHighAbilityRsc = 400 };
        var gain = new HighAbilityResourceEffect { MinHighAbilityResource = 20, MaxHighAbilityResource = 20 };
        var spend = new HighAbilityResourceEffect { MinHighAbilityResource = -600, MaxHighAbilityResource = -600 };

        gain.Apply(caster, null, target, null, null, null, null, DateTime.UtcNow);
        await Assert.That(target.HighAbilityRsc).IsEqualTo(400);
        await Assert.That(caster.HighAbilityRsc).IsEqualTo(100);
        await Assert.That(target.Packets).IsEqualTo(1);

        spend.Apply(caster, null, target, null, null, null, null, DateTime.UtcNow);
        await Assert.That(target.HighAbilityRsc).IsEqualTo(0);
        await Assert.That(target.Packets).IsEqualTo(2);
    }

    [Test]
    public async Task SkillRejectsResourceOutsideAuthoredLevelRange()
    {
        var unit = new TestUnit { ObjId = 1, Hp = 100, HighAbilityRsc = 399 };
        var skill = new Skill(new SkillTemplate { Id = 1, MinHighAbilityResource = 4, MaxHighAbilityResource = 6 });

        var result = skill.Use(unit, new SkillCasterUnit(unit.ObjId), new SkillCastUnitTarget(unit.ObjId),
            null, true, out _);

        await Assert.That(result).IsEqualTo(SkillResult.LackHighAbilityResource);
    }

    [Test]
    public async Task SkillEffectRunsOnlyWithinItsAuthoredResourceBand()
    {
        var manager = new SkillManager(null, null);
        typeof(SkillManager).GetField("_skillReagents", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, new Dictionary<uint, SkillReagent>());
        typeof(SkillManager).GetField("_skillProducts", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, new Dictionary<uint, SkillProduct>());
        var singleton = typeof(Singleton<SkillManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = singleton.GetValue(null);
        singleton.SetValue(null, manager);
        try
        {
            var caster = new TestUnit { ObjId = 1, Hp = 100, HighAbilityRsc = 399, Level = 1 };
            var effect = new RecordingEffect();
            var template = new SkillTemplate { Id = 1, TargetSelection = SkillTargetSelection.Source };
            template.Effects.Add(new SkillEffect
            {
                ApplicationMethod = SkillEffectApplicationMethod.Target,
                StartHighAbilityResource = 4, EndHighAbilityResource = 6,
                EndLevel = 99, Chance = 100, Front = true, Back = true, Template = effect
            });
            var skill = new Skill(template);
            var source = new SkillCasterUnit(caster.ObjId);
            var target = new SkillCastUnitTarget(caster.ObjId);

            skill.ApplyEffects(caster, source, caster, target, null);
            await Assert.That(effect.Applications).IsEqualTo(0);
            caster.HighAbilityRsc = 400;
            skill.ApplyEffects(caster, source, caster, target, null);
            await Assert.That(effect.Applications).IsEqualTo(1);
            caster.HighAbilityRsc = 700;
            skill.ApplyEffects(caster, source, caster, target, null);
            await Assert.That(effect.Applications).IsEqualTo(1);
        }
        finally
        {
            singleton.SetValue(null, previous);
        }
    }

    [Test]
    public async Task PlotRequirementUsesWholeResourceLevels()
    {
        var unit = new TestUnit { ObjId = 1, HighAbilityRsc = 399 };
        var condition = new PlotCondition
        {
            Kind = PlotConditionType.HighAbilityResource,
            Param1 = 4,
            Param2 = 6
        };
        await Assert.That(condition.Check(unit, null, unit, null, null, null)).IsFalse();
        unit.HighAbilityRsc = 400;
        await Assert.That(condition.Check(unit, null, unit, null, null, null)).IsTrue();
        unit.HighAbilityRsc = 700;
        await Assert.That(condition.Check(unit, null, unit, null, null, null)).IsFalse();
    }

    [Test]
    public async Task CapacityUsesDistinctSelectedAbilitiesAndAuthoredValues()
    {
        using var db = new SqliteConnection("Data Source=:memory:");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            CREATE TABLE high_abilities (id INT, ability_id INT, high_ability_id INT, max_high_ability_resource INT, or_unit_reqs INT);
            INSERT INTO high_abilities VALUES (1, 1, 1, 400, 0), (2, 6, 1, 300, 0);
            """;
        command.ExecuteNonQuery();
        var manager = new SkillManager(null, null);
        manager.LoadHighAbilityResources(db);
        var singleton = typeof(Singleton<SkillManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = singleton.GetValue(null);
        singleton.SetValue(null, manager);
        try
        {
            var character = new Character(null)
            {
                HighAbility1 = AbilityType.Fight,
                HighAbility2 = AbilityType.Wild,
                HighAbility3 = AbilityType.Fight
            };
            await Assert.That(character.MaxHighAbilityRsc).IsEqualTo(700);
            character.HighAbility2 = AbilityType.None;
            await Assert.That(character.MaxHighAbilityRsc).IsEqualTo(400);
            character.AddBonus(1, new Bonus
            {
                Template = new BonusTemplate { Attribute = (UnitAttribute)215, ModifierType = UnitModifierType.Value },
                Value = 200
            });
            await Assert.That(character.MaxHighAbilityRsc).IsEqualTo(600);
        }
        finally
        {
            singleton.SetValue(null, previous);
        }
    }

    private sealed class TestUnit : Unit
    {
        public int Packets { get; private set; }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            if (packet is SCUnitPointsPacket)
                Packets++;
        }
    }

    private sealed class RecordingEffect : EffectTemplate
    {
        public int Applications { get; private set; }
        public override bool OnActionTime => false;

        public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
            CompressedGamePackets packetBuilder = null) => Applications++;
    }
}
