using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

public class SkillModifiers
{
    private readonly Dictionary<uint, List<SkillModifier>> _modifiersBySkillId = [];
    private readonly Dictionary<uint, List<SkillModifier>> _modifiersByTagId = [];
    private readonly object _lock = new();
    private IReadOnlyList<SkillModifier> _equipmentModifiers = [];

    public double ApplyModifiers(Skill skill, SkillAttribute attribute, double baseValue)
    {
        lock (_lock)
        {
            var skillId = skill.Template.Id;
            var value = baseValue;
            if (_modifiersBySkillId.TryGetValue(skillId, out var bySkill))
            {
                foreach (var modifier in bySkill)
                    if (modifier.SkillAttribute == attribute && modifier.UnitModifierType == UnitModifierType.Value)
                        value += modifier.Value;

                foreach (var modifier in bySkill)
                    if (modifier.SkillAttribute == attribute && modifier.UnitModifierType == UnitModifierType.Percent)
                        value += value * (modifier.Value / 100f);
            }

            if (_modifiersByTagId.Count == 0)
                return value;

            foreach (var tag in SkillManager.Instance.GetSkillTags(skillId))
            {
                if (!_modifiersByTagId.TryGetValue(tag, out var byTag))
                    continue;
                foreach (var modifier in byTag)
                {
                    if (modifier.SkillAttribute != attribute)
                        continue;
                    switch (modifier.UnitModifierType)
                    {
                        case UnitModifierType.Value:
                            value += modifier.Value;
                            break;
                        case UnitModifierType.Percent:
                            value += value * (modifier.Value / 100f);
                            break;
                    }
                }
            }

            return value;
        }
    }

    public List<SkillModifier> GetModifiersForSkillIdWithAttribute(uint skillId, SkillAttribute attribute)
    {
        var modifiers = GetModifiersForSkillId(skillId);
        if (modifiers == null) return [];
        return modifiers.Where(mod => mod.SkillAttribute == attribute).ToList();
    }

    public List<SkillModifier> GetModifiersForTagIdWithAttribute(uint tagId, SkillAttribute attribute)
    {
        var modifiers = GetModifiersForTagId(tagId);
        if (modifiers == null) return [];
        return modifiers.Where(mod => mod.SkillAttribute == attribute).ToList();
    }

    public List<SkillModifier> GetModifiersForSkillId(uint skillId)
    {
        lock (_lock)
            return _modifiersBySkillId.TryGetValue(skillId, out var modifiers) ? [.. modifiers] : null;
    }

    public List<SkillModifier> GetModifiersForTagId(uint tagId)
    {
        lock (_lock)
            return _modifiersByTagId.TryGetValue(tagId, out var modifiers) ? [.. modifiers] : null;
    }

    public void AddModifiers(uint ownerId)
    {
        var modifiers = SkillManager.Instance.GetModifiersByOwnerId(ownerId);
        if (modifiers == null)
            return;
        lock (_lock)
        {
            foreach (var modifier in modifiers)
                AddModifier(modifier);
        }
    }

    public void RemoveModifiers(uint ownerId)
    {
        var modifiers = SkillManager.Instance.GetModifiersByOwnerId(ownerId);
        if (modifiers == null)
            return;
        lock (_lock)
        {
            foreach (var modifier in modifiers)
                RemoveModifier(modifier);
        }
    }

    public void AddModifier(SkillModifier modifier)
    {
        lock (_lock)
        {
            if (modifier.SkillId > 0)
                AddModifier(_modifiersBySkillId, modifier.SkillId, modifier);
            if (modifier.TagId > 0)
                AddModifier(_modifiersByTagId, modifier.TagId, modifier);
        }
    }

    internal void ReplaceEquipmentModifiers(IReadOnlyList<SkillModifier> modifiers)
    {
        lock (_lock)
        {
            foreach (var previous in _equipmentModifiers)
                RemoveModifier(previous);
            foreach (var modifier in modifiers)
                AddModifier(modifier);
            _equipmentModifiers = modifiers;
        }
    }

    private static void AddModifier(Dictionary<uint, List<SkillModifier>> byId, uint id, SkillModifier modifier)
    {
        if (!byId.TryGetValue(id, out var modifiers))
        {
            modifiers = [];
            byId.Add(id, modifiers);
        }

        modifiers.Add(modifier);
    }

    public void RemoveModifier(SkillModifier modifier)
    {
        lock (_lock)
        {
            RemoveModifier(_modifiersBySkillId, modifier.SkillId, modifier);
            RemoveModifier(_modifiersByTagId, modifier.TagId, modifier);
        }
    }

    private static void RemoveModifier(Dictionary<uint, List<SkillModifier>> byId, uint id, SkillModifier modifier)
    {
        if (!byId.TryGetValue(id, out var modifiers))
            return;
        modifiers.Remove(modifier);
        if (modifiers.Count == 0)
            byId.Remove(id);
    }
}
