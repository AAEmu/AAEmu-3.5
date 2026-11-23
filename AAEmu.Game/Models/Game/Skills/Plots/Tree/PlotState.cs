using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

public class PlotState(
    BaseUnit caster,
    SkillCaster casterCaster,
    BaseUnit target,
    SkillCastTarget targetCaster,
    SkillObject skillObject,
    Skill skill)
{
    private volatile bool _cancellationRequest;
    private bool _finishChanneling = false;
    private readonly Lock _castingLock = new();
    private PlotNode _castingExit;
    private long _castingStartedAt;
    private int _castingDurationMs;
    private int? _releasedCastingPercent;
    public Dictionary<uint, int> Tickets { get; set; } = [];
    public int[] Variables { get; set; } = new int[12];
    public int LastEffectedTargetCount { get; private set; }
    public byte CombatDiceRoll { get; set; }
    public bool IsCasting { get; set; }
    public bool IsChanneling { get; set; }
    internal int? CastingUsePercent { get; set; }

    public Skill ActiveSkill { get; set; } = skill;
    public Unit Caster { get; set; } = caster as Unit;
    public SkillCaster CasterCaster { get; set; } = casterCaster;
    public BaseUnit Target { get; set; } = target;
    public SkillCastTarget TargetCaster { get; set; } = targetCaster;
    public SkillObject SkillObject { get; set; } = skillObject;
    public List<(BaseUnit unit, uint buffId)> ChanneledBuffs { get; set; } = [];

    public Dictionary<uint, List<GameObject>> HitObjects { get; set; } = [];

    internal void ArmCastingUse(PlotNode exit, int durationMs, long startedAtMs)
    {
        if (durationMs <= 0 || exit.ParentNextEvent is not { Casting: true, CastingUseable: true } ||
            ActiveSkill?.Template?.CastingUseable != true)
            return;

        lock (_castingLock)
        {
            if (_castingExit != null)
                return;

            _castingExit = exit;
            _castingStartedAt = startedAtMs;
            _castingDurationMs = durationMs;
        }
    }

    internal bool TryRequestCastingUse(ushort plotTlId, long nowMs)
    {
        lock (_castingLock)
        {
            if (_cancellationRequest || _castingExit == null || plotTlId == 0 ||
                ActiveSkill?.TlId != plotTlId || _releasedCastingPercent.HasValue)
                return false;

            var elapsed = Math.Max(0, nowMs - _castingStartedAt);
            if (elapsed >= _castingDurationMs)
                return false;

            _releasedCastingPercent = (int)(elapsed * 100 / _castingDurationMs);
            return true;
        }
    }

    internal bool TryCompleteCasting(PlotNode exit, bool due, out int percent)
    {
        lock (_castingLock)
        {
            var isActiveExit = ReferenceEquals(exit, _castingExit);
            var release = isActiveExit ? _releasedCastingPercent : null;
            percent = release ?? 100;
            if (_cancellationRequest || !due && !release.HasValue)
                return false;

            if (isActiveExit)
            {
                _castingExit = null;
                _releasedCastingPercent = null;
            }
            return true;
        }
    }

    internal void ClearCastingUse()
    {
        lock (_castingLock)
        {
            _castingExit = null;
            _releasedCastingPercent = null;
        }
    }

    internal void UpdateEffectedTargetCount(PlotTargetInfo targetInfo)
    {
        var count = 0;
        foreach (var unit in targetInfo.EffectedTargets)
        {
            if (unit != null && unit.ObjId != 0 && unit.ObjId != uint.MaxValue)
                count++;
        }
        LastEffectedTargetCount = count;
    }

    public bool CancellationRequested() => _cancellationRequest;
    public bool RequestCancellation() => _cancellationRequest = true;
    public bool ChannelingFinishRequested() => _finishChanneling;
    public bool FinishChanneling() => _finishChanneling = true;
    public bool PermitChanneling() => _finishChanneling = false;
}
