using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Skills;
using NLog;

namespace AAEmu.Game.Models.Game.Skills;

public enum EffectState
{
    Created,
    Acting,
    Finishing,
    Finished
}

public partial class Buff
{
    protected static Logger Logger = LogManager.GetCurrentClassLogger();
    private const int HighAbilityPointsPerCharge = 100;

    private readonly object _fallbackLock = new();
    internal object SyncRoot => (Owner?.Buffs as Units.Buffs)?.SyncRoot ?? _fallbackLock;
    private Unit _sourceDeathSubscriber;
    private volatile bool _worldEffectsDetached;
    internal bool WorldEffectsDetached => _worldEffectsDetached;
    private DispelTask _dispelTask;
    private bool _dispelTaskRunning;
    private int _count;
    private bool _chargeInitialized;
    private ushort _abLevel;

    public uint Index { get; set; }
    public Skill Skill { get; set; }
    // public EffectTemplate Template { get; set; }
    public BuffTemplate Template { get; set; }
    public Unit Caster { get; set; }
    public SkillCaster SkillCaster { get; set; }
    public BaseUnit Owner { get; set; }
    public EffectState State { get; set; }
    public bool InUse { get; set; }
    public int Duration { get; set; }
    public int BaseDuration { get; set; }
    public double Tick { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int Charge { get; set; }
    public bool Passive { get; set; }
    public ushort AbLevel
    {
        get => Template?.FixAbilityLevelToOne == true ? (ushort)1 : _abLevel;
        set => _abLevel = value;
    }
    public bool IsRestored { get; set; }
    internal bool IsCrowdGrant { get; set; }
    public int Stack { get; set; } = 1;
    public int Count { get; set; } = 1;
    public BuffEvents Events { get; }
    public BuffTriggersHandler Triggers { get; }
    public Dictionary<uint, FactionsEnum> saveFactions { get; set; }

    public Buff(IBaseUnit owner, IBaseUnit caster, SkillCaster skillCaster, BuffTemplate template, Skill skill, DateTime time)
    {
        Owner = (BaseUnit)owner;
        Caster = caster as Unit;
        SkillCaster = skillCaster;
        Template = template;
        Skill = skill;
        StartTime = time;
        EndTime = DateTime.MinValue;
        AbLevel = 1;
        Events = new BuffEvents();
        Triggers = new BuffTriggersHandler(this);
        saveFactions = [];
        CaptureSourceLife();
    }

    private void ScheduleTimer()
    {
        if (Tick > 0 && (Duration > 0 || Template.TickEffects.Count > 0 || HighAbilityResourceTickCost > 0))
        {
            var time = GetTimeLeft();
            _count = time < 0 ? -1 : (int)Math.Min(int.MaxValue, time / Tick + 0.5f + 1);
            _dispelTask = EffectTaskManager.Instance.AddDispelTask(this, Tick);
        }
        else if (Duration > 0)
            _dispelTask = EffectTaskManager.Instance.AddDispelTask(this, GetTimeLeft());
    }

    private int HighAbilityResourceTickCost
    {
        get
        {
            var charges = Template.MinHighAbilityResource;
            return charges == Template.MaxHighAbilityResource && charges <= int.MaxValue / HighAbilityPointsPerCharge
                ? (int)charges * HighAbilityPointsPerCharge
                : 0;
        }
    }

    internal void RunTimer(DispelTask task)
    {
        Unit caster = null;
        Unit resourceOwner = null;
        SkillCaster skillCaster = null;
        var sourceRequired = false;
        bool timedOut;
        bool finished;
        lock (SyncRoot)
        {
            if (!ReferenceEquals(_dispelTask, task) || _dispelTaskRunning || IsEnded())
                return;

            if (_count > 0)
                _count--;
            timedOut = Tick <= 0 || _count == 0;
            finished = timedOut;

            if (!timedOut && HighAbilityResourceTickCost is > 0 and var cost)
            {
                finished = true;
                if (Owner is Unit owner && owner.TrySpendHighAbilityResource(cost, out var remaining))
                {
                    resourceOwner = owner;
                    finished = remaining < cost;
                }
            }

            if (finished)
            {
                _dispelTask = null;
                MarkFinished();
            }
            else
            {
                _dispelTaskRunning = true;
                caster = Caster;
                skillCaster = SkillCaster;
                sourceRequired = IsRestored;
            }
        }

        // Broadcasting and tick effects can reach other units.
        try
        {
            resourceOwner?.BroadcastHighAbilityResource();
            if (!finished && (caster != null || !sourceRequired))
                Template.TimeToTimeApply(caster, Owner, this, skillCaster);
        }
        finally
        {
            if (finished)
                CompleteFinish(replace: false, timedOut);
            else
            {
                lock (SyncRoot)
                {
                    if (ReferenceEquals(_dispelTask, task) && !IsEnded())
                    {
                        _dispelTaskRunning = false;
                        _dispelTask = EffectTaskManager.Instance.AddDispelTask(this, Tick);
                    }
                }
            }
        }
    }

    internal void Start()
    {
        lock (SyncRoot)
        {
            State = EffectState.Acting;
            InUse = true;
            SetSourceDeathSubscription(Template.RemoveOnSourceDead ? Caster : null);
            Tick = Template.GetTick();
            Template.Start(Caster, Owner, this);
            if (IsEnded())
                return;

            Triggers.RearmTimeTriggers();
            ScheduleTimer();
            StartAura();
            if ((Duration > 0 || Template.TickEffects.Count > 0) && Template.FactionId > 0 &&
                Owner is Unit { Faction: { } faction } owner)
            {
                Logger.Info($"Buff: buff={Template.BuffId}:{Index}, owner={owner.TemplateId}:{owner.ObjId}");
                saveFactions[owner.Id] = faction.Id;
                owner.SetFaction(Template.FactionId);
            }
        }
    }

    public void OverwriteWith(Buff newBuff)
    {
        lock (SyncRoot)
        {
            // Capture the remaining time before we update the StartTime.
            var remaining = GetTimeLeft();

            // Update buff properties from the new buff.
            UpdateAuraContext(newBuff);
            Charge = newBuff.Charge;
            _chargeInitialized = newBuff._chargeInitialized;
            AbLevel = newBuff.AbLevel;
            StopTrackingRestoredSource();
            Caster = newBuff.Caster;
            IsRestored = newBuff.IsRestored;
            RestoredSourceKind = newBuff.RestoredSourceKind;
            RestoredCasterId = newBuff.RestoredCasterId;
            SourceLife = newBuff.SourceLife;
            SetSourceDeathSubscription(Template.RemoveOnSourceDead ? Caster : null);
            SkillCaster = newBuff.SkillCaster;
            AuraSource = newBuff.AuraSource;
            IsCrowdGrant = newBuff.IsCrowdGrant;
            Skill = newBuff.Skill;
            BaseDuration = newBuff.BaseDuration;

            // Set StartTime to now.
            var now = DateTime.UtcNow;
            StartTime = now;

            // Update Duration based on the stack rule:
            if (Template.StackRule == BuffStackRule.Extend)
            {
                // Extend: new Duration = remaining time (from old timer) + newBuff.Duration.
                Duration = Template.ClampDuration(newBuff.Duration + (long)Math.Max(0, remaining));
            }
            else
            {
                // Refresh: new Duration = newBuff.Duration.
                Duration = newBuff.Duration;
            }

            // Recalculate EndTime based on the new StartTime and Duration.
            EndTime = Duration > 0 ? StartTime.AddMilliseconds(Duration) : DateTime.MinValue;

            CancelTimer();
            Tick = Template.GetTick();
            Template.Start(Caster, Owner, this);
            if (!IsEnded())
            {
                Triggers.RearmTimeTriggers();
                ScheduleTimer();
                StartAura();
            }
        }
    }

    internal void InitializeCharge()
    {
        if (!_chargeInitialized && !IsRestored && Charge == 0)
            Charge = Random.Shared.Next(Template.InitMinCharge, Math.Max(Template.InitMinCharge, Template.InitMaxCharge));
        _chargeInitialized = true;
    }

    internal void ExtendCharge(int incomingCharge)
    {
        lock (SyncRoot)
        {
            var maximum = Template.MaxCharge > 0 ? Template.MaxCharge : int.MaxValue;
            var charge = (int)Math.Clamp((long)Charge + Math.Max(0, incomingCharge), 0, maximum);
            if (charge == Charge)
                return;

            AuraSource = null;
            IsCrowdGrant = false;
            Charge = charge;
            StartAura();
            NotifyUpdated();
        }
    }

    internal void IncreaseStack(int stack)
    {
        AuraSource = null;
        IsCrowdGrant = false;
        Stack = stack;
        StartAura();
        Template.ApplyBonuses(Owner, this);
        NotifyUpdated();
    }

    private void NotifyUpdated()
    {
        if (!Passive)
            Owner.BroadcastPacket(new SCBuffUpdatedPacket(Owner.ObjId, Index, Stack, Charge), true);
    }

    internal int TakeStacks(int requested)
    {
        int taken;
        lock (SyncRoot)
        {
            if (State != EffectState.Acting)
                return 0;

            var before = Math.Max(1, Stack);
            taken = Math.Min(before, requested);
            if (taken < before)
            {
                Stack = before - taken;
                Template.ApplyBonuses(Owner, this);
                NotifyUpdated();
                return taken;
            }

            MarkFinished();
        }

        CompleteDispel();
        return taken;
    }

    public void Exit(bool replace = false)
    {
        if (TryBeginDispel())
            CompleteFinish(replace, timedOut: false);
    }

    internal bool TryBeginDispel()
    {
        lock (SyncRoot)
        {
            if (State != EffectState.Acting)
                return false;

            MarkFinished();
            return true;
        }
    }

    internal void CompleteDispel() => CompleteFinish(replace: false, timedOut: false);

    internal Buff TakeForSteal(Unit recipient, Unit stealer, Skill skill, out int remainingDuration)
    {
        lock (SyncRoot)
        {
            remainingDuration = 0;
            if (State != EffectState.Acting || WorldEffectsDetached)
                return null;

            if (Duration > 0)
            {
                remainingDuration = (int)GetTimeLeft();
                if (remainingDuration <= 0)
                    return null;
            }

            var taken = new Buff(recipient, stealer, new SkillCasterUnit(stealer.ObjId), Template, skill, DateTime.UtcNow)
            {
                AbLevel = AbLevel, Stack = Stack, Charge = Charge, _chargeInitialized = true
            };
            MarkFinished();
            return taken;
        }
    }

    internal void ExitSkillEffect(Skill skill, bool honorTimeout)
    {
        bool timedOut;
        lock (SyncRoot)
        {
            if (State != EffectState.Acting || !ReferenceEquals(Skill, skill))
                return;

            timedOut = honorTimeout && Duration > 0 && EndTime <= DateTime.UtcNow;
            MarkFinished();
        }

        CompleteFinish(replace: false, timedOut: timedOut);
    }

    internal void CancelTimer()
    {
        lock (SyncRoot)
        {
            var task = _dispelTask;
            _dispelTask = null;
            _dispelTaskRunning = false;
            task?.Cancel();
        }
    }

    internal void StopWorldEffects()
    {
        lock (SyncRoot)
        {
            if (_worldEffectsDetached)
                return;

            _worldEffectsDetached = true;
            SetSourceDeathSubscription(null);
            StopTrackingRestoredSource();
            Triggers.CancelDelayedTriggers();
            CancelTimer();
        }

        StopAura();
    }

    private void MarkFinished()
    {
        State = EffectState.Finished;
        InUse = false;
        SetSourceDeathSubscription(null);
        StopTrackingRestoredSource();
        Triggers.CancelDelayedTriggers();
        CancelTimer();
    }

    private void SetSourceDeathSubscription(Unit source)
    {
        if (ReferenceEquals(source, _sourceDeathSubscriber))
            return;

        if (_sourceDeathSubscriber != null)
            _sourceDeathSubscriber.Events.OnDeath -= OnSourceDied;

        _sourceDeathSubscriber = source;
        if (source != null)
            source.Events.OnDeath += OnSourceDied;
    }

    private void OnSourceDied(object sender, OnDeathArgs args)
    {
        lock (SyncRoot)
        {
            // A death notification already in flight may still belong to the previous caster.
            if (State != EffectState.Acting || _sourceDeathSubscriber == null || !ReferenceEquals(args?.Victim, _sourceDeathSubscriber))
                return;

            MarkFinished();
        }

        CompleteDispel();
    }

    private void CompleteFinish(bool replace, bool timedOut)
    {
        try
        {
            if (timedOut)
                Events.OnTimeout(this, new OnTimeoutArgs());
            else
                Events.OnDispelled(this, new OnDispelledArgs());
        }
        finally
        {
            StopAura();
            Triggers.UnsubscribeEvents();
            Owner.Buffs.RemoveEffect(this);
            Template.Dispel(Caster, Owner, this, replace);
            if ((Template.Taunt || Template.TauntWithTopAggro) && Owner is NPChar.Npc tauntedNpc)
                tauntedNpc.EndTaunt(Caster);

            if (Template.FactionId > 0 && Owner is NPChar.Npc npc)
                npc.SetFaction(npc.Template.FactionId);
            else if (Template.FactionId > 0 && Owner is Unit owner && saveFactions.Remove(owner.Id, out var factionId))
                owner.SetFaction(factionId);
        }
    }

    public bool IsEnded()
    {
        return State == EffectState.Finished || State == EffectState.Finishing;
    }

    public double GetTimeLeft()
    {
        if (Duration == 0)
            return -1;
        var time = (long)(StartTime.AddMilliseconds(Duration) - DateTime.UtcNow).TotalMilliseconds;
        return time > 0 ? time : 0;
    }

    public uint GetTimeElapsed()
    {
        var time = (uint)(DateTime.UtcNow - StartTime).TotalMilliseconds;
        return time > 0 ? time : 0;
    }

    public void WriteData(PacketStream stream)
    {
        if (Template == null)
        {
            //Logger.Error($"Buff.WriteData: Template is null, {GetLogContext()}");
            stream.WritePisc(Charge, Duration / 10, 0, 0);
            return;
        }

        var tickMs = Tick > 0 ? (long)(Tick / 10) : 0;
        var durationDiv10 = Template.StackRule is BuffStackRule.Extend or BuffStackRule.ChargeExtend
            ? BaseDuration / 10
            : Duration / 10;
        var elapsedDiv10 = (long)(GetTimeElapsed() / 10);
        /*Logger.Debug("[Buff.WriteData] BuffId={0}, sending Charge={1}, Duration/10={2}, Elapsed/10={3}, tickMs={4} (server Duration={5}ms, BaseDuration={6}ms)",
            Template.BuffId, Charge, durationDiv10, elapsedDiv10, tickMs, Duration, BaseDuration);*/
        stream.WritePisc(Charge, durationDiv10, elapsedDiv10, tickMs);
    }

    /// <summary>
    /// Consumes as much charge as possible. Remainder is returned
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public int ConsumeCharge(int value)
    {
        bool finished;
        lock (SyncRoot)
        {
            var newCharge = Math.Max(0, Charge - value);
            value = Math.Max(0, value - Charge);
            Charge = newCharge;
            finished = Charge == 0 && State == EffectState.Acting;
            if (finished)
                MarkFinished();
        }

        if (finished)
            CompleteFinish(replace: false, timedOut: false);

        return value;
    }
}
