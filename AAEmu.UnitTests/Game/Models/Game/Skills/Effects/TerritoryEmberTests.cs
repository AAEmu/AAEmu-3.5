using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

public sealed class TerritoryEmberTests
{
    [Test]
    [Arguments(645u, 10983u, AttachPointKind.Cannon1, 31216u, 34u, 0u)]
    [Arguments(645u, 10983u, AttachPointKind.Cannon1, 31221u, 54u, 0u)]
    [Arguments(654u, 11203u, AttachPointKind.Cannon14, 32025u, 33u, 9u)]
    [Arguments(655u, 11204u, AttachPointKind.Cannon14, 32026u, 43u, 9u)]
    [Arguments(656u, 11205u, AttachPointKind.Cannon14, 32027u, 44u, 9u)]
    public async Task AcceptsOnlyTheAuthoredHousingAttachmentAndInteraction(
        uint house, uint doodad, AttachPointKind attachment, uint phase, uint territory, uint permission)
    {
        await Assert.That(TerritoryMemoryEmber.MatchesSource(house, doodad, attachment, phase, territory, permission)).IsTrue();
        await Assert.That(TerritoryMemoryEmber.MatchesSource(42, doodad, attachment, phase, territory, permission)).IsFalse();
        await Assert.That(TerritoryMemoryEmber.MatchesSource(house, 16617, attachment, phase, territory, permission)).IsFalse();
        await Assert.That(TerritoryMemoryEmber.MatchesSource(house, doodad, AttachPointKind.None, phase, territory, permission)).IsFalse();
        await Assert.That(TerritoryMemoryEmber.MatchesSource(house, doodad, attachment, 31215, territory, permission)).IsFalse();
        await Assert.That(TerritoryMemoryEmber.MatchesSource(house, doodad, attachment, phase, 1, permission)).IsFalse();
        await Assert.That(TerritoryMemoryEmber.MatchesSource(house, doodad, attachment, phase, territory, 1)).IsFalse();
    }

    [Test]
    public async Task TerritoryRecallKeepsItsOriginalPointAndRejectsStaleOwnershipAndTerritory()
    {
        var template = new MemoryEmberTemplate { Id = TerritoryMemoryEmber.ItemId };
        var house = new House { Id = 91, TemplateId = 654, OwnerId = 17, PlaceDate = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc) };
        var position = new Vector3(1400f, 1700f, 57.5f);
        var ember = new MemoryEmber(1, template, 1);
        ember.BindToTerritory(house, 910, 33, position, 0.5f);
        var binding = new PacketStream();
        ember.WriteBinding(binding);
        binding.Rollback();

        var restored = new MemoryEmber(1, template, 1);
        restored.ReadBinding(binding);
        var itemDetails = new PacketStream();
        restored.WriteDetails(itemDetails);

        await Assert.That(restored.MatchesTerritory(house, 910, 33)).IsTrue();
        await Assert.That(restored.ReturnPosition).IsEqualTo(position);
        await Assert.That(restored.ReturnRotation).IsEqualTo(0.5f);
        await Assert.That(itemDetails.Count).IsEqualTo(0);
        await Assert.That(restored.MatchesTerritory(house, 910, 34)).IsFalse();
        await Assert.That(restored.MatchesTerritory(house, 911, 33)).IsFalse();
        house.OwnerId = 18;
        await Assert.That(restored.MatchesTerritory(house, 910, 33)).IsFalse();
    }

    [Test]
    [Arguments(645u, 40191u)]
    [Arguments(654u, 40189u)]
    [Arguments(655u, 40567u)]
    [Arguments(656u, 40568u)]
    public async Task TerritoryHousingRequiresAPlanForThatExactBuilding(uint design, uint item)
    {
        var housing = new HousingGameData();
        using var connection = OpenCompactDatabase();
        housing.LoadDesignItems(connection);

        await Assert.That(housing.IsDesignItem(design, item)).IsTrue();
        await Assert.That(housing.IsDesignItem(design, 13712)).IsFalse();
        await Assert.That(housing.IsDesignItem(design, TerritoryMemoryEmber.ItemId)).IsFalse();
        await Assert.That(housing.IsDesignItem(86, 13712)).IsTrue();
    }

    [Test]
    public async Task PersistentCooldownRestoreUsesAbsoluteDeadlinesAndKeepsNewerState()
    {
        var cooldowns = new UnitCooldowns();
        var now = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        var tomorrow = now.AddHours(20);
        var laterToday = now.AddHours(3);

        CharacterSkillCooldownPersistence.Restore(cooldowns, TerritoryMemoryEmber.AcquireSkillId, tomorrow, now);
        CharacterSkillCooldownPersistence.Restore(cooldowns, TerritoryMemoryEmber.AcquireSkillId, now.AddHours(15), now);
        CharacterSkillCooldownPersistence.Restore(cooldowns, TerritoryMemoryEmber.ReturnSkillId, now.AddMinutes(-30), now);
        CharacterSkillCooldownPersistence.Restore(cooldowns, 2, tomorrow, now);

        await Assert.That(cooldowns.Cooldowns[TerritoryMemoryEmber.AcquireSkillId]).IsEqualTo(tomorrow);
        await Assert.That(cooldowns.Cooldowns.ContainsKey(TerritoryMemoryEmber.ReturnSkillId)).IsFalse();
        await Assert.That(cooldowns.Cooldowns.ContainsKey(2)).IsFalse();

        CharacterSkillCooldownPersistence.Restore(cooldowns, TerritoryMemoryEmber.ReturnSkillId,
            DateTime.SpecifyKind(laterToday, DateTimeKind.Unspecified), now);

        await Assert.That(cooldowns.Cooldowns[TerritoryMemoryEmber.ReturnSkillId]).IsEqualTo(laterToday);
        await Assert.That(cooldowns.Cooldowns[TerritoryMemoryEmber.ReturnSkillId].Kind).IsEqualTo(DateTimeKind.Utc);
    }

    private static SqliteConnection OpenCompactDatabase()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "AAEmu.Game", "Data", "compact.sqlite3");
            if (!File.Exists(path))
                continue;

            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
            connection.Open();
            return connection;
        }

        throw new FileNotFoundException("Unable to locate the game's compact SQLite database.");
    }
}
