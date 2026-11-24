using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Units.Static;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.GameData;

public class MateGameDataTests
{
    [Test]
    public async Task Load_ResolvesMateTypeFromTheNpcEquipSlotPack()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE mate_equip_slot_packs (id INT PRIMARY KEY, mate_type_id INT);
            INSERT INTO mate_equip_slot_packs VALUES (4, 1), (5, 2);
            CREATE TABLE npc_mount_skills (npc_id INT, mount_skill_id INT);
            INSERT INTO npc_mount_skills VALUES (5430, 12);
            CREATE TABLE mount_skills (id INT, skill_id INT);
            CREATE TABLE mount_attached_skills (id INT, mount_skill_id INT, attach_point_id INT, skill_id INT);
            """;
        command.ExecuteNonQuery();

        var mateData = new MateGameData();
        mateData.Load(connection, null);

        await Assert.That(mateData.GetMateType(4)).IsEqualTo(MateType.Ride);
        await Assert.That(mateData.GetMateType(5)).IsEqualTo(MateType.Battle);
        await Assert.That(mateData.GetMateType(0)).IsEqualTo(MateType.None);
        await Assert.That(mateData.GetMateSkills(5430)).IsEquivalentTo(new uint[] { 12 });
        await Assert.That(mateData.GetMateSkills(17667)).IsEmpty();
    }
}
