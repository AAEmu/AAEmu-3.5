using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Skills;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public sealed class SkillAuthorityGameData : Singleton<SkillAuthorityGameData>, IGameDataLoader
{
    private Data _data = new();

    internal sealed record MountSkill(uint Id, bool CheckAttachPoint, HashSet<AttachPointKind> Seats);
    internal readonly record struct BuffMountSkill(uint BuffId, MountSkill Row);
    internal readonly record struct BuffSwap(uint BuffId, uint OriginSkillId);
    internal readonly record struct HeirSkill(ushort GroupId, ushort OriginId, ushort SuccessorId, byte Step, byte Points,
        AbilityType Ability, byte HighAbility, byte RequiredAbilityLevel, byte LevelStep);

    private sealed class Data
    {
        public HashSet<uint> SlashSkills { get; } = [];
        public HashSet<(uint OwnerId, uint SkillId)> NpcInteractions { get; } = [];
        public HashSet<(uint OwnerId, uint SkillId)> SlaveInteractions { get; } = [];
        public HashSet<uint> HousingRebuildSkills { get; } = [];
        public HashSet<uint> ComboTargets { get; } = [];
        public Dictionary<(uint OwnerId, uint SkillId), List<MountSkill>> MateSkills { get; } = [];
        public Dictionary<(uint OwnerId, uint SkillId), List<MountSkill>> SlaveSkills { get; } = [];
        public Dictionary<uint, List<BuffMountSkill>> BuffMountSkills { get; } = [];
        public Dictionary<uint, List<uint>> BuffSkills { get; } = [];
        public Dictionary<uint, HeirSkill> HeirSkills { get; } = [];
        public Dictionary<uint, List<BuffSwap>> BuffSwaps { get; } = [];
        public Dictionary<uint, List<(int GradeOrder, uint SkillId)>> GradeSkills { get; } = [];
        public Dictionary<uint, int> GradeOrders { get; } = [];
    }

    internal bool IsSlashSkill(uint skillId) => Volatile.Read(ref _data).SlashSkills.Contains(skillId);

    internal bool IsNpcInteraction(uint setId, uint skillId) =>
        Volatile.Read(ref _data).NpcInteractions.Contains((setId, skillId));

    internal bool IsSlaveInteraction(uint templateId, uint skillId) =>
        Volatile.Read(ref _data).SlaveInteractions.Contains((templateId, skillId));

    internal bool IsHousingRebuildSkill(uint skillId) => Volatile.Read(ref _data).HousingRebuildSkills.Contains(skillId);

    internal bool IsComboTarget(uint skillId) => Volatile.Read(ref _data).ComboTargets.Contains(skillId);

    internal IReadOnlyList<uint> GetGrantingBuffs(uint skillId) =>
        Volatile.Read(ref _data).BuffSkills.GetValueOrDefault(skillId) ?? [];

    internal bool TryGetHeirSkill(uint successorId, out HeirSkill skill) =>
        Volatile.Read(ref _data).HeirSkills.TryGetValue(successorId, out skill);

    internal IReadOnlyList<BuffSwap> GetBuffSwaps(uint skillId) =>
        Volatile.Read(ref _data).BuffSwaps.GetValueOrDefault(skillId) ?? [];

    internal IReadOnlyList<MountSkill> GetMountSkills(uint templateId, uint skillId, bool isMate)
    {
        var data = Volatile.Read(ref _data);
        var skills = isMate ? data.MateSkills : data.SlaveSkills;
        return skills.GetValueOrDefault((templateId, skillId)) ?? [];
    }

    internal IReadOnlyList<BuffMountSkill> GetBuffMountSkills(uint skillId) =>
        Volatile.Read(ref _data).BuffMountSkills.GetValueOrDefault(skillId) ?? [];

    internal uint GetGradeSkill(uint itemId, byte grade)
    {
        var data = Volatile.Read(ref _data);
        if (!data.GradeSkills.TryGetValue(itemId, out var skills) || !data.GradeOrders.TryGetValue(grade, out var order))
            return 0;

        // Highest available grade at or below the item's grade (the rows are ordered by grade descending).
        foreach (var (gradeOrder, skillId) in skills)
        {
            if (gradeOrder <= order)
                return skillId;
        }

        return 0;
    }

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        var data = new Data();
        Read(connection, "SELECT skill_id FROM slash_commands WHERE skill_id > 0", row => data.SlashSkills.Add(Id(row, 0)));
        Read(connection, "SELECT npc_interaction_set_id, skill_id FROM npc_interactions WHERE skill_id > 0",
            row => data.NpcInteractions.Add((Id(row, 0), Id(row, 1))));
        Read(connection, "SELECT slave_id, skill_id FROM slave_interaction_skills WHERE skill_id > 0",
            row => data.SlaveInteractions.Add((Id(row, 0), Id(row, 1))));
        Read(connection, "SELECT skill_id FROM housing_rebuildings WHERE skill_id > 0",
            row => data.HousingRebuildSkills.Add(Id(row, 0)));
        Read(connection, "SELECT buff_id, skill_id FROM buff_skills WHERE skill_id > 0 AND buff_id > 0",
            row => Add(data.BuffSkills, Id(row, 1), Id(row, 0)));
        Read(connection, """
            SELECT heir.id, heir.skill_id, detail.skill_id, heir.step, detail.point,
                   skill.ability_id, skill.high_ability_id, skill.ability_level, skill.level_step
            FROM heir_skill_details detail JOIN heir_skills heir ON heir.id = detail.heir_skill_id
            JOIN skills skill ON skill.id = detail.skill_id
            WHERE detail.skill_id BETWEEN 1 AND 65535 AND heir.id BETWEEN 1 AND 65535
              AND heir.skill_id BETWEEN 1 AND 65535
            """, row => data.HeirSkills.Add(Id(row, 2), new HeirSkill(
                checked((ushort)Id(row, 0)), checked((ushort)Id(row, 1)), checked((ushort)Id(row, 2)),
                row.GetByte(3), row.GetByte(4), (AbilityType)row.GetByte(5), row.GetByte(6), row.GetByte(7), row.GetByte(8))));
        Read(connection, """
            SELECT new_skill_id, buff_id, origin_skill_id FROM buff_swap_skills
            WHERE new_skill_id > 0 AND buff_id > 0 AND origin_skill_id > 0
            """, row => Add(data.BuffSwaps, Id(row, 0), new BuffSwap(Id(row, 1), Id(row, 2))));
        Read(connection, """
            SELECT DISTINCT special.value1
            FROM special_effects special
            JOIN effects effect ON effect.actual_type = 'SpecialEffect' AND effect.actual_id = special.id
            JOIN skill_effects mapping ON mapping.effect_id = effect.id
            WHERE special.special_effect_type_id = 48 AND special.value1 > 0
            """, row => data.ComboTargets.Add(Id(row, 0)));

        LoadMountSkills(connection, data);
        Read(connection, "SELECT id, grade_order FROM item_grades", row => data.GradeOrders[Id(row, 0)] = row.GetInt32(1));
        Read(connection, """
            SELECT mapping.item_id, grade.grade_order, mapping.skill_id
            FROM item_grade_skills mapping JOIN item_grades grade ON grade.id = mapping.item_grade_id
            WHERE mapping.skill_id > 0
            ORDER BY grade.grade_order DESC
            """, row => Add(data.GradeSkills, Id(row, 0), (row.GetInt32(1), Id(row, 2))));

        Volatile.Write(ref _data, data);
    }

    private static void LoadMountSkills(SqliteConnection connection, Data data)
    {
        var seats = new Dictionary<uint, HashSet<AttachPointKind>>();
        Read(connection, "SELECT mount_skill_id, attach_point_id FROM mount_attached_skills", row =>
        {
            var id = Id(row, 0);
            if (!seats.TryGetValue(id, out var list))
                seats[id] = list = [];
            list.Add((AttachPointKind)row.GetByte(1));
        });

        var authoredSkills = new Dictionary<uint, (uint SkillId, MountSkill Row)>();
        Read(connection, "SELECT id, skill_id, check_attach_point FROM mount_skills WHERE skill_id > 0", row =>
        {
            var id = Id(row, 0);
            var checkAttachPoint = row.IsDBNull(2) || row.GetBoolean(2);
            authoredSkills[id] = (Id(row, 1), new MountSkill(id, checkAttachPoint, seats.GetValueOrDefault(id) ?? []));
        });

        Read(connection, "SELECT npc_id, mount_skill_id FROM npc_mount_skills", row =>
        {
            if (authoredSkills.TryGetValue(Id(row, 1), out var skill))
                Add(data.MateSkills, (Id(row, 0), skill.SkillId), skill.Row);
        });
        Read(connection, "SELECT slave_id, mount_skill_id FROM slave_mount_skills", row =>
        {
            if (authoredSkills.TryGetValue(Id(row, 1), out var skill))
                Add(data.SlaveSkills, (Id(row, 0), skill.SkillId), skill.Row);
        });
        Read(connection, "SELECT buff_id, mount_skill_id FROM buff_mount_skills", row =>
        {
            if (authoredSkills.TryGetValue(Id(row, 1), out var skill))
                Add(data.BuffMountSkills, skill.SkillId, new BuffMountSkill(Id(row, 0), skill.Row));
        });
    }

    private static void Add<TKey, TValue>(Dictionary<TKey, List<TValue>> lookup, TKey key, TValue value)
    {
        if (!lookup.TryGetValue(key, out var values))
            lookup[key] = values = [];
        values.Add(value);
    }

    private static uint Id(SqliteDataReader reader, int ordinal) => checked((uint)reader.GetInt64(ordinal));

    private static void Read(SqliteConnection connection, string sql, Action<SqliteDataReader> consume)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
            consume(reader);
    }

    public void PostLoad()
    {
    }
}
