using System.Runtime.CompilerServices;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Units;

public partial class Buffs
{
    private const uint PackCompanionsBuffId = 1857;
    private static readonly ConditionalWeakTable<Buffs, object> s_crowdHolders = new();
    private static readonly object s_crowdHolderMarker = new();
    private static int s_crowdPassRunning;
    private uint _crowdRevision;

    private readonly record struct CrowdChange(Buffs Collection, Unit Holder, WorldInstance World, Buff Effect,
        uint Revision, bool Remove);

    private static bool IsCrowdSource(Buff buff) =>
        buff.Template is { CrowdBuffId: > 0, CrowdNumber: > 0, CrowdRadius: > 0 } template &&
        template.CrowdBuffId != template.Id;

    private void RegisterCrowdSource(Buff buff)
    {
        if (!buff.InUse || buff.IsEnded() || buff.Owner is not Unit || !IsCrowdSource(buff))
            return;

        _crowdRevision++;
        s_crowdHolders.GetValue(this, static _ => s_crowdHolderMarker);
    }

    internal static void RecheckCrowdBuffs()
    {
        if (Interlocked.Exchange(ref s_crowdPassRunning, 1) != 0)
            return;

        try
        {
            // A grant can trigger effects that remove crowd sources. Measure each holder before applying any grants.
            var changes = new List<CrowdChange>();
            foreach (var (buffs, _) in s_crowdHolders)
            {
                try
                {
                    buffs.EvaluateCrowd(changes);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Failed to evaluate crowd buffs for unit {0}", buffs.GetOwner()?.ObjId);
                }
            }

            foreach (var change in changes)
            {
                try
                {
                    change.Collection.ApplyCrowdChange(change);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Failed to update crowd buffs for unit {0}", change.Holder.ObjId);
                }
            }
        }
        finally
        {
            Volatile.Write(ref s_crowdPassRunning, 0);
        }
    }

    private void EvaluateCrowd(List<CrowdChange> changes)
    {
        List<Buff> sources = null;
        List<Buff> grants = null;
        Unit holder;
        uint revision;
        lock (_lock)
        {
            holder = GetOwner() as Unit;
            if (holder != null && !_worldEffectsDetached)
            {
                foreach (var buff in _effects)
                {
                    if (!buff.InUse || buff.IsEnded())
                        continue;
                    if (IsCrowdSource(buff))
                        (sources ??= []).Add(buff);
                    if (buff.IsCrowdGrant && buff.Duration == 0)
                        (grants ??= []).Add(buff);
                }
            }

            if (sources == null && grants == null)
            {
                s_crowdHolders.Remove(this);
                return;
            }

            revision = _crowdRevision;
        }

        var world = holder.ParentWorld;
        var reached = new HashSet<uint>();
        if (sources != null && IsCrowdParticipant(holder, world))
        {
            foreach (var source in sources)
            {
                var template = source.Template;
                if (reached.Contains(template.CrowdBuffId) ||
                    SkillManager.Instance.GetBuffTemplate(template.CrowdBuffId) == null ||
                    !HasCrowd(holder, world, template))
                    continue;

                reached.Add(template.CrowdBuffId);
                if (!CheckBuff(template.CrowdBuffId))
                    changes.Add(new CrowdChange(this, holder, world, source, revision, Remove: false));
            }
        }

        if (grants == null)
            return;

        foreach (var grant in grants)
        {
            if (!reached.Contains(grant.Template.Id))
                changes.Add(new CrowdChange(this, holder, world, grant, revision, Remove: true));
        }
    }

    private static bool IsCrowdParticipant(Unit unit, WorldInstance world) =>
        world != null && unit is { ObjId: > 0, IsDead: false, Region: not null } &&
        ReferenceEquals(unit.ParentWorld, world) && unit.Transform.InstanceId == world.Id;

    private static bool HasCrowd(Unit holder, WorldInstance world, BuffTemplate source)
    {
        var count = source.CrowdFriendly || !source.CrowdHostile ? 1u : 0u;
        if (count >= source.CrowdNumber)
            return true;

        var counted = new HashSet<Unit>();
        foreach (var candidate in world.GetUnitsWithinRadius(holder, source.CrowdRadius))
        {
            if (!IsCrowdParticipant(candidate, world) || candidate.Buffs is not Buffs buffs)
                continue;

            if ((source.CrowdFriendly || source.CrowdHostile) &&
                !CrowdRelationMatches(holder.GetRelationStateTo(candidate), source))
                continue;

            if (buffs.HasCrowdSource(source) && counted.Add(candidate) && ++count >= source.CrowdNumber)
                return true;
        }

        return false;
    }

    private static bool CrowdRelationMatches(RelationState relation, BuffTemplate source) =>
        (source.CrowdFriendly && relation == RelationState.Friendly) ||
        (source.CrowdHostile && relation == RelationState.Hostile);

    private bool HasCrowdSource(BuffTemplate source)
    {
        lock (_lock)
        {
            if (_worldEffectsDetached)
                return false;
            foreach (var buff in _effects)
            {
                if (!buff.InUse || buff.IsEnded())
                    continue;

                // In compact 3.5, the pack variants share the grant. Tag 831 also includes special-purpose packs.
                if (source.CrowdBuffId == PackCompanionsBuffId
                        ? buff.Template.CrowdBuffId == PackCompanionsBuffId
                        : buff.Template.Id == source.Id)
                    return true;
            }

            return false;
        }
    }

    private void ApplyCrowdChange(CrowdChange change)
    {
        if (change.Remove)
        {
            var grant = change.Effect;
            lock (_lock)
            {
                if (_crowdRevision != change.Revision || !ReferenceEquals(change.Holder.ParentWorld, change.World) ||
                    !grant.IsCrowdGrant || grant.Duration != 0 || !grant.TryBeginDispel())
                    return;
            }

            grant.CompleteDispel();
            return;
        }

        var template = SkillManager.Instance.GetBuffTemplate(change.Effect.Template.CrowdBuffId);
        if (template == null)
            return;

        var holder = change.Holder;
        var buff = new Buff(holder, holder, new SkillCasterUnit(holder.ObjId), template, null, DateTime.UtcNow)
        {
            IsCrowdGrant = true
        };
        AddBuff(buff, 0, 0, change);
    }

    private bool CanApplyCrowdGrant(Buff buff, CrowdChange change) =>
        _crowdRevision == change.Revision && IsCrowdParticipant(change.Holder, change.World) &&
        change.Effect.InUse && !change.Effect.IsEnded() && !CheckBuff(buff.Template.Id) &&
        buff.Template.CanApplyTo(change.Holder, change.Holder);

    private static void RemovePermanentCrowdGrant(Buff buff)
    {
        lock (buff.SyncRoot)
        {
            if (!buff.IsCrowdGrant || buff.Duration != 0 || !buff.TryBeginDispel())
                return;
        }

        buff.CompleteDispel();
    }
}
