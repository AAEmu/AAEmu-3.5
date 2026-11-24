using System.Reflection;

using AAEmu.Commons.IO;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using Microsoft.Data.Sqlite;

#nullable enable

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class SkillManagerLoadTests
{
    private static readonly object s_sync = new();
    private static SkillManager? s_manager;
    private static string? s_repoDataDir;

    [Test]
    public async Task Load_UsesDefaultsForNullOptionalBuffColumns()
    {
        var sourceDataDir = FindRepositoryDataDir();
        var temporaryGamePath = Path.Combine(Path.GetTempPath(), $"AAEmu.SkillManagerLoadTests.{Guid.NewGuid():N}");
        var temporaryDataDir = Path.Combine(temporaryGamePath, "Data");
        var appPathField = typeof(FileManager).GetField("_appPath", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not locate FileManager.AppPath backing field.");
        var originalAppPath = appPathField.GetValue(null);

        try
        {
            Directory.CreateDirectory(temporaryDataDir);
            File.Copy(Path.Combine(sourceDataDir, "compact.sqlite3"), Path.Combine(temporaryDataDir, "compact.sqlite3"));
            File.Copy(Path.Combine(sourceDataDir, "compact.server.table.sqlite3"),
                Path.Combine(temporaryDataDir, "compact.server.table.sqlite3"));

            uint buffId;
            var databasePath = Path.Combine(temporaryDataDir, "compact.sqlite3");
            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT id FROM buffs ORDER BY id LIMIT 1";
                buffId = Convert.ToUInt32(command.ExecuteScalar());

                var nullAssignments = new List<string>();
                using (var schemaCommand = connection.CreateCommand())
                {
                    schemaCommand.CommandText = "PRAGMA table_info(buffs)";
                    using var schemaReader = schemaCommand.ExecuteReader();
                    while (schemaReader.Read())
                    {
                        var columnName = schemaReader.GetString(1);
                        if (columnName == "id" || columnName == "active_weapon_id")
                            continue;

                        nullAssignments.Add($"\"{columnName}\" = NULL");
                    }
                }

                command.CommandText = $"UPDATE buffs SET {string.Join(", ", nullAssignments)} WHERE id = $id";
                command.Parameters.AddWithValue("$id", buffId);
                command.ExecuteNonQuery();
            }

            appPathField.SetValue(null, temporaryGamePath);
            var manager = new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object);

            manager.Load();

            var buff = manager.GetBuffTemplate(buffId);
            await Assert.That(buff).IsNotNull();
            await Assert.That(buff!.AddDurationBuffMul).IsEqualTo(0u);
            await Assert.That(buff.AddDurationBuffId).IsEqualTo(0u);
            await Assert.That(buff.AnimActionId).IsEqualTo(0u);
            await Assert.That(buff.AntiStealth).IsFalse();
            await Assert.That(buff.AgStance).IsEqualTo(string.Empty);
            await Assert.That(buff.BossTelescopeRange).IsEqualTo(0f);
            await Assert.That(buff.Desc).IsEqualTo(string.Empty);
            await Assert.That(buff.ExtraEffects).IsEqualTo(string.Empty);
            await Assert.That(buff.IdleAnim).IsEqualTo(string.Empty);
            await Assert.That(buff.Name).IsEqualTo(string.Empty);
            await Assert.That(buff.Tick).IsEqualTo(0d);
        }
        finally
        {
            appPathField.SetValue(null, originalAppPath);
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(temporaryGamePath))
                Directory.Delete(temporaryGamePath, recursive: true);
        }
    }

    [Test]
    public async Task Load_MapsSkillAndControllerColumns_FromRealSqliteData()
    {
        var manager = GetLoadedManager();

        var skill8001 = manager.GetSkillTemplate(8001);
        await Assert.That(skill8001).IsNotNull();
        await Assert.That(skill8001!.Name).IsEqualTo(GetNullableStringFromSource("skills", 8001, "name"));
        await Assert.That(skill8001.StartAnimId).IsEqualTo(GetUInt32FromSource("skills", 8001, "start_anim_id"));
        await Assert.That(skill8001.CategoryId).IsEqualTo(GetInt32FromSource("skills", 8001, "category_id"));

        var skill10152 = manager.GetSkillTemplate(10152);
        await Assert.That(skill10152).IsNotNull();
        await Assert.That(skill10152!.UseSkillCamera).IsEqualTo(GetBoolFromSource("skills", 10152, "use_skill_camera"));

        var skill10082 = manager.GetSkillTemplate(10082);
        await Assert.That(skill10082).IsNotNull();
        await Assert.That(skill10082!.SynergyIcon1Id).IsEqualTo(GetUInt32FromSource("skills", 10082, "synergy_icon1_id"));

        var buff20343 = manager.GetBuffTemplate(20343);
        await Assert.That(buff20343).IsNotNull();
        var tickEffectTarget = GetTickEffect(buff20343!, 58823);
        await Assert.That(tickEffectTarget).IsNotNull();
        await Assert.That(tickEffectTarget!.CheckTargetTagSrc).IsEqualTo(GetBoolFromSource("buff_tick_effects", 3007, "check_target_tag_src"));
        await Assert.That(tickEffectTarget.TargetBuffTagId).IsEqualTo(GetUInt32FromSource("buff_tick_effects", 3007, "target_buff_tag_id"));

        var tickEffectNoTarget = GetTickEffect(buff20343!, 58825);
        await Assert.That(tickEffectNoTarget).IsNotNull();
        await Assert.That(tickEffectNoTarget!.CheckNoTargetTagSrc).IsEqualTo(GetBoolFromSource("buff_tick_effects", 3009, "check_no_target_tag_src"));
        await Assert.That(tickEffectNoTarget.TargetNoBuffTagId).IsEqualTo(GetUInt32FromSource("buff_tick_effects", 3009, "target_nobuff_tag_id"));

        var controller = manager.GetEffectTemplate(55, "SkillController") as SkillControllerTemplate;
        await Assert.That(controller).IsNotNull();
        await Assert.That(controller!.KindId).IsEqualTo(GetUInt32FromSource("skill_controllers", 55, "kind_id"));
        await Assert.That(controller.ActiveWeaponId).IsEqualTo(GetUInt32FromSource("skill_controllers", 55, "active_weapon_id"));
        await Assert.That(controller.EndAnimId).IsEqualTo(GetUInt32FromSource("skill_controllers", 55, "end_anim_id"));
        await Assert.That(controller.EndSkillId).IsEqualTo(GetUInt32FromSource("skill_controllers", 55, "end_skill_id"));
        await Assert.That(controller.StartAnimId).IsEqualTo(GetUInt32FromSource("skill_controllers", 55, "start_anim_id"));
        await Assert.That(controller.TransitionAnim1Id).IsEqualTo(GetUInt32FromSource("skill_controllers", 55, "transition_anim_1_id"));
        await Assert.That(controller.TransitionAnim2Id).IsEqualTo(GetUInt32FromSource("skill_controllers", 55, "transition_anim_2_id"));
        await Assert.That(controller.StrValue1).IsEqualTo(GetNullableStringFromSource("skill_controllers", 55, "str_value1"));
        await Assert.That(controller.Value[0]).IsEqualTo(GetInt32FromSource("skill_controllers", 55, "value1"));
        await Assert.That(controller.Value[1]).IsEqualTo(GetInt32FromSource("skill_controllers", 55, "value2"));
    }

    [Test]
    public async Task Load_RegistersNewEffectTypes_FromRealSqliteData()
    {
        var manager = GetLoadedManager();

        var playLog = manager.GetEffectTemplate(3, "PlayLogEffect") as PlayLogEffect;
        await Assert.That(playLog).IsNotNull();
        await Assert.That(playLog!.Message).IsEqualTo(GetNullableStringFromSource("play_log_effects", 3, "message"));

        var worldMessage = manager.GetEffectTemplate(4, "WorldMessageEffect") as WorldMessageEffect;
        await Assert.That(worldMessage).IsNotNull();
        await Assert.That(worldMessage!.FactionScopeId).IsEqualTo(GetInt32FromSource("world_message_effects", 4, "faction_scope_id"));
        await Assert.That(worldMessage.IconKey).IsEqualTo(GetNullableStringFromSource("world_message_effects", 4, "icon_key"));
        await Assert.That(worldMessage.KillHero).IsEqualTo(GetBoolFromSource("world_message_effects", 4, "kill_hero"));
        await Assert.That(worldMessage.Message).IsEqualTo(GetNullableStringFromSource("world_message_effects", 4, "message"));
        await Assert.That(worldMessage.KillStreakCount).IsEqualTo(GetInt32FromSource("world_message_effects", 4, "kill_streak_count"));
        await Assert.That(worldMessage.ZoneGroupOnly).IsEqualTo(GetBoolFromSource("world_message_effects", 4, "zone_group_only"));
        await Assert.That(worldMessage.ZoneGroupWarState).IsEqualTo(GetBoolFromSource("world_message_effects", 4, "zone_group_war_state"));

        var highAbilityResource = manager.GetEffectTemplate(1, "HighAbilityResourceEffect") as HighAbilityResourceEffect;
        await Assert.That(highAbilityResource).IsNotNull();
        await Assert.That(highAbilityResource!.MaxHighAbilityResource).IsEqualTo(GetInt32FromSource("high_ability_resource_effects", 1, "max_high_ability_resource"));
        await Assert.That(highAbilityResource.MinHighAbilityResource).IsEqualTo(GetInt32FromSource("high_ability_resource_effects", 1, "min_high_ability_resource"));

        var moveToLocation = manager.GetEffectTemplate(1, "MoveToLocationEffect") as MoveToLocationEffect;
        await Assert.That(moveToLocation).IsNotNull();
        await Assert.That(moveToLocation!.OwnHouseOnly).IsEqualTo(GetBoolFromSource("move_to_location_effects", 1, "own_house_only"));

        var levelUp = manager.GetEffectTemplate(1, "LevelUpEffect") as LevelUpEffect;
        await Assert.That(levelUp).IsNotNull();
        await Assert.That(levelUp!.Level).IsEqualTo(GetInt32FromSource("level_up_effects", 1, "level"));

        var doodadItemChange = manager.GetEffectTemplate(1, "DoodadItemChangeEffect") as DoodadItemChangeEffect;
        await Assert.That(doodadItemChange).IsNotNull();
        await Assert.That(doodadItemChange!.Idx).IsEqualTo(GetInt32FromSource("doodad_item_change_effects", 1, "idx"));

        var recoverExp = manager.GetEffectTemplate(5, "RecoverExpEffect") as RecoverExpEffect;
        await Assert.That(recoverExp).IsNotNull();
        await Assert.That(recoverExp!.NeedMoney).IsEqualTo(GetBoolFromSource("recover_exp_effects", 5, "need_money"));
        await Assert.That(recoverExp.NeedLaborPower).IsEqualTo(GetBoolFromSource("recover_exp_effects", 5, "need_labor_power"));
        await Assert.That(recoverExp.NeedPriest).IsEqualTo(GetBoolFromSource("recover_exp_effects", 5, "need_priest"));
        await Assert.That(recoverExp.Penaltied).IsEqualTo(GetBoolFromSource("recover_exp_effects", 5, "penaltied"));
    }

    [Test]
    [Arguments(Race.Nuian, 35420u, 35418u)]
    [Arguments(Race.Dwarf, 35427u, 0u)]
    [Arguments(Race.Elf, 35421u, 35422u)]
    [Arguments(Race.Hariharan, 35423u, 35424u)]
    [Arguments(Race.Ferre, 35425u, 35426u)]
    [Arguments(Race.Warborn, 35428u, 0u)]
    [Arguments(Race.Fairy, 0u, 0u)]
    public async Task Load_UsesStartingActionSkillsForTheCharacterTemplate(Race race, uint firstRacialSkill, uint secondRacialSkill)
    {
        var manager = GetLoadedManager();

        foreach (var gender in new[] { Gender.Male, Gender.Female })
        {
            var slots = new Dictionary<byte, uint>();
            foreach (var skill in manager.GetStartingActionSkills(race, gender))
            {
                await Assert.That(skill.AddToSlot).IsTrue();
                await Assert.That(slots.TryAdd(skill.Slot, skill.Template.Id)).IsTrue();
            }

            await Assert.That(slots.GetValueOrDefault((byte)13)).IsEqualTo(16064u);
            await Assert.That(slots.GetValueOrDefault((byte)17)).IsEqualTo(firstRacialSkill);
            await Assert.That(slots.GetValueOrDefault((byte)18)).IsEqualTo(secondRacialSkill);
        }
    }

    private static SkillManager GetLoadedManager()
    {
        lock (s_sync)
        {
            PrepareSqliteData();

            if (s_manager != null)
                return s_manager;

            var mockAnimation = Mock.Of<IAnimationManager>();
            var mockPlot = Mock.Of<IPlotManager>();
            s_manager = new SkillManager(mockAnimation.Object, mockPlot.Object);
            s_manager.Load();
            return s_manager;
        }
    }

    private static void PrepareSqliteData()
    {
        if (s_repoDataDir == null)
        {
            s_repoDataDir = FindRepositoryDataDir();
        }

        var sourceDataDir = s_repoDataDir;
        var destinationDataDir = Path.Combine(FileManager.AppPath, "Data");
        Directory.CreateDirectory(destinationDataDir);

        // Под тестовым хостом FileManager.AppPath — каталог AAEmu.Game: в процессе загружена сборка сервера,
        // и у неё есть EntryPoint, так что AppPath берётся по ней, а не по тестовому выходу. Источник и
        // приёмник тогда совпадают, а File.Copy(source, destination, true) с одним и тем же путём обрезает
        // файл и пишет нули — 139 МБ базы превращались в нули прямо посреди прогона.
        CopyDataFile(sourceDataDir, destinationDataDir, "compact.sqlite3");
        CopyDataFile(sourceDataDir, destinationDataDir, "compact.server.table.sqlite3");
    }

    private static void CopyDataFile(string sourceDataDir, string destinationDataDir, string fileName)
    {
        var source = Path.GetFullPath(Path.Combine(sourceDataDir, fileName));
        var destination = Path.GetFullPath(Path.Combine(destinationDataDir, fileName));
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            return; // уже на месте: копировать файл сам на себя нельзя

        File.Copy(source, destination, true);
    }

    private static string FindRepositoryDataDir()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "AAEmu.Game", "Data");
            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate AAEmu.Game/Data from test output directory.");
    }

    private static SqliteConnection OpenSourceConnection(string databaseFile)
    {
        var repoDataDir = s_repoDataDir ?? throw new InvalidOperationException("Repository data directory was not initialized.");
        var connection = new SqliteConnection($"Data Source={Path.Combine(repoDataDir, databaseFile)};Mode=ReadOnly");
        connection.Open();
        return connection;
    }

    private static TickEffect? GetTickEffect(BuffTemplate buff, uint effectId)
    {
        foreach (var tickEffect in buff.TickEffects)
        {
            if (tickEffect.EffectId == effectId)
                return tickEffect;
        }

        return null;
    }

    private static string? GetNullableStringFromSource(string table, uint id, string column)
    {
        using var connection = OpenSourceConnection("compact.sqlite3");
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM {table} WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException($"No row found in {table} for id {id}.");
        return reader.IsDBNull(0) ? null : reader.GetString(0);
    }

    private static int GetInt32FromSource(string table, uint id, string column)
    {
        using var connection = OpenSourceConnection("compact.sqlite3");
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM {table} WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException($"No row found in {table} for id {id}.");
        return reader.GetInt32(0);
    }

    private static uint GetUInt32FromSource(string table, uint id, string column)
    {
        using var connection = OpenSourceConnection("compact.sqlite3");
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM {table} WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException($"No row found in {table} for id {id}.");
        return (uint)reader.GetInt64(0);
    }

    private static bool GetBoolFromSource(string table, uint id, string column)
    {
        using var connection = OpenSourceConnection("compact.sqlite3");
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM {table} WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException($"No row found in {table} for id {id}.");
        return reader.GetBoolean(0);
    }
}
