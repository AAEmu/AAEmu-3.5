using AAEmu.Game.Models.Game.Crafts;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class CraftManagerTests
{
    private static SqliteConnection CreateConnection()
    {
        var databasePath = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..",
            "AAEmu.Game", "Data", "compact.sqlite3"));
        var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        return connection;
    }

    [Test]
    public async Task CraftsSchema_HasExpectedColumns()
    {
        using var connection = CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(crafts)";
        using var reader = command.ExecuteReader();

        var columns = new HashSet<string>();
        while (reader.Read())
            columns.Add(reader.GetString(reader.GetOrdinal("name")));

        await Assert.That(columns).Contains("id");
        await Assert.That(columns).Contains("cast_delay");
        await Assert.That(columns).Contains("tool_id");
        await Assert.That(columns).Contains("skill_id");
        await Assert.That(columns).Contains("wi_id");
        await Assert.That(columns).Contains("req_doodad_id");
        await Assert.That(columns).Contains("need_bind");
        await Assert.That(columns).Contains("ac_id");
        await Assert.That(columns).Contains("actability_limit");
        await Assert.That(columns).Contains("show_upper_crafts");
        await Assert.That(columns).Contains("recommend_level");
        await Assert.That(columns).Contains("visible_order");
    }

    [Test]
    public async Task CraftClass_MapsCraftsTableColumns()
    {
        var type = typeof(Craft);
        await Assert.That(type.GetProperty("Id")).IsNotNull();
        await Assert.That(type.GetProperty("CastDelay")).IsNotNull();
        await Assert.That(type.GetProperty("ToolId")).IsNotNull();
        await Assert.That(type.GetProperty("SkillId")).IsNotNull();
        await Assert.That(type.GetProperty("WiId")).IsNotNull();
        await Assert.That(type.GetProperty("ReqDoodadId")).IsNotNull();
        await Assert.That(type.GetProperty("NeedBind")).IsNotNull();
        await Assert.That(type.GetProperty("AcId")).IsNotNull();
        await Assert.That(type.GetProperty("ActabilityLimit")).IsNotNull();
        await Assert.That(type.GetProperty("ShowUpperCraft")).IsNotNull();
        await Assert.That(type.GetProperty("RecommendLevel")).IsNotNull();
        await Assert.That(type.GetProperty("VisibleOrder")).IsNotNull();
    }

    [Test]
    public async Task CraftTables_HaveNonZeroRows()
    {
        using var connection = CreateConnection();
        foreach (var table in new[] { "crafts", "craft_products", "craft_materials", "craft_pack_crafts" })
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table}";
            var count = (long)(command.ExecuteScalar() ?? 0L);
            await Assert.That(count).IsGreaterThan(0).Because($"Table {table} should have rows");
        }
    }
}
