using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;
using GameTeam = AAEmu.Game.Models.Game.Team.Team;

namespace AAEmu.Game.Models.Game.Skills;

public partial class Buff
{
    private const int DefaultAuraIntervalMs = 1000;
    private const int LongLivedAuraGrantDurationMs = 60000;

    private AuraTask _auraTask;
    private bool _auraPulsing;
    private uint _auraRevision;
    private uint _auraGrantRevision;
    private Dictionary<Unit, Buff> _auraGrants;

    private readonly record struct AuraPulse(Unit Caster, SkillCaster Descriptor, ushort AbilityLevel, uint Revision);

    internal Buff AuraSource { get; private set; }

    private void StartAura()
    {
        if (!_worldEffectsDetached && AuraSource == null && Template.AuraRadius > 0 && Template.AuraSlaveBuffId > 0 &&
            _auraTask == null && !_auraPulsing)
            _auraTask = EffectTaskManager.Instance.AddAuraTask(this, 0);
    }

    private void UpdateAuraContext(Buff replacement)
    {
        if (Template.AuraRadius == 0 || Template.AuraSlaveBuffId == 0)
            return;

        var sameDescriptor = ReferenceEquals(SkillCaster, replacement.SkillCaster) ||
            SkillCaster is SkillCasterUnit oldUnit && replacement.SkillCaster is SkillCasterUnit newUnit &&
            oldUnit.Type == newUnit.Type && oldUnit.ObjId == newUnit.ObjId;
        if (!ReferenceEquals(Caster, replacement.Caster) || AbLevel != replacement.AbLevel || !sameDescriptor)
            _auraRevision++;
    }

    private bool IsCurrentAuraPulse(uint revision) =>
        State == EffectState.Acting && !_worldEffectsDetached && _auraRevision == revision;

    internal void RunAuraPulse(AuraTask task)
    {
        AuraPulse pulse;
        lock (SyncRoot)
        {
            if (State != EffectState.Acting || _worldEffectsDetached || !ReferenceEquals(_auraTask, task))
                return;

            _auraTask = null;
            _auraPulsing = true;
            pulse = new AuraPulse(Caster, SkillCaster, AbLevel, _auraRevision);
        }

        try
        {
            PulseAura(pulse);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Aura pulse failed for buff {0} on unit {1}", Template.Id, Owner.ObjId);
        }
        finally
        {
            lock (SyncRoot)
            {
                _auraPulsing = false;
                if (State == EffectState.Acting && !_worldEffectsDetached)
                {
                    var interval = Tick > 0 ? Math.Min(Tick, int.MaxValue) : DefaultAuraIntervalMs;
                    _auraTask = EffectTaskManager.Instance.AddAuraTask(this, interval);
                }
            }
        }
    }

    private void PulseAura(AuraPulse pulse)
    {
        var eligible = new HashSet<Unit>();
        var grant = SkillManager.Instance.GetBuffTemplate(Template.AuraSlaveBuffId);
        var caster = pulse.Caster;
        if (grant != null && caster != null && Owner.Region != null && Owner.ParentWorld is { } world)
        {
            var relation = (SkillTargetRelation)Template.AuraRelationId;
            var team = relation is SkillTargetRelation.Party or SkillTargetRelation.Raid
                ? TeamManager.Instance.GetTeamByObjId(caster.ObjId)
                : null;
            var radius = (float)Math.Max(0d, caster.ApplyBuffModifers(Template, BuffAttribute.AuraRadius, Template.AuraRadius));
            foreach (var candidate in world.GetUnitsWithinRadius(Owner, radius))
            {
                if (IsAuraRecipient(caster, candidate, relation, team))
                    eligible.Add(candidate);
            }

            if (Owner is Unit holder && IsAuraRecipient(caster, holder, relation, team))
                eligible.Add(holder);
        }

        // Release previous grants outside the source lock. A target can have an aura of its own.
        List<Buff> leaving = null;
        lock (SyncRoot)
        {
            if (!IsCurrentAuraPulse(pulse.Revision))
                return;

            if (_auraGrants != null)
            {
                foreach (var (unit, child) in _auraGrants)
                {
                    if (_auraGrantRevision == pulse.Revision && eligible.Contains(unit))
                        continue;

                    (leaving ??= []).Add(child);
                    _auraGrants.Remove(unit);
                }
            }
            _auraGrantRevision = pulse.Revision;
        }

        if (leaving != null)
        {
            foreach (var child in leaving)
                ReleaseAuraGrant(child);
        }

        foreach (var candidate in eligible)
            ApplyAuraGrant(candidate, pulse, grant);
    }

