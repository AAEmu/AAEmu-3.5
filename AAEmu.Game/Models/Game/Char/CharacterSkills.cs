using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char;

public partial class CharacterSkills
{
    private enum SkillType : byte
    {
        Skill = 1,
        Buff = 2
    }

    private readonly List<uint> _removed = [];
    private readonly Lock _skillsLock = new();
    private Dictionary<uint, long> _comboFollowUps;
    public Dictionary<uint, Skill> Skills { get; } = [];
    public Dictionary<uint, PassiveBuff> PassiveBuffs { get; } = [];
    private Character Owner { get; }

    public CharacterSkills(Character owner) : this(owner, SkillAuthorityGameData.Instance, ExperienceManager.Instance)
    {
    }

    internal CharacterSkills(Character owner, SkillAuthorityGameData skillData, ExperienceManager experience)
    {
        Owner = owner;
        _skillData = skillData;
        _experience = experience;
    }

    /// <summary>
    /// Try to learn a new Skill
    /// </summary>
    /// <param name="skillId"></param>
    public void AddSkill(uint skillId)
    {
        if (_skillData.TryGetHeirSkill(skillId, out _))
            return;

        // Check if what we want to learn is part of an active skill tree (or not part of one)
        var template = SkillManager.Instance.GetSkillTemplate(skillId);
        if (template == null || !CanLearnInTree(template.AbilityId))
            return;

        // Get total skill points for the player's level
        var points = ExperienceManager.Instance.GetSkillPointsForLevel(Owner.Level);

        // Deduct the amount of skill points already used
        points -= GetUsedSkillPoints(AbilityType.General);

        // Check if we have enough remaining to learn this Skill
        if (template.SkillPoints > points)
            return;

        // Check if we already learned it
        if (Skills.TryGetValue(skillId, out var skill))
            Owner.SendPacket(new SCSkillLearnedPacket(skill));
        else
            AddSkill(template, 1, true);
    }

    /// <summary>
    /// Adds a Skill and optionally sends a SCSkillLearnedPacket
    /// </summary>
    /// <param name="template"></param>
    /// <param name="level"></param>
    /// <param name="packet"></param>
    public void AddSkill(SkillTemplate template, byte level, bool packet)
    {
        if (template == null || CharacterAbilities.IsSpecialAbility(template.AbilityId))
            return;

        var skill = new Skill
        {
            Id = template.Id,
            Template = template,
            Level = template.LevelStep > 0 ? (byte)((Owner.GetAbLevel(template.AbilityId) - template.AbilityLevel) / template.LevelStep + 1) : (byte)1
        };
        lock (_skillsLock)
            Skills.Add(skill.Id, skill);

        if (packet)
            Owner.SendPacket(new SCSkillLearnedPacket(skill));
    }

    /// <summary>
    /// Try to learn a Passive Skill
    /// </summary>
    /// <param name="buffId"></param>
    public void AddBuff(uint buffId)
    {
        // Check if what we want to learn is part of an active skill tree (or not part of one)
        var template = SkillManager.Instance.GetPassiveBuffTemplate(buffId);
        if (template == null || !CanLearnInTree(template.AbilityId))
            return;

        // Get total skill points for the player's level
        var points = ExperienceManager.Instance.GetSkillPointsForLevel(Owner.Level);

        // Deduct the amount of skill points already used
        points -= GetUsedSkillPoints(AbilityType.General);

        // Check if we have enough remaining to learn this Skill
        if (points < 1)
            return;

        // Check if there are enough points already invested in this tree to allow learning this Passive
        if (GetUsedSkillPoints(template.AbilityId) < template.ReqPoints)
            return;

        // Check if we already learned it
        if (PassiveBuffs.ContainsKey(buffId))
            return;

        // Add Passive Buff
        var buff = new PassiveBuff { Id = buffId, Template = template };
        PassiveBuffs.Add(buff.Id, buff);
        Owner.BroadcastPacket(new SCBuffLearnedPacket(Owner.ObjId, buff.Id), true);
        buff.Apply(Owner);
    }

    private bool CanLearnInTree(AbilityType ability) =>
        !CharacterAbilities.IsSpecialAbility(ability) &&
        (ability == AbilityType.General || ability == Owner.Ability1 || ability == Owner.Ability2 || ability == Owner.Ability3);

    /// <summary>
    /// Resets all skills from a specific ability Skill Tree
    /// </summary>
    /// <param name="abilityId"></param>
    public void Reset(AbilityType abilityId)
    {
        // TODO: with price...
        lock (_skillsLock)
        {
            if (RemoveHeirSkills(HeirSkillResetKind.Ability, abilityId, 0))
                Owner.SendPacket(new SCResetHeirSkillPacket((uint)HeirSkillResetKind.Ability, 0, (byte)abilityId));

            foreach (var skill in new List<Skill>(Skills.Values))
            {
                if (skill.Template.AbilityId != abilityId)
                    continue;
                Skills.Remove(skill.Id);
                _removed.Add(skill.Id);
            }
        }

        foreach (var buff in new List<PassiveBuff>(PassiveBuffs.Values))
        {
            if (buff.Template.AbilityId != abilityId)
                continue;
            buff.Remove(Owner);
            PassiveBuffs.Remove(buff.Id);
            _removed.Add(buff.Id);
        }

        Owner.BroadcastPacket(new SCSkillsResetPacket(Owner.ObjId, abilityId), true);
    }

