using System.Security.Cryptography;
using System.Text;

using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Commons.Utils.Updater;

public static class MySqlSchemaBootstrap
{
    private const int UnknownDatabase = 1049;
    private const string BootstrapRecord = "aaemu/schema-bootstrap";
    private const string ResourcePrefix = "AAEmu.MySql.";
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Opens a startup connection and installs the bundled schema when needed.
    /// Disposing the connection releases the server-side lock for this schema.
    /// </summary>
    public static MySqlConnection Open(string connectionString, string moduleName)
    {
        if (moduleName is not ("aaemu_game" or "aaemu_login"))
            throw new ArgumentOutOfRangeException(nameof(moduleName));

        var builder = new MySqlConnectionStringBuilder(connectionString);
        var database = builder.Database;
        if (string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("Configure a MySQL database name before starting the server.");

        builder.Database = string.Empty;
        builder.Pooling = false;
        var connection = new MySqlConnection(builder.ConnectionString);
        try
        {
            connection.Open();
            AcquireLock(connection, database);
            SelectDatabase(connection, database);
            if (NeedsBootstrap(connection))
                InstallSchema(connection, moduleName, database);

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void AcquireLock(MySqlConnection connection, string database)
    {
        var lockName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"aaemu-schema:{database.ToUpperInvariant()}")));
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT GET_LOCK(@name, 60)";
        command.CommandTimeout = 65;
        command.Parameters.AddWithValue("@name", lockName);
        if (command.ExecuteScalar() is not 1L and not 1)
            throw new InvalidOperationException($"Could not acquire the startup lock for MySQL database '{database}'.");
    }

    private static void SelectDatabase(MySqlConnection connection, string database)
    {
        try
        {
            connection.ChangeDatabase(database);
        }
        catch (MySqlException exception) when (exception.Number == UnknownDatabase)
        {
            var identifier = database.Replace("`", "``", StringComparison.Ordinal);
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{identifier}`";
            command.ExecuteNonQuery();
            connection.ChangeDatabase(database);
        }
    }

    private static bool NeedsBootstrap(MySqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*), COUNT(CASE WHEN TABLE_NAME = 'updates' THEN 1 END)
            FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE()
            """;
        long tableCount;
        using (var reader = command.ExecuteReader())
        {
            reader.Read();
            tableCount = reader.GetInt64(0);
            if (tableCount == 0)
                return true;
            if (reader.GetInt64(1) == 0)
                return false;
        }

        command.CommandText = "SELECT installed FROM `updates` WHERE script_name = @script";
        command.Parameters.AddWithValue("@script", BootstrapRecord);
        var state = command.ExecuteScalar();
        if (state != null)
            return Convert.ToInt32(state) == 0;
        if (tableCount != 1)
            return false;

        // A previous start can stop after creating the journal and before writing its first record.
        command.CommandText = "SELECT COUNT(*) FROM `updates`";
        command.Parameters.Clear();
        return Convert.ToInt64(command.ExecuteScalar()) == 0;
    }

    private static void InstallSchema(MySqlConnection connection, string moduleName, string database)
    {
        using var schema = OpenResource($"{moduleName}.sql");
        // The manual import uses the first two lines to create and select its default database.
        if (schema.ReadLine()?.StartsWith("CREATE DATABASE ", StringComparison.Ordinal) != true ||
            schema.ReadLine()?.StartsWith("USE ", StringComparison.Ordinal) != true)
            throw new InvalidOperationException($"Invalid MySQL schema header for '{moduleName}'.");

        Logger.Info("Installing the initial MySQL schema in '{0}'.", database);
        MySqlDatabaseUpdater.CreateUpdatesTable(connection);
        WriteRecord(connection, BootstrapRecord, installed: false);

        using (var command = connection.CreateCommand())
        {
            command.CommandText = schema.ReadToEnd();
            command.ExecuteNonQuery();
        }

        using var baseline = OpenResource("mysql-bootstrap-baseline.txt");
        var moduleToken = $"_{moduleName}";
        while (baseline.ReadLine() is { } fileName)
        {
            if (fileName.Contains(moduleToken, StringComparison.Ordinal))
                WriteRecord(connection, fileName, installed: true);
        }

        // DDL commits implicitly. Keep the pending record until both the schema and its journal are ready.
        WriteRecord(connection, BootstrapRecord, installed: true);
        Logger.Info("Initial MySQL schema installed in '{0}'.", database);
    }

    private static StreamReader OpenResource(string name)
    {
        var stream = typeof(MySqlSchemaBootstrap).Assembly.GetManifestResourceStream(ResourcePrefix + name)
            ?? throw new InvalidOperationException($"The bundled MySQL resource '{name}' is missing.");
        return new StreamReader(stream);
    }

    private static void WriteRecord(MySqlConnection connection, string script, bool installed)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            REPLACE INTO `updates` (script_name, installed, install_date, last_error)
            VALUES (@script, @installed, UTC_TIMESTAMP(), '')
            """;
        command.Parameters.AddWithValue("@script", script);
        command.Parameters.AddWithValue("@installed", installed ? 1 : 0);
        command.ExecuteNonQuery();
    }
}