    private bool IsAuraRecipient(Unit caster, Unit candidate, SkillTargetRelation relation, GameTeam team)
    {
        if (candidate is not { ObjId: > 0, IsDead: false, Buffs: not null, Region: not null })
            return false;
        if (candidate.Transform.InstanceId != Owner.Transform.InstanceId)
            return false;

        var isCreator = ReferenceEquals(candidate, caster);
        if (Template.AuraCreatorOnly && !isCreator)
            return false;
        if (Template.AuraChildOnly && !isCreator && !ReferenceEquals(candidate.ParentObj, caster) &&
            !ReferenceEquals(candidate.GetOwnerCharacter(), caster))
            return false;

        if (relation is SkillTargetRelation.Party or SkillTargetRelation.Raid)
            return isCreator || (relation == SkillTargetRelation.Party && Template.AuraChildOnly) ||
                   team?.IsObjMember(candidate.ObjId) == true;

        return SkillTargetingUtil.IsRelationValid(relation, caster, candidate);
    }

    private void ApplyAuraGrant(Unit candidate, AuraPulse pulse, BuffTemplate grant)
    {
        lock (SyncRoot)
        {
            if (!IsCurrentAuraPulse(pulse.Revision))
                return;
        }

        if (candidate.Buffs.CheckBuff(grant.Id) || !grant.CanApplyTo(candidate, pulse.Caster))
            return;

        var child = new Buff(candidate, pulse.Caster, pulse.Descriptor, grant, null, DateTime.UtcNow)
        {
            AbLevel = pulse.AbilityLevel,
            AuraSource = this
        };
        // A momentary trigger can award a long-lived buff with its own duration (Doombringer in 3.5).
        var independent = Template.Duration is > 0 and <= DefaultAuraIntervalMs && grant.Duration >= LongLivedAuraGrantDurationMs;

        try
        {
            candidate.Buffs.AddBuff(child);
        }
        finally
        {
            if (child.InUse)
            {
                var tracked = false;
                lock (SyncRoot)
                {
                    if (IsCurrentAuraPulse(pulse.Revision))
                    {
                        if (!independent)
                            (_auraGrants ??= [])[candidate] = child;
                        tracked = true;
                    }
                }

                if (!tracked)
                    ReleaseAuraGrant(child);
                else if (independent)
                {
                    lock (child.SyncRoot)
                        child.AuraSource = null;
                }
            }
        }
    }

    private void StopAura()
    {
        if (Template.AuraRadius == 0 || Template.AuraSlaveBuffId == 0)
            return;

        Dictionary<Unit, Buff> grants;
        lock (SyncRoot)
        {
            _auraTask?.Cancel();
            _auraTask = null;
            grants = _auraGrants;
            _auraGrants = null;
        }

        if (grants == null)
            return;

        foreach (var child in grants.Values)
            ReleaseAuraGrant(child);
    }

    private void ReleaseAuraGrant(Buff child)
    {
        try
        {
            lock (child.SyncRoot)
            {
                if (!ReferenceEquals(child.AuraSource, this) || !child.TryBeginDispel())
                    return;
            }

            child.CompleteDispel();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Aura release failed for buff {0} on unit {1}", child.Template.Id, child.Owner.ObjId);
        }
    }
}
