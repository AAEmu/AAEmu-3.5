using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Items.Procs;

/// <summary>Runtime cooldown and activation state for an equipped item proc.</summary>
public class ItemProc(ItemProcTemplate template)
{
    private readonly object _lock = new();
    private DateTime _activationTime;
    private DateTime _nextActivation;
    private bool _applying;
    private bool _removed;

    public ItemProcTemplate Template { get; } = template;

    internal bool Apply(Unit owner, Unit target, Skill triggeringSkill, bool finishing)
    {
        if (Template.SkillTemplate == null || (Template.Finisher && !finishing) ||
            !TryBegin(DateTime.UtcNow, Random.Shared.Next(100), triggeringSkill))
            return false;

        var success = false;
        try
        {
            var caster = new SkillCasterUnit(owner.ObjId);
            if (target != null && Template.SkillTemplate.TargetType == SkillTargetType.Friendly &&
                owner.GetRelationStateTo(target) is not (RelationState.Friendly or RelationState.Neutral))
                target = owner;
            var castTarget = new SkillCastUnitTarget(target?.ObjId ?? owner.ObjId);
            var skill = new Skill(Template.SkillTemplate);
            success = skill.Use(owner, caster, castTarget, new SkillObject { IsItemProc = true }, true, out _) ==
                SkillResult.Success;
            return success;
        }
        finally
        {
            Complete(success);
        }
    }

    internal bool TryBegin(DateTime now, int roll, Skill triggeringSkill)
    {
        lock (_lock)
        {
            if (_removed || _applying || now < _nextActivation || roll >= Template.ChanceRate ||
                (Template.TriggerSkillId != 0 && triggeringSkill?.Id != Template.TriggerSkillId) ||
                (Template.TriggerTagId != 0 && (triggeringSkill == null ||
                  !SkillManager.Instance.GetSkillTags(triggeringSkill.Id).Contains(Template.TriggerTagId))))
                return false;

            _activationTime = now;
            _applying = true;
            return true;
        }
    }

    internal void Complete(bool success)
    {
        lock (_lock)
        {
            if (success)
                _nextActivation = _activationTime.AddSeconds(Template.CooldownSec);
            _applying = false;
        }
    }

    internal void SetEnabled(bool enabled)
    {
        lock (_lock)
            _removed = !enabled;
    }
}
