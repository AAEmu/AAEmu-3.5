using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char;

public partial class CharacterSkills
{
    internal enum HeirSkillResetKind : uint
    {
        All = 1,
        Ability = 2,
        Successor = 3
    }

    private readonly Dictionary<ushort, SkillAuthorityGameData.HeirSkill> _heirSelections = [];
    private bool _saveHeirSelections;
    private readonly SkillAuthorityGameData _skillData;
    private readonly ExperienceManager _experience;

    internal bool TryActivateHeirSkill(ushort groupId, ushort successorId, bool isChange)
    {
        if (!_skillData.TryGetHeirSkill(successorId, out var heir) || heir.GroupId != groupId)
            return false;

        lock (_skillsLock)
        {
            if (!CanSelectHeirSkill(heir))
                return false;

            var hasSelection = _heirSelections.TryGetValue(groupId, out var previous);
            if (hasSelection != isChange || previous.SuccessorId == successorId)
                return false;

            var points = heir.Points - previous.Points;
            foreach (var selection in _heirSelections.Values)
                points += selection.Points;
            if (points > Owner.HeirLevel || isChange && (!CanResetHeirSkills() || !PayHeirResetCost(1)))
                return false;

            _heirSelections[groupId] = heir;
            _saveHeirSelections = true;
            Owner.SendPacket(new SCActivatedHeirSkillPacket(groupId, successorId, isChange));
            return true;
        }
    }

    internal bool TryResetHeirSkills(uint resetKind, AbilityType ability, ushort successorId)
    {
        var kind = (HeirSkillResetKind)resetKind;
        if (kind is not (HeirSkillResetKind.All or HeirSkillResetKind.Ability or HeirSkillResetKind.Successor))
            return false;

        lock (_skillsLock)
        {
            if (!CanResetHeirSkills() || !RemoveHeirSkills(kind, ability, successorId, charge: true))
                return false;

            Owner.SendPacket(new SCResetHeirSkillPacket(resetKind, successorId, (byte)ability));
            return true;
        }
    }

    internal bool IsSelectedHeirSkill(SkillAuthorityGameData.HeirSkill heir)
    {
        lock (_skillsLock)
        {
            return _heirSelections.TryGetValue(heir.GroupId, out var saved) && saved.SuccessorId == heir.SuccessorId &&
                   HasUnlockedStep(heir) && Skills.ContainsKey(heir.OriginId) && HasActiveAbility(heir.Ability);
        }
    }

    internal SCHeirSkillListPacket BuildHeirSkillListPacket()
    {
        lock (_skillsLock)
        {
            var entries = new List<SCHeirSkillListPacket.HeirSkillEntry>(_heirSelections.Count);
            foreach (var selection in _heirSelections.Values)
            {
                if (!HasUnlockedStep(selection) || !Skills.ContainsKey(selection.OriginId) || !HasActiveAbility(selection.Ability))
                    continue;

                var abilityLevel = GetHeirAbilityLevel(selection.Ability);
                var rank = abilityLevel < selection.RequiredAbilityLevel
                    ? 0u
                    : selection.LevelStep == 0 ? 1u : (uint)((abilityLevel - selection.RequiredAbilityLevel) / selection.LevelStep + 1);
                entries.Add(new SCHeirSkillListPacket.HeirSkillEntry(selection.GroupId, selection.OriginId,
                    selection.SuccessorId, rank, (byte)selection.Ability, selection.HighAbility, true));
            }

            return new SCHeirSkillListPacket([.. entries]);
        }
    }

    private bool CanSelectHeirSkill(SkillAuthorityGameData.HeirSkill heir) =>
        HasUnlockedStep(heir) &&
        Skills.ContainsKey(heir.OriginId) && HasActiveAbility(heir.Ability) &&
        GetHeirAbilityLevel(heir.Ability) >= ExperienceManager.HeirUnlockLevel;

