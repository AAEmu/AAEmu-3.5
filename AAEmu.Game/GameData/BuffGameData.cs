using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class BuffGameData : Singleton<BuffGameData>, IGameDataLoader
{
    private Dictionary<uint, List<BuffModifier>> _buffModifiers;
    private Dictionary<uint, BuffTolerance> _buffTolerances;
    private Dictionary<uint, BuffTolerance> _buffTolerancesById;
    private Dictionary<uint, HashSet<uint>> _immuneBuffTags = [];
    private Dictionary<uint, HashSet<uint>> _requiredBuffTags = [];
    private Dictionary<uint, HashSet<uint>> _buffsBrokenByTag = [];

    public List<BuffModifier> GetModifiersForBuff(uint ownerId)
    {
        return _buffModifiers.TryGetValue(ownerId, out var modifier) ? modifier : [];
    }

    public BuffTolerance GetBuffToleranceForBuffTag(uint buffTag)
    {
        return _buffTolerances.TryGetValue(buffTag, out var tolerance) ? tolerance : null;
    }

    public IReadOnlySet<uint> GetImmuneBuffTags(uint buffId) => _immuneBuffTags.GetValueOrDefault(buffId);

    public IReadOnlySet<uint> GetRequiredBuffTags(uint buffId) => _requiredBuffTags.GetValueOrDefault(buffId);
    public IReadOnlySet<uint> GetBuffsBrokenByTag(uint tagId) => _buffsBrokenByTag.GetValueOrDefault(tagId);

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        _buffModifiers = [];
        _buffTolerances = [];
        _buffTolerancesById = [];
        _immuneBuffTags = ReadBuffTags(connection, "SELECT buff_id AS buff_key, buff_tag_id AS buff_value FROM tagged_immune_buffs");
        _requiredBuffTags = ReadBuffTags(connection, "SELECT buff_id AS buff_key, buff_tag_id AS buff_value FROM tagged_require_buffs");
        _buffsBrokenByTag = ReadBuffTags(connection2, "SELECT buff_tag_id AS buff_key, buff_id AS buff_value FROM buff_breakers");

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM buff_modifiers";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new BuffModifier();
                    //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                    template.OwnerId = reader.GetUInt32("owner_id");
                    template.OwnerType = reader.GetString("owner_type");
                    template.TagId = reader.GetUInt32("tag_id", 0);
                    template.BuffAttribute = (BuffAttribute)reader.GetUInt32("buff_attribute_id");
                    template.UnitModifierType = (UnitModifierType)reader.GetUInt32("unit_modifier_type_id");
                    template.Value = reader.GetInt32("value");
                    template.BuffId = reader.GetUInt32("buff_id", 0);
                    template.Synergy = reader.GetBoolean("synergy");

                    if (!_buffModifiers.ContainsKey(template.OwnerId))
                        _buffModifiers.Add(template.OwnerId, []);
                    _buffModifiers[template.OwnerId].Add(template);
                }
            }
        }

        using (var command = connection2.CreateCommand())
        {
            command.CommandText = "SELECT * FROM buff_tolerances";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new BuffTolerance
                    {
                        Id = reader.GetUInt32("id"),
                        BuffTagId = reader.GetUInt32("buff_tag_id"),
                        StepDuration = reader.GetUInt32("step_duration"),
                        FinalStepBuffId = reader.GetUInt32("final_step_buff_id"),
                        CharacterTimeReduction = reader.GetUInt32("character_time_reduction"),
                        Steps = []
                    };

                    _buffTolerances.Add(template.BuffTagId, template);
                    _buffTolerancesById.Add(template.Id, template);
                }
            }
        }

        using (var command = connection2.CreateCommand())
        {
            command.CommandText = "SELECT * FROM buff_tolerance_steps";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var buffToleranceId = reader.GetUInt32("buff_tolerance_id");
                    if (!_buffTolerancesById.TryGetValue(buffToleranceId, out var buffTolerance)) // potential bug?
                        continue;
                    var template = new BuffToleranceStep
                    {
                        Id = reader.GetUInt32("id"),
                        BuffTolerance = buffTolerance,
                        HitChance = reader.GetUInt32("hit_chance"),
                        TimeReduction = reader.GetUInt32("time_reduction")
                    };

                    buffTolerance.Steps.Add(template);
                }
            }
        }
    }

    public void PostLoad()
    {
        foreach (var tolerance in _buffTolerances.Values)
        {
            tolerance.Steps.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        }
    }

    private static Dictionary<uint, HashSet<uint>> ReadBuffTags(SqliteConnection connection, string query)
    {
        Dictionary<uint, HashSet<uint>> buffTags = [];
        using var command = connection.CreateCommand();
        command.CommandText = query;
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var key = reader.GetUInt32("buff_key");
            var value = reader.GetUInt32("buff_value");
            if (!buffTags.TryGetValue(key, out var tags))
            {
                tags = [];
                buffTags.Add(key, tags);
            }

            tags.Add(value);
        }

        return buffTags;
    }
}
