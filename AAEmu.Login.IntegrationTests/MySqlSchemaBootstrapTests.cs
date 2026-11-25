using AAEmu.Commons.Utils.Updater;
using MySql.Data.MySqlClient;
using Xunit;

namespace AAEmu.Login.IntegrationTests;

public class MySqlSchemaBootstrapTests
{
    private static string? TestConnectionString => Environment.GetEnvironmentVariable("AAEMU_MYSQL_TEST_CONNECTION");
    public static bool HasTestDatabase => !string.IsNullOrWhiteSpace(TestConnectionString);

    [Theory(Skip = "Set AAEMU_MYSQL_TEST_CONNECTION to a disposable MySQL instance with schema creation rights.", SkipUnless = nameof(HasTestDatabase))]
    [InlineData("aaemu_login", "users", "2026-03-15_aaemu_login_user_2fa.sql")]
    [InlineData("aaemu_game", "characters", "2026-11-23_aaemu_game_character_buff_caster.sql")]
    public void MissingSchemaIsCreatedUnderTheConfiguredName(string module, string table, string update)
    {
        using var database = new TestDatabase();

        using var connection = MySqlSchemaBootstrap.Open(database.ConnectionString, module);

        Assert.Equal(database.Name, Execute(connection, "SELECT DATABASE()"));
        Assert.Equal(0L, Execute(connection, $"SELECT COUNT(*) FROM `{table}`"));
        Assert.Equal(1L, Execute(connection, $"SELECT COUNT(*) FROM `updates` WHERE script_name = '{update}' AND installed = 1"));
    }

    [Fact(Skip = "Set AAEMU_MYSQL_TEST_CONNECTION to a disposable MySQL instance with schema creation rights.", SkipUnless = nameof(HasTestDatabase))]
    public void ExistingSchemaWithDataIsLeftForTheUpdater()
    {
        using var database = new TestDatabase();
        database.Create();
        using var existingConnection = new MySqlConnection(database.ConnectionString);
        existingConnection.Open();
        Execute(existingConnection, "CREATE TABLE `users` (`id` INT PRIMARY KEY); INSERT INTO `users` VALUES (7)");

        using var connection = MySqlSchemaBootstrap.Open(database.ConnectionString, "aaemu_login");

        Assert.Equal(7, Execute(connection, "SELECT id FROM `users`"));
        Assert.Equal(1L, Execute(connection, "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()"));
    }

    [Fact(Skip = "Set AAEMU_MYSQL_TEST_CONNECTION to a disposable MySQL instance with schema creation rights.", SkipUnless = nameof(HasTestDatabase))]
    public async Task ConcurrentStartsInitializeAnEmptySchemaOnce()
    {
        using var database = new TestDatabase();
        database.Create();

        var starts = new Task<object>[2];
        for (var i = 0; i < starts.Length; i++)
        {
            starts[i] = Task.Run(() =>
            {
                using var connection = MySqlSchemaBootstrap.Open(database.ConnectionString, "aaemu_game");
                return Execute(connection, "SELECT COUNT(*) FROM `housings`");
            }, TestContext.Current.CancellationToken);
        }

        Assert.All(await Task.WhenAll(starts), count => Assert.Equal(12L, count));
    }

    [Fact(Skip = "Set AAEMU_MYSQL_TEST_CONNECTION to a disposable MySQL instance with schema creation rights.", SkipUnless = nameof(HasTestDatabase))]
    public void PendingBootstrapIsResumedAfterPartialSchemaCreation()
    {
        using var database = new TestDatabase();
        using (var connection = MySqlSchemaBootstrap.Open(database.ConnectionString, "aaemu_login"))
        {
            Execute(connection, """
                DROP TABLE `user_2fa`;
                DELETE FROM `updates` WHERE script_name = '2026-03-15_aaemu_login_user_2fa.sql';
                UPDATE `updates` SET installed = 0 WHERE script_name = 'aaemu/schema-bootstrap';
                """);
        }

        using var resumed = MySqlSchemaBootstrap.Open(database.ConnectionString, "aaemu_login");

        Assert.Equal(0L, Execute(resumed, "SELECT COUNT(*) FROM `user_2fa`"));
        Assert.Equal(1L, Execute(resumed, "SELECT COUNT(*) FROM `updates` WHERE script_name = '2026-03-15_aaemu_login_user_2fa.sql' AND installed = 1"));
    }

    private static object Execute(MySqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()!;
    }

    private sealed class TestDatabase : IDisposable
    {
        private readonly string _serverConnectionString;

        public TestDatabase()
        {
            var builder = new MySqlConnectionStringBuilder(TestConnectionString)
            {
                Database = string.Empty,
                Pooling = false
            };
            _serverConnectionString = builder.ConnectionString;
            builder.Database = Name;
            ConnectionString = builder.ConnectionString;
        }

        public string Name { get; } = $"aac-bootstrap-{Guid.NewGuid():N}";
        public string ConnectionString { get; }

        public void Create()
        {
            using var connection = new MySqlConnection(_serverConnectionString);
            connection.Open();
            Execute(connection, $"CREATE DATABASE `{Name}`");
        }

        public void Dispose()
        {
            using var connection = new MySqlConnection(_serverConnectionString);
            connection.Open();
            Execute(connection, $"DROP DATABASE IF EXISTS `{Name}`");
        }
    }
}
