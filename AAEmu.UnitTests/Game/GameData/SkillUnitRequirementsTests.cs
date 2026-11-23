using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.GameData;

public class SkillUnitRequirementsTests
{
    [Test]
    public async Task AlternativeRequirementsRejectWhenAllRowsFail()
    {
        var requirements = LoadRequirements();
        var skill = new SkillTemplate { Id = 100, OrUnitReqs = true };

        var result = requirements.CanUseSkill(skill, new Unit { Level = 5 }, null);

        await Assert.That(result.ResultKey).IsEqualTo(SkillResultKeys.skill_unit_reqs_or_fail);
    }

    [Test]
    public async Task AlternativeRequirementsAcceptWhenTheSecondRowPasses()
    {
        var requirements = LoadRequirements();
        var skill = new SkillTemplate { Id = 100, OrUnitReqs = true };

        var result = requirements.CanUseSkill(skill, new Unit { Level = 20 }, null);

        await Assert.That(result.ResultKey).IsEqualTo(SkillResultKeys.ok);
    }

    [Test]
    public async Task SkillWithoutRequirementsCanCastWhenThereAreNoSkillRows()
    {
        var requirements = LoadRequirements(withSkillRows: false);

        var result = requirements.CanUseSkill(new SkillTemplate { Id = 123 }, new Unit(), null);

        await Assert.That(result.ResultKey).IsEqualTo(SkillResultKeys.ok);
    }

    [Test]
    public async Task TargetRequirementsUseTheRequestedTargetEvenWithAnotherUnitSelected()
    {
        var selected = new Unit { ObjId = 2, IsInBattle = true };
        var aimed = new Unit { ObjId = 3 };
        var caster = new Unit { ObjId = 1, CurrentTarget = selected };
        var world = new WorldInstance(new WorldTemplate(), 0, true, caster.Transform.InstanceId);
        caster.ParentWorld = world;
        world.AddObject(selected);
        world.AddObject(aimed);
        var requirements = LoadRequirements();
        var skill = new SkillTemplate { Id = 101, OrUnitReqs = true, TargetType = SkillTargetType.AnyUnit };

        var result = requirements.CanUseSkill(skill, caster, new SkillCasterUnit(caster.ObjId), new SkillCastUnitTarget(aimed.ObjId));

        await Assert.That(result.ResultKey).IsEqualTo(SkillResultKeys.ok);
    }

    private static UnitRequirementsGameData LoadRequirements(bool withSkillRows = true)
    {
        using var db = new SqliteConnection("Data Source=:memory:");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "CREATE TABLE unit_reqs (owner_id, owner_type, kind_id, value1, value2, value3, display_msg)";
        command.ExecuteNonQuery();
        if (withSkillRows)
        {
            command.CommandText = """
                INSERT INTO unit_reqs VALUES
                (100, 'Skill', 1, 10, 15, 0, 1),
                (100, 'Skill', 1, 18, 25, 0, 1),
                (101, 'Skill', 17, 0, 0, 0, 1)
                """;
            command.ExecuteNonQuery();
        }

        var requirements = new UnitRequirementsGameData();
        requirements.Load(db, db);
        return requirements;
    }
}
