using System.Reflection;
using System.Globalization;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

[NotInParallel]
public class HighAbilitySelectionTests
{
    [Test]
    public async Task ActivationAndSwapTrackTheHighAbilityAndBaseAbilityPair()
    {
        using var db = new SqliteConnection("Data Source=:memory:");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            CREATE TABLE high_abilities (id INT, ability_id INT, high_ability_id INT, max_high_ability_resource INT, or_unit_reqs INT);
            INSERT INTO high_abilities VALUES (1, 1, 1, 400, 0), (2, 6, 1, 400, 0);
            CREATE TABLE unit_reqs (owner_id, owner_type, kind_id, value1, value2, value3, display_msg);
            INSERT INTO unit_reqs VALUES (2, 'HighAbility', 1, 55, 0, 0, 0);
            """;
        command.ExecuteNonQuery();
        var manager = new SkillManager(null, null);
        manager.LoadHighAbilityResources(db);
        var requirements = new UnitRequirementsGameData();
        requirements.Load(db, db);
        var formulaManager = new FormulaManager();
        typeof(FormulaManager).GetProperty(nameof(FormulaManager.CalculationEngine))!.SetValue(formulaManager,
            new Jace.CalculationEngine(new Jace.JaceOptions { CultureInfo = CultureInfo.InvariantCulture }));
        var formulaField = typeof(Singleton<FormulaManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousFormula = formulaField.GetValue(null);
        formulaField.SetValue(null, formulaManager);
        typeof(FormulaManager).GetField("_formulas", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(formulaManager,
            new Dictionary<uint, Formula> { [(uint)FormulaKind.SwapAbilityCost] = new("pc_level * config_swap_ability_cost") });
        var skillsField = typeof(Singleton<SkillManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var reqsField = typeof(Singleton<UnitRequirementsGameData>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousSkills = skillsField.GetValue(null);
        var previousReqs = reqsField.GetValue(null);
        skillsField.SetValue(null, manager);
        reqsField.SetValue(null, requirements);
        try
        {
            var character = new Character(null)
            {
                Id = 1, ObjId = 100,
                Ability1 = AbilityType.Fight, Ability2 = AbilityType.Wild, Ability3 = AbilityType.Illusion
            };
            character.Abilities = new CharacterAbilities(character);

            await Assert.That(character.Abilities.TrySelectHighAbility(1, AbilityType.None, 1, AbilityType.Fight)).IsFalse();
            character.Ability3 = AbilityType.None;
            await Assert.That(character.Abilities.TrySelectHighAbility(0, AbilityType.None, 1, AbilityType.Fight)).IsFalse();
            character.Ability3 = AbilityType.Illusion;
            await Assert.That(character.Abilities.TrySelectHighAbility(0, AbilityType.None, 1, AbilityType.Fight)).IsTrue();
            await Assert.That(character.MaxHighAbilityRsc).IsEqualTo(400);
            await Assert.That(character.Abilities.TrySelectHighAbility(0, AbilityType.None, 1, AbilityType.Fight)).IsFalse();
            character.Level = 55;
            await Assert.That(character.Abilities.TrySelectHighAbility(0, AbilityType.None, 1, AbilityType.Wild)).IsFalse();
            character.Level = 1;
            await Assert.That(character.Abilities.TrySelectHighAbility(1, AbilityType.Fight, 1, AbilityType.Wild)).IsFalse();
            await Assert.That(character.HighAbility1).IsEqualTo(AbilityType.Fight);
            character.Level = 55;
            character.Money = 32999;
            await Assert.That(character.Abilities.TrySelectHighAbility(1, AbilityType.Fight, 1, AbilityType.Wild)).IsFalse();
            await Assert.That(character.Money).IsEqualTo(32999);
            character.Money = 33000;
            await Assert.That(character.Abilities.TrySelectHighAbility(1, AbilityType.Fight, 1, AbilityType.Wild)).IsTrue();
            await Assert.That(character.HighAbility1).IsEqualTo(AbilityType.Wild);
            await Assert.That(character.Money).IsEqualTo(0);
            await Assert.That(character.Abilities.TrySelectHighAbility(1, AbilityType.Fight, 1, AbilityType.Fight)).IsFalse();
            await Assert.That(character.HighAbility1).IsEqualTo(AbilityType.Wild);
        }
        finally
        {
            skillsField.SetValue(null, previousSkills);
            reqsField.SetValue(null, previousReqs);
            formulaField.SetValue(null, previousFormula);
        }
    }

    [Test]
    public async Task ActivationPacketUsesTheNativePairOrdering()
    {
        var packet = new SCHighAbilityActivedPacket(123, 0, AbilityType.None, 1, AbilityType.Wild, false);
        var reader = new PacketStream(packet.Write(new PacketStream()).GetBytes());

        await Assert.That(reader.ReadBc()).IsEqualTo(123u);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)AbilityType.None);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)1);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)AbilityType.Wild);
        await Assert.That(reader.ReadBoolean()).IsFalse();
        await Assert.That(reader.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task HighAbilitySkillsRequireTheirOwnActiveBranch()
    {
        var character = new Character(null) { ObjId = 1, HighAbility1 = AbilityType.Fight, HighAbilityRsc = 400 };
        character.Abilities = new CharacterAbilities(character);
        var skill = new Skill(new SkillTemplate { Id = 34093, HighAbilityId = 1, AbilityId = AbilityType.Magic });

        var result = skill.Use(character, new SkillCasterUnit(character.ObjId), new SkillCastUnitTarget(character.ObjId),
            null, true, out _);

        await Assert.That(result).IsEqualTo(SkillResult.InvalidSkill);
        await Assert.That(character.Abilities.IsHighAbilityActive(1, AbilityType.Fight)).IsTrue();
        await Assert.That(character.Abilities.IsHighAbilityActive(1, AbilityType.Magic)).IsFalse();
        await Assert.That(character.Abilities.IsHighAbilityActive(1, AbilityType.General)).IsTrue();
        await Assert.That(character.Abilities.IsHighAbilityActive(2, AbilityType.Fight)).IsFalse();
    }

    [Test]
    public async Task RequestAcceptsThePlayerOrANearbyAbilityChanger()
    {
        var character = new Character(null) { ObjId = 1, Hp = 100 };
        var world = new WorldInstance(new WorldTemplate(), 0, true, character.Transform.InstanceId);
        character.ParentWorld = world;
        var npc = new Npc
        {
            ObjId = 2, Hp = 100, ParentWorld = world,
            Template = new NpcTemplate { AbilityChanger = true }
        };
        world.AddObject(npc);

        await Assert.That(CSSelectHighAbilityPacket.IsValidSource(character, false, character.ObjId)).IsTrue();
        await Assert.That(CSSelectHighAbilityPacket.IsValidSource(character, false, npc.ObjId)).IsTrue();
        await Assert.That(CSSelectHighAbilityPacket.IsValidSource(character, true, npc.ObjId)).IsTrue();
        await Assert.That(CSSelectHighAbilityPacket.IsValidSource(character, true, character.ObjId)).IsFalse();
        npc.Template.AbilityChanger = false;
        await Assert.That(CSSelectHighAbilityPacket.IsValidSource(character, true, npc.ObjId)).IsFalse();
    }

    [Test]
    public async Task CharacterStateReportsOnlyTheSelectedHighAbilities()
    {
        var character = new Character(null)
        {
            HighAbility1 = AbilityType.Fight,
            HighAbility2 = AbilityType.Wild,
            HighAbility3 = AbilityType.None,
            Ability1 = AbilityType.Fight,
            Ability2 = AbilityType.Wild,
            Ability3 = AbilityType.None,
            HeirExp = 123
        };
        character.Abilities = new CharacterAbilities(character);
        character.Abilities.Abilities[AbilityType.Fight].Order = 2;
        character.Abilities.Abilities[AbilityType.Wild].Order = 0;
        var stream = new PacketStream();
        CharacterStatePacket.WriteHighAbilityInfos(stream, character);
        var reader = new PacketStream(stream.GetBytes());

        for (var kind = 0; kind < 2; kind++)
        {
            for (var ability = 0; ability < 13; ability++)
            {
                var active = reader.ReadBoolean();
                var order = reader.ReadByte();
                reader.ReadByte();
                var exp = reader.ReadUInt32();
                var expected = kind == 1 && (ability == (int)AbilityType.Fight || ability == (int)AbilityType.Wild);
                await Assert.That(active).IsEqualTo(expected);
                await Assert.That(order).IsEqualTo(expected ? (ability == (int)AbilityType.Fight ? (byte)3 : (byte)1) : (byte)0);
                await Assert.That(exp).IsEqualTo(123u);
            }
        }
    }
}
