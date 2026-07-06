using AAEmu.Game.Models.Game.Animation;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class AnimationManagerTests
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
    public async Task AnimsSchema_HasExpectedColumns()
    {
        using var connection = CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(anims)";
        using var reader = command.ExecuteReader();

        var columns = new Dictionary<string, string>();
        while (reader.Read())
        {
            var name = reader.GetString(reader.GetOrdinal("name"));
            var type = reader.GetString(reader.GetOrdinal("type"));
            columns[name] = type;
        }

        await Assert.That(columns).ContainsKey("id");
        await Assert.That(columns).ContainsKey("category_id");
        await Assert.That(columns).ContainsKey("hang_ub");
        await Assert.That(columns).ContainsKey("loop");
        await Assert.That(columns).ContainsKey("move_ub");
        await Assert.That(columns).ContainsKey("name");
        await Assert.That(columns).ContainsKey("relaxed_ub");
        await Assert.That(columns).ContainsKey("ride_ub");
        await Assert.That(columns).ContainsKey("swim_move_ub");
        await Assert.That(columns).ContainsKey("swim_ub");
    }

    [Test]
    public async Task AnimClass_MapsAllAnimTableColumns()
    {
        var animType = typeof(Anim);
        await Assert.That(animType.GetProperty("Id")).IsNotNull();
        await Assert.That(animType.GetProperty("Category")).IsNotNull();
        await Assert.That(animType.GetProperty("HangUB")).IsNotNull();
        await Assert.That(animType.GetProperty("Loop")).IsNotNull();
        await Assert.That(animType.GetProperty("MoveUB")).IsNotNull();
        await Assert.That(animType.GetProperty("Name")).IsNotNull();
        await Assert.That(animType.GetProperty("RelaxedUB")).IsNotNull();
        await Assert.That(animType.GetProperty("RideUB")).IsNotNull();
        await Assert.That(animType.GetProperty("SwimMoveUB")).IsNotNull();
        await Assert.That(animType.GetProperty("SwimUB")).IsNotNull();
    }

    [Test]
    public async Task AnimsTable_HasNonZeroRows()
    {
        using var connection = CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM anims";
        var count = (long)(command.ExecuteScalar() ?? 0L);
        await Assert.That(count).IsGreaterThan(0);
    }
}
