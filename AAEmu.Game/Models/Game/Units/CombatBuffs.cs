using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;

namespace AAEmu.Game.Models.Game.Units;

public class CombatBuffs(BaseUnit owner)
{
    private readonly object _lock = new();
    private readonly Dictionary<uint, int> _conditionCounts = [];
    private readonly Dictionary<SkillHitType, CombatBuffTemplate[]> _entries = [];

    public void AddCombatBuffs(uint buffId)
    {
        var templates = SkillManager.Instance.GetCombatBuffs(buffId);
        if (templates.Count == 0)
            return;

        lock (_lock)
        {
            var count = _conditionCounts.GetValueOrDefault(buffId);
            _conditionCounts[buffId] = count + 1;
            if (count > 0)
                return;

            foreach (var template in templates)
            {
                var entries = _entries.GetValueOrDefault(template.HitType) ?? [];
                _entries[template.HitType] = [.. entries, template];
            }
        }
    }

    public void RemoveCombatBuff(uint buffId)
    {
        lock (_lock)
        {
            if (!_conditionCounts.TryGetValue(buffId, out var count))
                return;
            if (count > 1)
            {
                _conditionCounts[buffId] = count - 1;
                return;
            }

            _conditionCounts.Remove(buffId);
            foreach (var template in SkillManager.Instance.GetCombatBuffs(buffId))
            {
                if (!_entries.TryGetValue(template.HitType, out var entries))
                    continue;
                var remaining = Array.FindAll(entries, entry => entry.ReqBuffId != buffId);
                if (remaining.Length == 0)
                    _entries.Remove(template.HitType);
                else
                    _entries[template.HitType] = remaining;
            }
        }
    }

    public void TriggerCombatBuffs(BaseUnit attacker, BaseUnit receiver, SkillHitType type, bool isHeal, Skill skill = null)
    {
        if (owner is not Unit unit || (!ReferenceEquals(owner, attacker) && !ReferenceEquals(owner, receiver)))
            return;

        CombatBuffTemplate[] entries;
        lock (_lock)
            if (!_entries.TryGetValue(type, out entries))
                return;

        var ownerIsAttacker = ReferenceEquals(owner, attacker);
        foreach (var entry in entries)
        {
            if (entry.IsHealSpell != isHeal || entry.BuffToSource != ownerIsAttacker ||
                (entry.HitSkillId != 0 && entry.HitSkillId != skill?.Id) || !owner.Buffs.CheckBuff(entry.ReqBuffId))
                continue;

            // Defensive rows grant the receiver's own passive bonus even when they carry buff_from_source.
            var template = SkillManager.Instance.GetBuffTemplate(entry.BuffId);
            if (template?.CanApplyTo(unit, unit, skill) == true)
                unit.Buffs.AddBuff(new Buff(unit, unit, new SkillCasterUnit(unit.ObjId), template, null, DateTime.UtcNow));
        }
    }
}