using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.Utils.DB;

internal static class CompactDatabasePatcher
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    internal static void Apply(string appPath)
    {
        var directory = Path.Combine(appPath, "SQL", "patches", "compact", "startup");
        if (!Directory.Exists(directory))
            return;

        var patches = Directory.GetFiles(directory, "*.sql");
        if (patches.Length == 0)
            return;

        Array.Sort(patches, StringComparer.Ordinal);
        var database = Path.Combine(appPath, "Data", "compact.sqlite3");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                                  CREATE TABLE IF NOT EXISTS sql_patches_applied (
                                      patch_name TEXT NOT NULL PRIMARY KEY,
                                      applied_at TEXT NOT NULL
                                  );
                                  """;
            command.ExecuteNonQuery();
        }

        foreach (var patch in patches)
        {
            var name = Path.GetFileName(patch);
            try
            {
                // Acquire the write lock before checking the journal so concurrent startups cannot run a patch twice.
                using var transaction = connection.BeginTransaction(deferred: false);
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT 1 FROM sql_patches_applied WHERE patch_name = $name;";
                command.Parameters.AddWithValue("$name", name);
                if (command.ExecuteScalar() != null)
                    continue;

                command.Parameters.Clear();
                command.CommandText = File.ReadAllText(patch);
                command.ExecuteNonQuery();

                command.CommandText = "INSERT INTO sql_patches_applied (patch_name, applied_at) VALUES ($name, CURRENT_TIMESTAMP);";
                command.Parameters.AddWithValue("$name", name);
                command.ExecuteNonQuery();
                transaction.Commit();
                Logger.Info("Applied compact.sqlite3 patch: {0}", name);
            }
            catch (SqliteException ex)
            {
                throw new InvalidOperationException($"Failed to apply compact.sqlite3 patch '{name}' to '{database}': {ex.Message}", ex);
            }
        }
    }
}
