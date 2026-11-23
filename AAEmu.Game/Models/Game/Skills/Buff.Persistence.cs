using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

public partial class Buff
{
    private Unit _trackedRestoredCaster;

    internal PersistedBuffSourceKind RestoredSourceKind { get; set; }
    internal uint RestoredCasterId { get; set; }
    internal Guid? SourceLife { get; set; }

    private void CaptureSourceLife()
    {
        if (Template?.RemoveOnSourceDead == true && Caster is Character && !ReferenceEquals(Caster, Owner))
            SourceLife = Caster.Events.BuffSourceLife;
    }

    internal bool PrepareRestoredSource()
    {
        if (!IsRestored || Caster is not Character caster || ReferenceEquals(caster, Owner))
            return true;

        if (Template.RemoveOnSourceDead)
        {
            SetSourceDeathSubscription(caster);
            if (caster.IsDead || SourceLife == null)
            {
                SetSourceDeathSubscription(null);
                return false;
            }
        }

        if (caster.Events.TryTrackBuffSource(OnRestoredSourceDetached, Template.RemoveOnSourceDead ? SourceLife : null))
        {
            _trackedRestoredCaster = caster;
            return true;
        }

        SetSourceDeathSubscription(null);
        if (Template.RemoveOnSourceDead)
            return false;

        Caster = null;
        SkillCaster = new SkillCasterUnit(0);
        return true;
    }

    private void StopTrackingRestoredSource()
    {
        _trackedRestoredCaster?.Events.UntrackBuffSource(OnRestoredSourceDetached);
        _trackedRestoredCaster = null;
    }

    private void OnRestoredSourceDetached()
    {
        var finished = false;
        lock (SyncRoot)
        {
            if (_trackedRestoredCaster == null || State != EffectState.Acting)
                return;

            StopTrackingRestoredSource();
            if (Template.RemoveOnSourceDead)
            {
                MarkFinished();
                finished = true;
            }
            else
            {
                Caster = null;
                SkillCaster = new SkillCasterUnit(0);
                _auraRevision++;
                Triggers.CancelDelayedTriggers();
            }
        }

        if (finished)
        {
            try
            {
                CompleteDispel();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to remove restored buff {0} after its caster logged out", Template.Id);
            }
        }
    }
}
