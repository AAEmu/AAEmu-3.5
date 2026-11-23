using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Tasks.Skills;

public class CastTask(
    Skill skill,
    BaseUnit caster,
    SkillCaster casterCaster,
    BaseUnit target,
    SkillCastTarget targetCaster,
    SkillObject skillObject,
    int castTimeMs,
    long startedAtMs)
    : SkillTask(skill)
{
    private readonly Lock _releaseLock = new();
    private readonly bool _canReleaseEarly = caster is Character &&
        skill.Template is { CastingUseable: true, PlotOnly: false, Plot: null } && castTimeMs > 0;
    private bool _resolved;

    internal bool TryFireEarly(Unit unit, ushort timelineId, long nowMs)
    {
        if (!_canReleaseEarly || timelineId == 0 || !ReferenceEquals(unit, caster))
            return false;

        lock (_releaseLock)
        {
            if (_resolved || Skill.Cancelled || timelineId != Skill.TlId)
                return false;

            var elapsed = Math.Max(0, nowMs - startedAtMs);
            if (elapsed >= castTimeMs)
                return false;

            var progress = (int)(elapsed * 100 / castTimeMs);
            if (!IsInEffectBand(progress) || !unit.TryClearSkillTask(this))
                return false;

            _resolved = true;
            Skill.EarlyCastingProgress = progress;
        }

        Fire();
        return true;
    }

    internal bool TryStop(Unit unit)
    {
        lock (_releaseLock)
        {
            if (_resolved || !unit.TryClearSkillTask(this))
                return false;

            _resolved = true;
            Skill.Cancelled = true;
            return true;
        }
    }

    public override void Execute()
    {
        var unit = (Unit)caster;
        bool displaced;
        lock (_releaseLock)
        {
            if (_resolved || Skill.Cancelled)
                return;

            displaced = !unit.TryClearSkillTask(this);
            _resolved = true;
        }

        if (displaced)
            Skill.FinishStoppedCast(unit);
        else
            Fire();
    }

    private bool IsInEffectBand(int progress)
    {
        if (progress < 1)
            return false;
        if (Skill.Template.Effects.Count == 0)
            return true;

        foreach (var effect in Skill.Template.Effects)
            if (effect.MatchesCastingUseProgress(progress))
                return true;

        return false;
    }

    private void Fire() => Skill.Cast(caster, casterCaster, target, targetCaster, skillObject);
}
