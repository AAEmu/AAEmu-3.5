using AAEmu.Game.Core.Packets.G2C;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char;

public sealed class CharacterSkillActiveTypes(Character owner)
{
    private readonly Lock _sync = new();
    private readonly Dictionary<uint, byte> _activeTypes = [];

    internal bool TryGet(uint skillId, out byte activeType)
    {
        lock (_sync)
            return _activeTypes.TryGetValue(skillId, out activeType);
    }

    internal IReadOnlyDictionary<uint, byte> Capture()
    {
        lock (_sync)
            return new Dictionary<uint, byte>(_activeTypes);
    }

    internal void Set(uint skillId, byte activeType)
    {
        lock (_sync)
        {
            _activeTypes[skillId] = activeType;
            owner.SendPacket(new SCUpdateSkillActiveTypePacket(skillId, activeType));
        }
    }

    internal void Load(MySqlConnection connection)
    {
        var loaded = new Dictionary<uint, byte>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT skill_id, active_type FROM skill_active_types WHERE owner = @owner";
        command.Parameters.AddWithValue("@owner", owner.Id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var skillId = reader.GetInt64(0);
            var activeType = reader.GetInt32(1);
            if (skillId is > 0 and <= uint.MaxValue && activeType is >= 1 and <= 3)
                loaded[(uint)skillId] = (byte)activeType;
        }

        lock (_sync)
        {
            _activeTypes.Clear();
            foreach (var entry in loaded)
                _activeTypes.Add(entry.Key, entry.Value);
        }
    }

    internal void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        foreach (var entry in Capture())
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO skill_active_types (owner, skill_id, active_type)
                VALUES (@owner, @skill, @active)
                ON DUPLICATE KEY UPDATE active_type = @active
                """;
            command.Parameters.AddWithValue("@owner", owner.Id);
            command.Parameters.AddWithValue("@skill", entry.Key);
            command.Parameters.AddWithValue("@active", entry.Value);
            command.ExecuteNonQuery();
        }
    }

    internal void SendOnLogin()
    {
        lock (_sync)
        {
            var entries = new List<KeyValuePair<uint, byte>>(SCListSkillActiveTypePacket.MaxEntries);
            foreach (var entry in _activeTypes)
            {
                entries.Add(entry);
                if (entries.Count != SCListSkillActiveTypePacket.MaxEntries)
                    continue;
                owner.SendPacket(new SCListSkillActiveTypePacket(entries));
                entries = new List<KeyValuePair<uint, byte>>(SCListSkillActiveTypePacket.MaxEntries);
            }

            if (entries.Count > 0)
                owner.SendPacket(new SCListSkillActiveTypePacket(entries));
        }
    }
}
