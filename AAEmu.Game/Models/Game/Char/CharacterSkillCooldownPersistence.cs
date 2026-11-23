using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char;

internal static class CharacterSkillCooldownPersistence
{
    internal static void Load(MySqlConnection connection, uint characterId, UnitCooldowns cooldowns, DateTime now)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT skill_id, ends_at FROM character_skill_cooldowns
            WHERE character_id = @characterId AND skill_id IN (@acquire, @return) AND ends_at > @now
            """;
        command.Parameters.AddWithValue("@characterId", characterId);
        command.Parameters.AddWithValue("@acquire", TerritoryMemoryEmber.AcquireSkillId);
        command.Parameters.AddWithValue("@return", TerritoryMemoryEmber.ReturnSkillId);
        command.Parameters.AddWithValue("@now", now);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            Restore(cooldowns, reader.GetUInt32(0), reader.GetDateTime(1), now);
    }

    internal static void Restore(UnitCooldowns cooldowns, uint skillId, DateTime endsAt, DateTime now)
    {
        if (skillId is not (TerritoryMemoryEmber.AcquireSkillId or TerritoryMemoryEmber.ReturnSkillId) || endsAt <= now)
            return;

        var end = DateTime.SpecifyKind(endsAt, DateTimeKind.Utc);
        cooldowns.Cooldowns.AddOrUpdate(skillId, end, (_, existing) => end > existing ? end : existing);
    }

    internal static void Save(MySqlConnection connection, MySqlTransaction transaction, uint characterId,
        UnitCooldowns cooldowns, DateTime now)
    {
        cooldowns.Cooldowns.TryGetValue(TerritoryMemoryEmber.AcquireSkillId, out var acquisition);
        cooldowns.Cooldowns.TryGetValue(TerritoryMemoryEmber.ReturnSkillId, out var recall);
        if (acquisition <= now && recall <= now)
            return;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO character_skill_cooldowns (character_id, skill_id, duration, ends_at)
            VALUES (@characterId, @skillId, @duration, @endsAt)
            ON DUPLICATE KEY UPDATE duration = IF(ends_at < @endsAt, @duration, duration),
                                    ends_at = GREATEST(ends_at, @endsAt)
            """;
        command.Parameters.AddWithValue("@characterId", characterId);
        var skillParameter = command.Parameters.Add("@skillId", MySqlDbType.UInt32);
        var durationParameter = command.Parameters.Add("@duration", MySqlDbType.UInt32);
        var endParameter = command.Parameters.Add("@endsAt", MySqlDbType.DateTime);
        SaveOne(TerritoryMemoryEmber.AcquireSkillId, acquisition);
        SaveOne(TerritoryMemoryEmber.ReturnSkillId, recall);

        void SaveOne(uint skillId, DateTime endsAt)
        {
            if (endsAt <= now || SkillManager.Instance.GetSkillTemplate(skillId) is not { CooldownTime: > 0 } template)
                return;

            skillParameter.Value = skillId;
            durationParameter.Value = (uint)template.CooldownTime;
            endParameter.Value = endsAt;
            command.ExecuteNonQuery();
        }
    }
}
