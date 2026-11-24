using AAEmu.Game.GameData;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillAuthorityTests
{
    [Test]
    public async Task AutoLearnOnAGeneralTemplateDoesNotGrantTheSkill()
    {
        var sender = CreateCharacter();
        var source = new SkillTemplate { Id = 500, AbilityId = AbilityType.General, AbilityLevel = 1 };
        sender.Skills.AddSkill(source, 1, false);
        var skill = new SkillTemplate { Id = 501, AbilityId = AbilityType.General, AbilityLevel = 1, AutoLearn = true };

        var result = CreateAuthority().Check(sender, new SkillCasterUnit(sender.ObjId), null, skill);

        await Assert.That(result).IsEqualTo(SkillResult.InvalidSkill);
    }

    [Test]
    public async Task LearnedBaseSkillDoesNotAuthorizeUnselectedHeirVariant()
    {
        var sender = CreateCharacter();
        var authority = new SkillAuthority(CreateData(HeirRows));
        var variant = new SkillTemplate { Id = 502, Name = "Origin", AbilityId = AbilityType.Fight, AbilityLevel = 1 };
        var caster = new SkillCasterUnit(sender.ObjId);

        await Assert.That(authority.Check(sender, caster, null, variant)).IsEqualTo(SkillResult.InvalidSkill);
        sender.Skills.AddSkill(new SkillTemplate { Id = 501, Name = "Origin", AbilityId = AbilityType.Fight, AbilityLevel = 1 }, 1, false);

        await Assert.That(authority.Check(sender, caster, null, variant)).IsEqualTo(SkillResult.InvalidSkill);
    }

    [Test]
    public async Task SelectionGrantsOnlyTheChosenSuccessorAndResetRevokesIt()
    {
        var data = CreateData(HeirRows);
        var sender = CreateHeirCharacter(data);
        var authority = new SkillAuthority(data);
        var caster = new SkillCasterUnit(sender.ObjId);
        var selected = new SkillTemplate { Id = 502, AbilityId = AbilityType.Fight, AbilityLevel = 1 };
        var unselected = new SkillTemplate { Id = 503, AbilityId = AbilityType.Fight, AbilityLevel = 1 };

        var request = new PacketStream().Write((ushort)10).Write((ushort)502).Write(false);
        request.Rollback();
        var packet = new CSActivateHeirSkillPacket { Connection = new GameConnection(null) { ActiveChar = sender } };
        packet.Read(request);

        await Assert.That(packet.TypeId).IsEqualTo((ushort)0x08A);
        await Assert.That(request.Pos).IsEqualTo(request.Count);
        await Assert.That(authority.Check(sender, caster, null, selected)).IsEqualTo(SkillResult.Success);
        await Assert.That(authority.Check(sender, caster, null, unselected)).IsEqualTo(SkillResult.InvalidSkill);

        sender.Skills.Reset(AbilityType.Fight);

        await Assert.That(authority.Check(sender, caster, null, selected)).IsEqualTo(SkillResult.InvalidSkill);
    }

    [Test]
    public async Task SelectionChecksGroupOriginStepAndBudget()
    {
        var data = CreateData(HeirRows);
        var sender = CreateHeirCharacter(data);

        await Assert.That(sender.Skills.TryActivateHeirSkill(11, 502, false)).IsFalse();
        await Assert.That(sender.Skills.TryActivateHeirSkill(12, 507, false)).IsFalse();
        await Assert.That(sender.Skills.TryActivateHeirSkill(11, 505, false)).IsFalse();
        await Assert.That(sender.Skills.TryActivateHeirSkill(10, 502, true)).IsFalse();
        await Assert.That(sender.Skills.TryActivateHeirSkill(10, 502, false)).IsTrue();
        await Assert.That(sender.Skills.TryActivateHeirSkill(10, 503, false)).IsFalse();
        sender.Skills.AddSkill(new SkillTemplate { Id = 506, AbilityId = AbilityType.Fight, AbilityLevel = 1 }, 1, false);
        await Assert.That(sender.Skills.TryActivateHeirSkill(12, 507, false)).IsFalse();
    }

    [Test]
    public async Task ComboFollowUpRequiresAnActiveGrantFromThePriorSkill()
    {
        var sender = CreateCharacter();
        var source = new SkillTemplate { Id = 501, Name = "Combo", AbilityId = AbilityType.Fight, AbilityLevel = 1 };
        sender.Skills.AddSkill(source, 1, false);
        var followUp = new SkillTemplate { Id = 502, Name = "Combo", AbilityId = AbilityType.Fight, AbilityLevel = 1 };
        var caster = new SkillCasterUnit(sender.ObjId);
        var authority = CreateAuthority("""
            INSERT INTO special_effects VALUES (30, 48, 502);
            INSERT INTO effects VALUES (31, 'SpecialEffect', 30);
            INSERT INTO skill_effects VALUES (501, 31);
            """);

        await Assert.That(authority.Check(sender, caster, null, followUp)).IsEqualTo(SkillResult.InvalidSkill);
        new Combo().Execute(sender, caster, sender, null, null, new Skill(source), null,
            DateTime.UtcNow, 502, 1000, 0, 0);

        await Assert.That(authority.Check(sender, caster, null, followUp)).IsEqualTo(SkillResult.Success);
    }

    [Test]
    public async Task FlameboltPlotManaCostAuthorizesEachSuccessiveAttack()
    {
        var sender = CreateCharacter();
        var caster = new SkillCasterUnit(sender.ObjId);
        var authority = CreateAuthority(FlameboltComboRows);
        var first = FlameboltStage(10752, 24894);
        var second = FlameboltStage(24894, 24895);
        var third = new SkillTemplate { Id = 24895, AbilityId = AbilityType.Magic, PlotOnly = true };
        sender.Skills.AddSkill(first, 1, false);

        await Assert.That(authority.Check(sender, caster, null, second)).IsEqualTo(SkillResult.InvalidSkill);
        var firstCast = new Skill(first);
        ApplyFlameboltManaCost(sender, caster, firstCast, 280, 2346, 16);
        var secondAfterFirst = authority.Check(sender, caster, null, second);
        var thirdAfterFirst = authority.Check(sender, caster, null, third);

        var secondCast = new Skill(second);
        ApplyFlameboltManaCost(sender, caster, secondCast, 1454, 11559, 10);
        var thirdAfterSecond = authority.Check(sender, caster, null, third);

        await Assert.That(secondAfterFirst).IsEqualTo(SkillResult.Success);
        await Assert.That(thirdAfterFirst).IsEqualTo(SkillResult.InvalidSkill);
        await Assert.That(thirdAfterSecond).IsEqualTo(SkillResult.Success);
    }

    [Test]
    [Arguments(true, true)]
    [Arguments(false, false)]
    public async Task PlotManaCostGrantsNoComboForCancelledOrNonPlotOnlySkills(bool plotOnly, bool cancelled)
    {
        var sender = CreateCharacter();
        var caster = new SkillCasterUnit(sender.ObjId);
        var target = new SkillCastUnitTarget(sender.ObjId);
        var authority = CreateAuthority(FlameboltComboRows);
        var first = FlameboltStage(10752, 24894);
        first.PlotOnly = plotOnly;
        var followUp = FlameboltStage(24894, 24895);
        var cast = new Skill(first);
        cast.ActivePlotState = new PlotState(sender, caster, sender, target, null, cast);
        if (cancelled)
            cast.ActivePlotState.RequestCancellation();

        ApplyFlameboltManaCost(sender, caster, cast, 280, 2346, 16);

        await Assert.That(authority.Check(sender, caster, null, followUp)).IsEqualTo(SkillResult.InvalidSkill);
    }

    private const string FlameboltComboRows = """
        INSERT INTO special_effects VALUES (23309, 48, 24894), (15763, 48, 24895);
        INSERT INTO effects VALUES (55077, 'SpecialEffect', 23309), (36559, 'SpecialEffect', 15763);
        INSERT INTO skill_effects VALUES (10752, 55077), (24894, 36559);
        """;

    private static SkillTemplate FlameboltStage(uint id, int followUp) => new()
    {
        Id = id,
        Name = "Flamebolt",
        AbilityId = AbilityType.Magic,
        AbilityLevel = 1,
        PlotOnly = true,
        Effects =
        [
            new SkillEffect
            {
                Template = new SpecialEffect { SpecialEffectTypeId = SpecialType.Combo, Value1 = followUp, Value2 = 1000 }
            }
        ]
    };

    private static void ApplyFlameboltManaCost(Character sender, SkillCasterUnit caster, Skill cast, uint plotId, uint eventId, int mana)
    {
        var effect = new SpecialEffect { SpecialEffectTypeId = SpecialType.ManaCost, Value1 = mana };
        effect.Apply(sender, caster, sender, new SkillCastUnitTarget(sender.ObjId),
            new CastPlot(plotId, 1, eventId, cast.Id), new EffectSource(cast), null, DateTime.UtcNow);
    }

    [Test]
    public async Task CurrentDoodadPhaseOffersItsQuestAction()
    {
        var sender = CreateCharacter();
        var world = new WorldInstance(new WorldTemplate(), 0, true, sender.Transform.InstanceId);
        sender.ParentWorld = world;
        var doodad = new Doodad { ObjId = 10, ParentWorld = world };
        world.AddObject(doodad);
        doodad.CurrentFuncs.Add(new DoodadFunc { SkillId = 601, FuncType = "DoodadFuncQuest" });
        var senderCaster = new SkillCasterUnit(sender.ObjId);
        var target = new SkillCastDoodadTarget { ObjId = doodad.ObjId, Type = SkillCastTargetType.Doodad };
        var authority = CreateAuthority();

        var result = authority.Check(sender, senderCaster, target, new SkillTemplate { Id = 601, TargetType = SkillTargetType.Doodad });

        await Assert.That(result).IsEqualTo(SkillResult.Success);
        await Assert.That(authority.Check(sender, senderCaster, target,
            new SkillTemplate { Id = 602, TargetType = SkillTargetType.Doodad })).IsEqualTo(SkillResult.InvalidSkill);
    }

    [Test]
    public async Task OwnedQuestItemCanCastItsUseSkill()
    {
        var sender = CreateCharacter();
        var item = CreateItem(sender, new ItemTemplate { UseSkillId = 701, BindType = ItemBindType.BindOnPickup });

        var authority = CreateAuthority();

        await Assert.That(authority.CheckItem(sender, item, 701)).IsEqualTo(SkillResult.Success);
        await Assert.That(authority.CheckItem(sender, item, 702)).IsEqualTo(SkillResult.InvalidSkill);
    }

    [Test]
    public async Task TerritoryEmberCopiesShareTheirCharactersRecallCooldown()
    {
        var sender = CreateCharacter();
        var authority = CreateAuthority();
        var template = new MemoryEmberTemplate
        {
            Id = TerritoryMemoryEmber.ItemId,
            UseSkillId = TerritoryMemoryEmber.ReturnSkillId
        };
        var first = new MemoryEmber(15, template, 1) { OwnerId = sender.Id, SlotType = SlotType.Inventory };
        var second = new MemoryEmber(16, template, 1) { OwnerId = sender.Id, SlotType = SlotType.Inventory };

        await Assert.That(authority.CheckItem(sender, first, template.UseSkillId)).IsEqualTo(SkillResult.Success);
        sender.Cooldowns.AddCooldown(template.UseSkillId, 14_400_000);

        await Assert.That(authority.CheckItem(sender, first, template.UseSkillId)).IsEqualTo(SkillResult.CooldownTime);
        await Assert.That(authority.CheckItem(sender, second, template.UseSkillId)).IsEqualTo(SkillResult.CooldownTime);
    }

    [Test]
    public async Task PortalBookCanSaveARecallPoint()
    {
        var sender = CreateCharacter();
        var item = CreateItem(sender, new ItemTemplate { ImplId = ItemImplEnum.Portal, UseSkillId = 11216 });

        var result = CreateAuthority().CheckItem(sender, item, 11215);

        await Assert.That(result).IsEqualTo(SkillResult.Success);
    }

    [Test]
    public async Task OwningAnItemInStorageDoesNotMakeItACastSource()
    {
        var sender = CreateCharacter();
        var item = CreateItem(sender, new ItemTemplate { UseSkillId = 701 });
        item.SlotType = SlotType.Bank;

        var result = CreateAuthority().CheckItem(sender, item, 701);

        await Assert.That(result).IsEqualTo(SkillResult.InvalidSource);
    }

    [Test]
    public async Task ItemUsesTheHighestAuthoredSkillWithinItsGrade()
    {
        var sender = CreateCharacter();
        var item = CreateItem(sender, new ItemTemplate { UseSkillId = 701 });
        item.TemplateId = 56;
        item.Grade = 4;
        var authority = CreateAuthority("""
            INSERT INTO item_grades VALUES (1, 1), (4, 4), (6, 6);
            INSERT INTO item_grade_skills VALUES (56, 1, 702), (56, 6, 703);
            """);

        await Assert.That(authority.CheckItem(sender, item, 702)).IsEqualTo(SkillResult.Success);
        await Assert.That(authority.CheckItem(sender, item, 703)).IsEqualTo(SkillResult.InvalidSkill);
    }

    [Test]
    public async Task SlaveSkillRequiresItsAuthoredSeatOnTheCastingUnit()
    {
        var sender = CreateCharacter();
        var slave = new Slave { ObjId = 3, TemplateId = 13 };
        var authority = CreateAuthority("""
            INSERT INTO mount_skills VALUES (30, 801, 1);
            INSERT INTO slave_mount_skills VALUES (13, 30);
            INSERT INTO mount_attached_skills VALUES (30, 1, 0);
            """);
        var caster = new SkillCasterMount(slave.ObjId) { MountSkillTemplateId = 30 };
        var skill = new SkillTemplate { Id = 801 };

        slave.AttachedCharacters[AttachPointKind.Passenger0] = sender;
        await Assert.That(authority.Check(sender, caster, null, skill, slave)).IsEqualTo(SkillResult.NoPerm);
        slave.AttachedCharacters.Clear();
        slave.AttachedCharacters[AttachPointKind.Driver] = sender;

        await Assert.That(authority.Check(sender, caster, null, skill, slave)).IsEqualTo(SkillResult.Success);
    }

    [Test]
    public async Task UnseatedPetSkillIsAvailableToItsOwner()
    {
        var sender = CreateCharacter();
        var pet = new Mate { ObjId = 3, TemplateId = 13, OwnerId = sender.Id };
        var authority = CreateAuthority("""
            INSERT INTO mount_skills VALUES (30, 801, 0);
            INSERT INTO npc_mount_skills VALUES (13, 30);
            """);
        var caster = new SkillCasterMount(pet.ObjId) { MountSkillTemplateId = 30 };
        var skill = new SkillTemplate { Id = 801 };

        await Assert.That(authority.Check(sender, caster, null, skill, pet)).IsEqualTo(SkillResult.Success);
        await Assert.That(authority.Check(CreateCharacter(2), caster, null, skill, pet)).IsEqualTo(SkillResult.NoPerm);
    }

    private static Character CreateCharacter(uint id = 1)
    {
        var sender = new Character(null) { Id = id, ObjId = id };
        sender.Skills = new CharacterSkills(sender);
        return sender;
    }

    private static Item CreateItem(Character sender, ItemTemplate template) => new()
    {
        Id = 15,
        OwnerId = sender.Id,
        SlotType = SlotType.Inventory,
        Template = template
    };

    private const string HeirRows = """
        INSERT INTO heir_skills VALUES (10, 501, 1), (11, 504, 2), (12, 506, 1);
        INSERT INTO heir_skill_details VALUES (10, 502, 1), (10, 503, 1), (11, 505, 1), (12, 507, 1);
        INSERT INTO skills VALUES (502, 1, 0, 1, 0), (503, 1, 0, 1, 0), (505, 1, 0, 1, 0), (507, 1, 0, 1, 0);
        """;

    private static Character CreateHeirCharacter(SkillAuthorityGameData data)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE heir_levels (id, level, req_item_count, req_item_id, req_total_exp, step);
            INSERT INTO heir_levels VALUES (1, 0, 8, 8001964, 3777158, 0), (2, 1, 10, 8001964, 5309104, 1);
            """;
        command.ExecuteNonQuery();

        var experience = new ExperienceManager();
        experience.Load(new TestLevels(), 55, 50);
        experience.LoadHeirLevels(connection);
        var sender = new Character(null) { Id = 1, ObjId = 1, Level = 55, HeirLevel = 1, Ability1 = AbilityType.Fight };
        sender.Abilities = new CharacterAbilities(sender);
        sender.Abilities.Abilities[AbilityType.Fight].Exp = 55000;
        sender.Skills = new CharacterSkills(sender, data, experience);
        sender.Skills.AddSkill(new SkillTemplate { Id = 501, AbilityId = AbilityType.Fight, AbilityLevel = 1 }, 1, false);
        sender.Skills.AddSkill(new SkillTemplate { Id = 504, AbilityId = AbilityType.Fight, AbilityLevel = 1 }, 1, false);
        return sender;
    }

    private sealed class TestLevels : IExperienceLevelTemplateLoader
    {
        public IEnumerable<ExperienceLevelTemplate> Load()
        {
            for (var i = 1; i <= 55; i++)
                yield return new ExperienceLevelTemplate { TotalExp = i * 1000 };
        }
    }

    private static SkillAuthority CreateAuthority(string rows = "") => new(CreateData(rows));

    private static SkillAuthorityGameData CreateData(string rows)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE slash_commands (skill_id);
            CREATE TABLE npc_interactions (npc_interaction_set_id, skill_id);
            CREATE TABLE slave_interaction_skills (slave_id, skill_id);
            CREATE TABLE mount_skills (id, skill_id, check_attach_point);
            CREATE TABLE mount_attached_skills (mount_skill_id, attach_point_id, skill_id);
            CREATE TABLE npc_mount_skills (npc_id, mount_skill_id);
            CREATE TABLE slave_mount_skills (slave_id, mount_skill_id);
            CREATE TABLE buff_mount_skills (buff_id, mount_skill_id);
            CREATE TABLE buff_skills (buff_id, skill_id);
            CREATE TABLE item_grade_skills (item_id, item_grade_id, skill_id);
            CREATE TABLE item_grades (id, grade_order);
            CREATE TABLE heir_skills (id, skill_id, step);
            CREATE TABLE heir_skill_details (heir_skill_id, skill_id, point);
            CREATE TABLE skills (id, ability_id, high_ability_id, ability_level, level_step);
            CREATE TABLE buff_swap_skills (buff_id, origin_skill_id, new_skill_id);
            CREATE TABLE housing_rebuildings (skill_id);
            CREATE TABLE special_effects (id, special_effect_type_id, value1);
            CREATE TABLE effects (id, actual_type, actual_id);
            CREATE TABLE skill_effects (skill_id, effect_id);
            """;
        command.ExecuteNonQuery();
        command.CommandText = rows;
        if (rows.Length > 0)
            command.ExecuteNonQuery();

        var data = new SkillAuthorityGameData();
        data.Load(connection, connection);
        return data;
    }
}
