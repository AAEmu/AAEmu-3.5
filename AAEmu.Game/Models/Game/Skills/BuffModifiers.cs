using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

public class BuffModifiers
{
    private readonly Dictionary<uint, List<BuffModifier>> _modifiersByBuffId = [];
    private readonly Dictionary<uint, List<BuffModifier>> _modifiersByTagId = [];
    private readonly Lock _lock = new();
    private IReadOnlyList<BuffModifier> _equipmentModifiers = [];

    public double ApplyModifiers(BuffTemplate buff, BuffAttribute attribute, double baseValue)
    {
        lock (_lock)
        {
            var value = baseValue;
            if (_modifiersByBuffId.TryGetValue(buff.Id, out var byBuff))
            {
                foreach (var modifier in byBuff)
                    if (modifier.BuffAttribute == attribute && modifier.UnitModifierType == UnitModifierType.Value)
                        value += modifier.Value;
                foreach (var modifier in byBuff)
                    if (modifier.BuffAttribute == attribute && modifier.UnitModifierType == UnitModifierType.Percent)
                        value += value * (modifier.Value / 100f);
            }

            if (_modifiersByTagId.Count == 0)
                return value;

            foreach (var tag in SkillManager.Instance.GetBuffTags(buff.Id))
            {
                if (!_modifiersByTagId.TryGetValue(tag, out var byTag))
                    continue;
                foreach (var modifier in byTag)
                    if (modifier.BuffAttribute == attribute)
                        value += modifier.UnitModifierType switch
                        {
                            UnitModifierType.Value => modifier.Value,
                            UnitModifierType.Percent => value * (modifier.Value / 100f),
                            _ => 0d
                        };
            }
            return value;
        }
    }

    public List<BuffModifier> GetModifiersForBuffIdWithAttribute(uint skillId, BuffAttribute attribute)
    {
        var modifiers = GetModifiersForBuffId(skillId);
        if (modifiers == null) return [];
        return modifiers.Where(mod => mod.BuffAttribute == attribute).ToList();
    }

    public List<BuffModifier> GetModifiersForTagIdWithAttribute(uint tagId, BuffAttribute attribute)
    {
        var modifiers = GetModifiersForTagId(tagId);
        if (modifiers == null) return [];
        return modifiers.Where(mod => mod.BuffAttribute == attribute).ToList();
    }

    public List<BuffModifier> GetModifiersForBuffId(uint skillId)
    {
        lock (_lock)
            return _modifiersByBuffId.TryGetValue(skillId, out var modifiers) ? [.. modifiers] : null;
    }

    public List<BuffModifier> GetModifiersForTagId(uint tagId)
    {
        lock (_lock)
            return _modifiersByTagId.TryGetValue(tagId, out var modifiers) ? [.. modifiers] : null;
    }

    public void AddModifiers(uint ownerId)
    {
        var modifiers = BuffGameData.Instance.GetModifiersForBuff(ownerId);
        lock (_lock)
            foreach (var modifier in modifiers)
                AddModifier(modifier);
    }

    public void RemoveModifiers(uint ownerId)
    {
        var modifiers = BuffGameData.Instance.GetModifiersForBuff(ownerId);
        lock (_lock)
            foreach (var modifier in modifiers)
                RemoveModifier(modifier);
    }

    public void AddModifier(BuffModifier modifier)
    {
        lock (_lock)
        {
            if (modifier.BuffId > 0)
                AddModifier(_modifiersByBuffId, modifier.BuffId, modifier);
            if (modifier.TagId > 0)
                AddModifier(_modifiersByTagId, modifier.TagId, modifier);
        }
    }

    public void RemoveModifier(BuffModifier modifier)
    {
        lock (_lock)
        {
            RemoveModifier(_modifiersByBuffId, modifier.BuffId, modifier);
            RemoveModifier(_modifiersByTagId, modifier.TagId, modifier);
        }
    }

    internal void ReplaceEquipmentModifiers(IReadOnlyList<BuffModifier> modifiers)
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

    private static void AddModifier(Dictionary<uint, List<BuffModifier>> byId, uint id, BuffModifier modifier)
    {
        if (!byId.TryGetValue(id, out var modifiers))
            byId[id] = modifiers = [];
        modifiers.Add(modifier);
    }

    private static void RemoveModifier(Dictionary<uint, List<BuffModifier>> byId, uint id, BuffModifier modifier)
    {
        if (!byId.TryGetValue(id, out var modifiers))
            return;
        modifiers.Remove(modifier);
        if (modifiers.Count == 0)
            byId.Remove(id);
    }
}