    private bool HasUnlockedStep(SkillAuthorityGameData.HeirSkill heir) =>
        Owner.HeirLevel > 0 && (_experience.GetHeirLevelTemplate(Owner.HeirLevel)?.Step ?? 0) >= heir.Step;

    private bool HasActiveAbility(AbilityType ability) => ability != AbilityType.None &&
        (Owner.Ability1 == ability || Owner.Ability2 == ability || Owner.Ability3 == ability);

    private int GetHeirAbilityLevel(AbilityType ability) =>
        Owner.Abilities?.Abilities.TryGetValue(ability, out var entry) == true
            ? _experience.GetLevelFromExp(entry.Exp, out _)
            : 0;

    private bool CanResetHeirSkills() => !Owner.IsInBattle && Owner.SkillTask == null && Owner.ActiveSkillController == null;

    private bool RemoveHeirSkills(HeirSkillResetKind kind, AbilityType ability, ushort successorId, bool charge = false)
    {
        var removals = new List<ushort>();
        foreach (var (groupId, selection) in _heirSelections)
        {
            if (kind == HeirSkillResetKind.All || kind == HeirSkillResetKind.Ability && selection.Ability == ability ||
                kind == HeirSkillResetKind.Successor && selection.SuccessorId == successorId)
                removals.Add(groupId);
        }

        if (removals.Count == 0 || charge && !PayHeirResetCost(removals.Count))
            return false;

        foreach (var groupId in removals)
            _heirSelections.Remove(groupId);
        return true;
    }

    private bool PayHeirResetCost(int selectionCount)
    {
        // 3.5 client passes global constant 208 and the number of affected selections to formula 52.
        var formula = FormulaManager.Instance.GetFormula((uint)FormulaKind.ResetHeirSkillCost);
        if (formula == null)
            return false;

        var cost = Math.Round(formula.Evaluate(new Dictionary<string, double>
        {
            ["heir_level"] = Owner.HeirLevel,
            ["reset_heir_skill_cost"] = 100,
            ["reset_heir_skill_count"] = selectionCount
        }));

        return cost is > 0 and <= int.MaxValue &&
               Owner.ChangeMoney(SlotType.Inventory, SlotType.None, (int)cost, ItemTaskType.HeirSkillReset);
    }

    private void LoadHeirSkills(MySqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT heir_group_id, successor_id FROM character_heir_skills WHERE owner = @owner ORDER BY heir_group_id";
        command.Parameters.AddWithValue("@owner", Owner.Id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            _saveHeirSelections = true;
            var groupId = reader.GetUInt16(0);
            var successorId = reader.GetUInt16(1);
            if (_skillData.TryGetHeirSkill(successorId, out var heir) && heir.GroupId == groupId &&
                Skills.ContainsKey(heir.OriginId) && HasActiveAbility(heir.Ability))
                _heirSelections.TryAdd(groupId, heir);
        }
    }

    internal (long Money, uint Experience, SkillAuthorityGameData.HeirSkill[] Selections) CaptureHeirStateForSave()
    {
        lock (_skillsLock)
            return (Owner.Money, Owner.HeirExp, _saveHeirSelections ? [.. _heirSelections.Values] : null);
    }

    private void SaveHeirSkills(MySqlConnection connection, MySqlTransaction transaction, SkillAuthorityGameData.HeirSkill[] snapshot)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM character_heir_skills WHERE owner = @owner";
        command.Parameters.AddWithValue("@owner", Owner.Id);
        command.ExecuteNonQuery();

        command.CommandText = "INSERT INTO character_heir_skills (owner, heir_group_id, successor_id) VALUES (@owner, @group, @successor)";
        var groupId = command.Parameters.Add("@group", MySqlDbType.UInt16);
        var successorId = command.Parameters.Add("@successor", MySqlDbType.UInt16);
        foreach (var heir in snapshot)
        {
            groupId.Value = heir.GroupId;
            successorId.Value = heir.SuccessorId;
            command.ExecuteNonQuery();
        }
    }
}
