using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items.Procs;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Models.Game.Units;

public class UnitProcs(Unit owner)
{
    private readonly object _lock = new();
    private readonly Dictionary<uint, ItemProc> _knownProcs = [];
    private Dictionary<ProcChanceKind, List<ItemProc>> _procsByChanceKind = [];
    private HashSet<uint> _activeProcIds = [];

    public Unit Owner { get; } = owner;

    internal void SetActiveProcs(HashSet<uint> procIds)
    {
        lock (_lock)
        {
            if (_activeProcIds.SetEquals(procIds))
                return;

            foreach (var previous in _activeProcIds)
            {
                if (!procIds.Contains(previous))
                    _knownProcs[previous].SetEnabled(false);
            }

            HashSet<uint> activeProcIds = [];
            Dictionary<ProcChanceKind, List<ItemProc>> byKind = [];
            foreach (var id in procIds)
            {
                if (!_knownProcs.TryGetValue(id, out var proc))
                {
                    if (ItemManager.Instance.GetItemProcTemplate(id) is not { } template)
                        continue;
                    proc = new ItemProc(template);
                    _knownProcs.Add(id, proc);
                }

                proc.SetEnabled(true);
                activeProcIds.Add(id);
                if (!byKind.TryGetValue(proc.Template.ChanceKind, out var procs))
                {
                    procs = [];
                    byKind.Add(proc.Template.ChanceKind, procs);
                }
                procs.Add(proc);
            }

            _activeProcIds = activeProcIds;
            _procsByChanceKind = byKind;
        }
    }

    public void RollProcsForKind(ProcChanceKind kind, Unit target, Skill triggeringSkill, bool finishing = false)
    {
        List<ItemProc> procs;
        lock (_lock)
            if (!_procsByChanceKind.TryGetValue(kind, out procs))
                return;

        foreach (var proc in procs)
            proc.Apply(Owner, target, triggeringSkill, finishing);
    }

    internal void RollDamageProcs(Unit target, Skill triggeringSkill, DamageType damageType, SkillHitType hit, bool received)
    {
        var finishing = !received && target is { Hp: <= 0 };
        RollProcsForKind(received ? ProcChanceKind.TakeDamageAny : ProcChanceKind.HitAny, target, triggeringSkill, finishing);
        var kind = damageType switch
        {
            DamageType.Melee => received ? ProcChanceKind.TakeDamageMelee : ProcChanceKind.HitMelee,
            DamageType.Magic => received ? ProcChanceKind.TakeDamageSpell : ProcChanceKind.HitSpell,
            DamageType.Ranged => received ? ProcChanceKind.TakeDamageRanged : ProcChanceKind.HitRanged,
            DamageType.Siege => received ? ProcChanceKind.TakeDamageSiege : ProcChanceKind.HitSiege,
            _ => (ProcChanceKind?)null
        };
        if (kind is { } damageKind)
            RollProcsForKind(damageKind, target, triggeringSkill, finishing);

        var criticalKind = hit switch
        {
            SkillHitType.MeleeCritical => received ? ProcChanceKind.TakeDamageMeleeCrit : ProcChanceKind.HitMeleeCrit,
            SkillHitType.SpellCritical => received ? ProcChanceKind.TakeDamageSpellCrit : ProcChanceKind.HitSpellCrit,
            SkillHitType.RangedCritical => received ? ProcChanceKind.TakeDamageRangedCrit : ProcChanceKind.HitRangedCrit,
            _ => (ProcChanceKind?)null
        };
        if (criticalKind is { } critical)
            RollProcsForKind(critical, target, triggeringSkill, finishing);
    }
}
