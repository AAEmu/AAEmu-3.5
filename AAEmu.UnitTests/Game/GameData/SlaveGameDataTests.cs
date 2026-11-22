using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Units;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.GameData;

public class SlaveGameDataTests
{
    [Test]
    public async Task ShipDriveLimitsReadTheCompactBoundsForControllerAttributes()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE unit_attribute_limits (unit_attribute_id INT PRIMARY KEY, minimum INT, maximum INT);
            INSERT INTO unit_attribute_limits VALUES (10, -5000, 7500), (13, 0, 3000), (72, 0, 250), (193, -200, 200), (0, 0, 40);
            """;
        command.ExecuteNonQuery();

        var limits = SlaveGameData.ReadShipDriveAttributeLimits(connection);

        await Assert.That(limits[UnitAttribute.MoveSpeedMul]).IsEqualTo((-5000, 7500));
        await Assert.That(limits[UnitAttribute.Facets]).IsEqualTo((0, 3000));
        await Assert.That(limits[UnitAttribute.TurnSpeed]).IsEqualTo((0, 250));
        await Assert.That(limits[UnitAttribute.ReverseVelocityMul]).IsEqualTo((-200, 200));
        await Assert.That(limits.ContainsKey(UnitAttribute.Str)).IsFalse();
    }

    [Test]
    public async Task OldCompactWithoutAttributeLimitsUsesTheNativeDefaultRange()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var limits = SlaveGameData.ReadShipDriveAttributeLimits(connection);

        await Assert.That(limits).IsEmpty();
    }

    [Test]
    [Arguments(0.375f)]
    [Arguments(0f)]
    public async Task ShipMassReduction_UsesTheAuthoredConstant(float value)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE constants (id INT PRIMARY KEY, value REAL); INSERT INTO constants VALUES (19, 9), (24, $value);";
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();

        await Assert.That(SlaveGameData.ReadShipMassReductionPerSecond(connection)).IsEqualTo(value);
    }

    [Test]
    public async Task ShipMassReduction_UsesTheStock35RateForDatabasesWithoutConstants()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        await Assert.That(SlaveGameData.ReadShipMassReductionPerSecond(connection)).IsEqualTo(1f);
    }
}
