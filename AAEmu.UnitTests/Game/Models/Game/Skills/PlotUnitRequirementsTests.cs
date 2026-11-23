using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Units;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

[NotInParallel]
public class PlotUnitRequirementsTests
{
    [Test]
    public async Task PlotConditionUsesItsOwnRequirementsAndTheirCombinationRule()
    {
        using var db = new SqliteConnection("Data Source=:memory:");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            CREATE TABLE unit_reqs (owner_id, owner_type, kind_id, value1, value2, value3, display_msg);
            INSERT INTO unit_reqs VALUES
                (100, 'PlotCondition', 1, 10, 15, 0, 0),
                (100, 'PlotCondition', 1, 18, 25, 0, 0);
            """;
        command.ExecuteNonQuery();
        var requirements = new UnitRequirementsGameData();
        requirements.Load(db, db);
        var singleton = typeof(Singleton<UnitRequirementsGameData>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = singleton.GetValue(null);
        singleton.SetValue(null, requirements);
        try
        {
            var caster = new Unit { Level = 20 };
            var condition = new PlotCondition { Id = 100, Kind = PlotConditionType.UnitReqs };
            await Assert.That(condition.Check(caster, null, caster, null, null, null)).IsFalse();
            condition.OrUnitReqs = true;
            await Assert.That(condition.Check(caster, null, caster, null, null, null)).IsTrue();
            caster.Level = 5;
            await Assert.That(condition.Check(caster, null, caster, null, null, null)).IsFalse();
            condition.NotCondition = true;
            await Assert.That(condition.Check(caster, null, caster, null, null, null)).IsTrue();
            condition.Id = 101;
            condition.NotCondition = false;
            await Assert.That(condition.Check(caster, null, caster, null, null, null)).IsTrue();
        }
        finally
        {
            singleton.SetValue(null, previous);
        }
    }
}
