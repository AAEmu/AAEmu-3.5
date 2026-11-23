using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Buffs.Triggers;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Buffs;

public class BuffTriggersHandler(Buff buff)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly List<BuffTrigger> _triggers = [];

    public void SubscribeEvents()
    {
        foreach (var template in SkillManager.Instance.GetBuffTriggerTemplates(buff.Template.Id))
        {
            var trigger = new BuffTrigger(buff, template);
            if (template.Kind == BuffEventTriggerKind.Time || ChangeSubscription(trigger, subscribe: true))
                _triggers.Add(trigger);
            else
                Logger.Trace("Unimplemented buff trigger {0} for buff {1}", template.Kind, buff.Template.Id);
        }
    }

    public void UnsubscribeEvents()
    {
        foreach (var trigger in _triggers)
            if (trigger.Template.Kind != BuffEventTriggerKind.Time)
                ChangeSubscription(trigger, subscribe: false);

        _triggers.Clear();
    }

    internal void RearmTimeTriggers()
    {
        foreach (var trigger in _triggers)
            if (trigger.Template.Kind == BuffEventTriggerKind.Time && trigger.Template.Effect != null)
                trigger.RearmTimeTrigger();
    }

    internal void CancelDelayedTriggers()
    {
        foreach (var trigger in _triggers)
            trigger.CancelPending();
    }

    private bool ChangeSubscription(BuffTrigger trigger, bool subscribe)
    {
        switch (trigger.Template.Kind)
        {
            case BuffEventTriggerKind.Started:
                ChangeHandler(ref buff.Events.OnBuffStarted, trigger, subscribe);
                return true;
            case BuffEventTriggerKind.Dispelled:
                ChangeHandler(ref buff.Events.OnDispelled, trigger, subscribe);
                return true;
            case BuffEventTriggerKind.Timeout:
                ChangeHandler(ref buff.Events.OnTimeout, trigger, subscribe);
                return true;
            case BuffEventTriggerKind.Any:
                ChangeHandler(ref buff.Events.OnTimeout, trigger, subscribe);
                ChangeHandler(ref buff.Events.OnDispelled, trigger, subscribe);
                return true;
            case BuffEventTriggerKind.Breaker:
                ChangeHandler(ref buff.Events.OnBroken, trigger, subscribe);
                return true;
            case BuffEventTriggerKind.Immortality:
                ChangeHandler(ref buff.Events.OnImmortality, trigger, subscribe);
                return true;
        }

        if (buff.Owner is not Unit unit)
            return false;

        var events = unit.Events;
        switch (trigger.Template.Kind)
        {
            case BuffEventTriggerKind.Attack:
                ChangeHandler(ref events.OnAttack, trigger, subscribe);
                break;
            case BuffEventTriggerKind.Attacked:
                ChangeHandler(ref events.OnAttacked, trigger, subscribe);
                break;
            case BuffEventTriggerKind.Damage:
                ChangeHandler(ref events.OnDamage, trigger, subscribe);
                break;
            case BuffEventTriggerKind.DamageMelee:
                ChangeHandler(ref events.OnDamageMelee, trigger, subscribe);
                break;
            case BuffEventTriggerKind.DamageSpell:
                ChangeHandler(ref events.OnDamageSpell, trigger, subscribe);
                break;
            case BuffEventTriggerKind.DamageRanged:
                ChangeHandler(ref events.OnDamageRanged, trigger, subscribe);
                break;
            case BuffEventTriggerKind.DamageSiege:
                ChangeHandler(ref events.OnDamageSiege, trigger, subscribe);
                break;
            case BuffEventTriggerKind.Damaged:
            case BuffEventTriggerKind.RemoveOnDamage:
                ChangeHandler(ref events.OnDamaged, trigger, subscribe);
                break;
            case BuffEventTriggerKind.DamagedMelee:
                ChangeHandler(ref events.OnDamagedMelee, trigger, subscribe);
                break;
            case BuffEventTriggerKind.DamagedRanged:
                ChangeHandler(ref events.OnDamagedRanged, trigger, subscribe);
                break;
            case BuffEventTriggerKind.DamagedSpell:
                ChangeHandler(ref events.OnDamagedSpell, trigger, subscribe);
                break;
            case BuffEventTriggerKind.DamagedSiege:
                ChangeHandler(ref events.OnDamagedSiege, trigger, subscribe);
                break;
            case BuffEventTriggerKind.DamagedCollision:
                ChangeHandler(ref events.OnDamagedCollision, trigger, subscribe);
                break;
            case BuffEventTriggerKind.Landing:
                ChangeHandler(ref events.OnLanding, trigger, subscribe);
                break;
            case BuffEventTriggerKind.RemoveOnMove:
                ChangeHandler(ref events.OnMovement, trigger, subscribe);
                break;
            case BuffEventTriggerKind.ChannelingCancel:
                ChangeHandler(ref events.OnChannelingCancel, trigger, subscribe);
                break;
            case BuffEventTriggerKind.Death:
                if (subscribe)
                    events.OnDeath += trigger.Execute;
                else
                    events.OnDeath -= trigger.Execute;
                break;
            case BuffEventTriggerKind.Kill:
            case BuffEventTriggerKind.KillAny:
                ChangeHandler(ref events.OnKill, trigger, subscribe);
                break;
            case BuffEventTriggerKind.Unmount:
                ChangeHandler(ref events.OnUnmount, trigger, subscribe);
                break;
            default:
                return false;
        }

        return true;
    }

    private static void ChangeHandler<TArgs>(ref EventHandler<TArgs> handlers, BuffTrigger trigger,
        bool subscribe) where TArgs : EventArgs
    {
        EventHandler<TArgs> handler = trigger.Execute;
        EventHandler<TArgs> current;
        EventHandler<TArgs> updated;
        do
        {
            current = handlers;
            updated = subscribe
                ? (EventHandler<TArgs>)Delegate.Combine(current, handler)
                : (EventHandler<TArgs>)Delegate.Remove(current, handler);
        } while (!ReferenceEquals(Interlocked.CompareExchange(ref handlers, updated, current), current));
    }
}