    /// <summary>
    /// Get skill points invested in total or for a specific tree
    /// </summary>
    /// <param name="ability">Ability whose Skill Tree to check. Use AbilityType.General if you want the total for all learned skills</param>
    /// <returns>Number of skill points invested</returns>
    private int GetUsedSkillPoints(AbilityType ability)
    {
        var points = 0;

        // Count points for Active Skills
        foreach (var skill in Skills.Values)
            if (ability == AbilityType.General || skill.Template.AbilityId == ability)
                points += skill.Template.SkillPoints;

        // Count points for Passive Skills (for Version 1.2)
        foreach (var buff in PassiveBuffs.Values)
            if (ability == AbilityType.General || buff.Template.AbilityId == ability)
                points += 1; // buff.Template?.ReqPoints ?? 1;

        return points;
    }

    internal bool IsLearned(uint skillId)
    {
        lock (_skillsLock)
            return Skills.ContainsKey(skillId);
    }

    internal void OpenComboFollowUp(uint skillId, int durationMilliseconds)
    {
        if (skillId == 0 || durationMilliseconds <= 0)
            return;

        lock (_skillsLock)
        {
            _comboFollowUps ??= [];
            var expires = Environment.TickCount64 + durationMilliseconds;
            _comboFollowUps[skillId] = Math.Max(expires, _comboFollowUps.GetValueOrDefault(skillId));
        }
    }

    internal bool HasComboFollowUp(uint skillId)
    {
        lock (_skillsLock)
        {
            if (_comboFollowUps?.TryGetValue(skillId, out var expires) != true)
                return false;
            if (expires > Environment.TickCount64)
                return true;

            _comboFollowUps.Remove(skillId);
            return false;
        }
    }

    internal bool HasNamedVariant(SkillTemplate template)
    {
        if (template.AbilityId is AbilityType.General or AbilityType.None || template.Show || string.IsNullOrEmpty(template.Name))
            return false;

        lock (_skillsLock)
        {
            foreach (var skill in Skills.Values)
            {
                if (skill.Template is { } learned && learned.AbilityId == template.AbilityId &&
                    learned.AbilityLevel == template.AbilityLevel && learned.Name == template.Name)
                    return true;
            }
        }

        return false;
    }

    #region database
    public void Load(MySqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM skills WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var type = Enum.Parse<SkillType>(reader.GetString("type"), true);
                    switch (type)
                    {
                        case SkillType.Skill:
                            var skill = new Skill
                            {
                                Id = reader.GetUInt32("id"),
                                Level = reader.GetByte("level")
                            };
                            AddSkill(skill.Id);
                            break;
                        case SkillType.Buff:
                            var buffId = reader.GetUInt32("id");
                            var template = SkillManager.Instance.GetPassiveBuffTemplate(buffId);
                            if (template == null || CharacterAbilities.IsSpecialAbility(template.AbilityId))
                                break;
                            var buff = new PassiveBuff { Id = buffId, Template = template };
                            PassiveBuffs.Add(buff.Id, buff);
                            buff.Apply(Owner);
                            break;
                    }
                }
            }
        }

        foreach (var skill in Skills.Values)
            if (skill != null)
                skill.Template = SkillManager.Instance.GetSkillTemplate(skill.Id);

        LoadHeirSkills(connection);
        CharacterSkillCooldownPersistence.Load(connection, Owner.Id, Owner.Cooldowns, DateTime.UtcNow);
    }

    internal void Save(MySqlConnection connection, MySqlTransaction transaction, SkillAuthorityGameData.HeirSkill[] heirSelections)
    {
        if (_removed.Count > 0)
        {
            using var command = connection.CreateCommand();
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = "DELETE FROM skills WHERE owner = @owner AND id IN(" + string.Join(",", _removed) + ")";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.Prepare();
            command.ExecuteNonQuery();
            _removed.Clear();
        }

        foreach (var skill in Skills.Values)
        {
            using var command = connection.CreateCommand();
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = "REPLACE INTO skills(`id`,`level`,`type`,`owner`) VALUES (@id, @level, @type, @owner)";
            command.Parameters.AddWithValue("@id", skill.Id);
            command.Parameters.AddWithValue("@level", skill.Level);
            command.Parameters.AddWithValue("@type", (byte)SkillType.Skill);
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.ExecuteNonQuery();
        }

        foreach (var buff in PassiveBuffs.Values)
        {
            using var command = connection.CreateCommand();
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = "REPLACE INTO skills(`id`,`level`,`type`,`owner`) VALUES(@id,@level,@type,@owner)";
            command.Parameters.AddWithValue("@id", buff.Id);
            command.Parameters.AddWithValue("@level", 1);
            command.Parameters.AddWithValue("@type", (byte)SkillType.Buff);
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.ExecuteNonQuery();
        }

        if (heirSelections != null)
            SaveHeirSkills(connection, transaction, heirSelections);

        CharacterSkillCooldownPersistence.Save(connection, transaction, Owner.Id, Owner.Cooldowns, DateTime.UtcNow);
    }

    #endregion
}
