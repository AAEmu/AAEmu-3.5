using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Utils.DB;

public class CompactDatabasePatcherTests
{
    [Test]
    public async Task Apply_UsesFilenameOrderAndTracksEachDatabaseSeparately()
    {
        using var files = new PatchFiles();
        files.Execute("CREATE TABLE test_data (value TEXT NOT NULL)");
        files.WritePatch("002-update.sql", "UPDATE test_data SET value = value || ';second';");
        files.WritePatch("001-insert.sql", "INSERT INTO test_data VALUES ('first');");
        File.WriteAllText(Path.Combine(files.PatchDirectory, "..", "manual.sql"), "invalid SQL");

        CompactDatabasePatcher.Apply(files.Root);
        CompactDatabasePatcher.Apply(files.Root);

        await Assert.That((long)files.Scalar("SELECT count(*) FROM test_data")).IsEqualTo(1L);
        await Assert.That((string)files.Scalar("SELECT value FROM test_data")).IsEqualTo("first;second");

        File.Move(files.Database, files.Database + ".old");
        files.Execute("CREATE TABLE test_data (value TEXT NOT NULL)");
        CompactDatabasePatcher.Apply(files.Root);

        await Assert.That((string)files.Scalar("SELECT value FROM test_data")).IsEqualTo("first;second");
    }

    [Test]
    public async Task Apply_RollsBackFailedPatchAndRetriesItNextTime()
    {
        using var files = new PatchFiles();
        files.Execute("CREATE TABLE test_data (value INTEGER NOT NULL)");
        files.WritePatch("001-success.sql", "INSERT INTO test_data VALUES (1);");
        files.WritePatch("002-failure.sql", "INSERT INTO test_data VALUES (2); INSERT INTO missing_table VALUES (3);");
        files.WritePatch("003-next.sql", "INSERT INTO test_data VALUES (4);");

        var exception = Assert.Throws<InvalidOperationException>(() => CompactDatabasePatcher.Apply(files.Root));

        await Assert.That(exception.Message).Contains("002-failure.sql");
        await Assert.That((long)files.Scalar("SELECT sum(value) FROM test_data")).IsEqualTo(1L);

        files.WritePatch("002-failure.sql", "INSERT INTO test_data VALUES (2);");
        CompactDatabasePatcher.Apply(files.Root);

        await Assert.That((long)files.Scalar("SELECT sum(value) FROM test_data")).IsEqualTo(7L);
    }

    [Test]
    public async Task Apply_FixesStartingSkillsUsingThePackagedPatch()
    {
        using var files = new PatchFiles();
        files.Execute("""
                      CREATE TABLE characters (id INTEGER, char_race_id INTEGER);
                      CREATE TABLE default_skills (id INTEGER, skill_id INTEGER);
                      CREATE TABLE character_default_skills (character_id INTEGER, default_skill_id INTEGER);
                      INSERT INTO characters VALUES (1, 1), (2, 1), (3, 3), (13, 8);
                      INSERT INTO default_skills VALUES (161, 35420);
                      INSERT INTO character_default_skills VALUES
                          (1, 165), (1, 167), (2, 165), (2, 167), (3, 168), (3, 169), (13, 162), (13, 170);
                      """);
        const string patchName = "2026-11-21-starting-action-skills.sql";
        File.Copy(Path.Combine(AppContext.BaseDirectory, "SQL", "patches", "compact", "startup", patchName),
            Path.Combine(files.PatchDirectory, patchName));

        CompactDatabasePatcher.Apply(files.Root);

        var links = files.Scalar("""
                                 SELECT group_concat(character_id || ':' || default_skill_id, ',')
                                 FROM (SELECT * FROM character_default_skills ORDER BY character_id, default_skill_id);
                                 """);
        await Assert.That((string)links).IsEqualTo("1:161,1:165,2:161,2:165,3:169,13:170");
    }

    [Test]
    public async Task CampfirePatchRestoresTheAuthoredCrowdBuffAndTag()
    {
        using var files = new PatchFiles();
        files.Execute("""
                      CREATE TABLE buffs (
                        id INT PRIMARY KEY, name TEXT, "desc" TEXT, duration INT, tick INT,
                        icon_id INT, fx_group_id INT, kind_id INT, system INT, active_weapon_id INT,
                        stack_rule_id INT, max_stack INT, save_rule_id INT, remove_on_death INT, remove_on_exempt INT,
                        crowd_friendly INT, crowd_hostile INT, crowd_number INT, scale REAL, scaleDuration REAL,
                        custom_dual_material_fade_time REAL, reflection_ratio INT, reflection_target_ratio INT,
                        psychokinesis_speed REAL, tick_area_angle INT, gliding_fall_speed_fast REAL,
                        gliding_fall_speed_normal REAL, gliding_fall_speed_slow REAL, gliding_land_height REAL,
                        gliding_lift_count INT, gliding_lift_duration REAL, gliding_lift_height REAL,
                        gliding_lift_speed REAL, gliding_lift_valid_time REAL, gliding_move_speed_fast REAL,
                        gliding_move_speed_normal REAL, gliding_move_speed_slow REAL, gliding_sliding_time REAL,
                        gliding_smooth_time REAL, gliding_startup_speed REAL, gliding_startup_time REAL
                      );
                      CREATE TABLE tagged_buffs (id INT PRIMARY KEY, buff_id INT, tag_id INT);
                      """);
        const string patchName = "2026-11-23-campfire-crowd-buff.sql";
        File.Copy(Path.Combine(AppContext.BaseDirectory, "SQL", "patches", "compact", "startup", patchName),
            Path.Combine(files.PatchDirectory, patchName));

        CompactDatabasePatcher.Apply(files.Root);
        CompactDatabasePatcher.Apply(files.Root);

        await Assert.That((long)files.Scalar("SELECT count(*) FROM buffs WHERE id=22659 AND duration=0 AND fx_group_id=836 AND system=1")).IsEqualTo(1L);
        await Assert.That((long)files.Scalar("SELECT count(*) FROM tagged_buffs WHERE buff_id=22659 AND tag_id=2480")).IsEqualTo(1L);
    }

    private sealed class PatchFiles : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("aaemu-compact-patches-").FullName;
        internal string PatchDirectory { get; }
        internal string Database { get; }

        internal PatchFiles()
        {
            PatchDirectory = Directory.CreateDirectory(Path.Combine(Root, "SQL", "patches", "compact", "startup")).FullName;
            Database = Path.Combine(Directory.CreateDirectory(Path.Combine(Root, "Data")).FullName, "compact.sqlite3");
        }

        internal void WritePatch(string name, string sql)
        {
            File.WriteAllText(Path.Combine(PatchDirectory, name), sql);
        }

        internal void Execute(string sql) => Run(sql, scalar: false);

        internal object Scalar(string sql) => Run(sql, scalar: true);

        private object Run(string sql, bool scalar)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Database,
                Pooling = false
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return scalar ? command.ExecuteScalar() : command.ExecuteNonQuery();
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
