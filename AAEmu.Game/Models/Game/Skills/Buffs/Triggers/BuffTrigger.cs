using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using NLog;
using GameTask = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.Game.Models.Game.Skills.Buffs.Triggers;

public class BuffTrigger(Buff buff, BuffTriggerTemplate template)
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    protected readonly Buff _buff = buff;
    protected readonly BaseUnit _owner = buff.Owner;
    private HashSet<DeferredTriggerTask> _pending;
    public BuffTriggerTemplate Template { get; set; } = template;

    public virtual void Execute(object sender, EventArgs eventArgs) => Fire(eventArgs, Template.DelayTime);

    internal void RearmTimeTrigger()
    {
        CancelPending();
        var offset = Template.DelayTime;
        if (offset < 0)
            offset = Math.Max(0, _buff.Duration + offset);

        var delay = _buff.StartTime.AddMilliseconds(offset) - DateTime.UtcNow;
        Queue(() => Fire(EventArgs.Empty, 0), delay > TimeSpan.Zero ? delay : TimeSpan.Zero, terminal: false);
    }

    internal void CancelPending()
    {
        lock (_buff.SyncRoot)
        {
            if (_pending == null)
                return;
            foreach (var task in _pending)
                task.Cancel();
            _pending.Clear();
        }
    }

    private void Fire(EventArgs eventArgs, int delayMs)
    {
        if (_owner is not Unit || _buff.WorldEffectsDetached || Template.Effect is not { } effect)
            return;

        var terminal = Template.Kind is BuffEventTriggerKind.Timeout or BuffEventTriggerKind.Dispelled or
            BuffEventTriggerKind.Any or BuffEventTriggerKind.Breaker or BuffEventTriggerKind.Immortality;
        if (!terminal && (!_buff.InUse || _buff.IsEnded()))
            return;
        if (eventArgs is OnKillArgs kill && (kill.Killer == null || kill.Victim == null || ReferenceEquals(kill.Killer, kill.Victim)))
            return;

        var (eventSource, eventTarget, damage) = eventArgs switch
        {
            OnAttackArgs attack => ((BaseUnit)attack.Attacker, attack.Receiver, 0),
            OnAttackedArgs attacked => (attacked.Attacker, attacked.Receiver, 0),
            OnDamageArgs dealt => ((BaseUnit)dealt.Attacker, dealt.Receiver, dealt.Amount),
            OnDamagedArgs received => ((BaseUnit)received.Attacker, received.Receiver, received.Amount),
            OnDamagedCollisionArgs collision => (collision.Attacker, collision.Receiver, collision.Amount),
            OnBrokenArgs broken => (broken.Breaker, _owner, 0),
            OnImmortalityArgs immortality => (immortality.Attacker, _owner, immortality.Amount),
            OnDeathArgs death => ((BaseUnit)death.Killer, death.Victim, 0),
            OnKillArgs killed => ((BaseUnit)killed.Killer, killed.Victim, 0),
            _ => (null, null, 0)
        };
        var source = Template.UseOriginalSource
            ? _buff.Caster
            : ResolveAgent(Template.SourceAgentId, eventSource, eventTarget);
        var target = Template.EffectOnSource
            ? source
            : ResolveAgent(Template.TargetAgentId, eventSource, eventTarget);
        if (source == null || target == null)
            return;

        var requiredSource = Template.CheckTagSrcInOwner ? _owner : Template.CheckTagSrcInTarget ? target : source;
        var excludedSource = Template.CheckNoTagSrcInOwner ? _owner : Template.CheckNoTagSrcInTarget ? target : source;
        if (!MatchesTags(_owner, Template.OwnerBuffTagId, Template.OwnerNoBuffTagId) ||
            !MatchesTags(requiredSource, Template.SourceBuffTagId, 0) ||
            !MatchesTags(excludedSource, 0, Template.SourceNoBuffTagId) ||
            !MatchesTags(target, Template.TargetBuffTagId, Template.TargetNoBuffTagId))
            return;

        var amount = Template.UseDamageAmount ? damage : 1000;
        if (Template.UseStackCount)
            amount = (int)Math.Clamp((long)amount * Math.Max(1, _buff.Stack), int.MinValue, int.MaxValue);

        var sourceContext = new EffectSource(_buff.Skill, _buff.Template) { IsTrigger = true, Amount = amount };
        if (delayMs > 0)
        {
            QueueEffect(effect, source, target, sourceContext, delayMs, terminal);
            return;
        }

        Apply(effect, source, target, sourceContext);
    }

    private void QueueEffect(EffectTemplate effect, BaseUnit source, BaseUnit target, EffectSource context, int delayMs, bool terminal)
    {
        // Capture the event's units and amount before releasing it to the scheduler.
        Queue(() => Apply(effect, source, target, context), TimeSpan.FromMilliseconds(delayMs), terminal);
    }

    private void Apply(EffectTemplate effect, BaseUnit source, BaseUnit target, EffectSource sourceContext)
    {
        if (_buff.WorldEffectsDetached)
            return;

        effect.Apply(source, new SkillCasterUnit(source.ObjId), target, new SkillCastUnitTarget(target.ObjId),
            new CastBuff(_buff), sourceContext, null, DateTime.UtcNow);
    }

    private void Queue(Action apply, TimeSpan delay, bool terminal)
    {
        var task = new DeferredTriggerTask(this, apply, terminal);
        if (terminal)
        {
            // Terminal events describe a buff that has already ended.
            TaskManager.Instance.Schedule(task, delay);
            return;
        }

        lock (_buff.SyncRoot)
        {
            if (_buff.WorldEffectsDetached || !_buff.InUse || _buff.IsEnded())
                return;

            (_pending ??= []).Add(task);
            if (!TaskManager.Instance.Schedule(task, delay))
                _pending.Remove(task);
        }
    }

    private bool TryClaim(DeferredTriggerTask task)
    {
        lock (_buff.SyncRoot)
            return _pending?.Remove(task) == true && !_buff.WorldEffectsDetached && _buff.InUse && !_buff.IsEnded();
    }

    private sealed class DeferredTriggerTask(BuffTrigger trigger, Action apply, bool terminal) : GameTask
    {
        private Action _apply = apply;

        public override void Execute()
        {
            if (terminal || trigger.TryClaim(this))
                Interlocked.Exchange(ref _apply, null)?.Invoke();
        }
    }

    private BaseUnit ResolveAgent(uint agent, BaseUnit source, BaseUnit target)
    {
        // enum_buff_trigger_agents uses owner, event source, event target and original caster.
        return agent switch
        {
            0 => _owner,
            1 => source ?? _buff.Caster,
            2 => target ?? _owner,
            3 => _buff.Caster,
            _ => null
        };
    }

    private static bool MatchesTags(BaseUnit unit, uint required, uint forbidden) =>
        (required == 0 || unit.Buffs.CheckBuffTag(required)) &&
        (forbidden == 0 || !unit.Buffs.CheckBuffTag(forbidden));
}
