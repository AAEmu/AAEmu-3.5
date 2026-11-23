using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.StaticValues;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Units;

public partial class Buffs : IBuffs
{
    // ReSharper disable once InconsistentNaming
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    // [GEAR-SLOT-FIX] Slot reserved for gear bonuses (see Unit.UpdateGearBonuses,
    // which does `Bonuses[1] = []`). The buff allocator MUST skip this index, otherwise the first
    // passive buff applied at character load collides with gear and is wiped at login.
    // _nextIndex therefore starts at GearBonusesIndex + 1 and wraps back to it after uint.MaxValue.
    public const uint GearBonusesIndex = 1;
    private const uint FirstBuffIndex = GearBonusesIndex + 1;

    // ReSharper disable once ChangeFieldTypeToSystemThreadingLock
    private readonly object _lock = new();
    internal object SyncRoot => _lock;
    private bool _worldEffectsDetached;
    private bool _persistentBuffsLoaded;
    private uint _nextIndex;

    private WeakReference _owner;
    private readonly List<Buff> _effects;
    private readonly Dictionary<uint, BuffToleranceCounter> _toleranceCounters;

    public Buffs()
    {
        _nextIndex = FirstBuffIndex; // [GEAR-SLOT-FIX] skip GearBonusesIndex
        _effects = [];
        _toleranceCounters = [];
    }

    public Buffs(BaseUnit owner)
    {
        SetOwner(owner);
        _nextIndex = FirstBuffIndex; // [GEAR-SLOT-FIX] skip GearBonusesIndex
        _effects = [];
        _toleranceCounters = [];
    }

    public bool CheckBuffImmune(BuffTemplate candidate, BaseUnit caster, Skill castingSkill = null)
    {
        var owner = GetOwner();
        if (owner == null)
            return false;

        var manager = SkillManager.Instance;
        var candidateTags = manager.GetBuffTags(candidate.Id);
        if (candidateTags.Count == 0)
            return false;

        Buff[] effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        var data = BuffGameData.Instance;
        foreach (var effect in effects)
        {
            if (effect is not { InUse: true, Template: { } immunity } || effect.IsEnded())
                continue;

            var immuneTags = data.GetImmuneBuffTags(immunity.Id);
            if (immuneTags == null)
                continue;

            var blocked = false;
            foreach (var tag in candidateTags)
            {
                if (!immuneTags.Contains(tag))
                    continue;

                // A shared immunity tag permits the granting buff's own refresh or stack. An
                // exclusive tag still gates reapplication, as with internal cooldown buffs.
                if (immunity.Id == candidate.Id && CanReapplyToSharedTag(candidate) &&
                    manager.GetBuffsByTagId(tag) is { Count: > 1 })
                    continue;

                blocked = true;
                break;
            }

            if (!blocked)
                continue;

            if (immunity.ImmuneExceptCreator && caster != null &&
                (ReferenceEquals(caster, effect.Caster) || (caster.ObjId != 0 && caster.ObjId == effect.Caster?.ObjId)))
                continue;

            if (immunity.ImmuneExceptSkillTagId != 0 && castingSkill?.Template is { } skill &&
                manager.GetSkillTags(skill.Id).Contains(immunity.ImmuneExceptSkillTagId))
                continue;

            if (immunity.ImmuneExceptCreatorRelationCheck && caster != null &&
                immunity.ImmuneExceptCreatorRelationId is > 0 and <= byte.MaxValue)
            {
                var relation = (SkillTargetRelation)immunity.ImmuneExceptCreatorRelationId;
                if (Enum.IsDefined(relation) && SkillTargetingUtil.IsRelationValid(relation, caster, owner))
                    continue;
            }

            if (effect.InUse && !effect.IsEnded())
                return true;
        }

        return false;
    }

    private static bool CanReapplyToSharedTag(BuffTemplate template) => template.StackRule switch
    {
        BuffStackRule.Refresh or BuffStackRule.ChargeRefresh or BuffStackRule.Extend => true,
        BuffStackRule.Multiple => template.MaxStack > 1,
        _ => false
    };

    public bool CheckDamageImmune(DamageType damageType)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var effect in effects.ToList())
        {
            if (effect == null)
                continue;
            var template = effect.Template;

            if (template == null)
                continue;

            switch (damageType)
            {
                case DamageType.Melee:
                    if (template.MeleeImmune) return true;
                    continue;
                case DamageType.Magic:
                    if (template.SpellImmune) return true;
                    continue;
                case DamageType.Ranged:
                    if (template.RangedImmune) return true;
                    continue;
                case DamageType.Siege:
                    if (template.SiegeImmune) return true;
                    continue;
                default:
                    continue;
            }
        }

        return false;
    }

    public List<Buff> GetEffectsByType(Type effectType)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        var temp = new List<Buff>();
        foreach (var effect in effects.ToList())
            if (effect.Template.GetType() == effectType)
                temp.Add(effect);
        return temp;
    }

    public Buff GetEffectByIndex(uint index)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var effect in effects.ToList())
            if (effect.Index == index)
                return effect;
        return null;
    }

    public Buff GetEffectByTemplate(BuffTemplate template)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var effect in effects.ToList())
            if (effect.Template == template)
                return effect;
        return null;
    }

    public bool CheckBuff(uint id)
    {
        if (id == 0)
            return false;

        lock (_lock)
        {
            foreach (var buff in _effects)
            {
                if (!buff.IsEnded() && buff.Template.Id == id)
                    return true;
            }
        }

        return false;
    }

    public bool CheckBuffTag(uint tagId)
    {
        var buffs = SkillManager.Instance.GetBuffsByTagId(tagId);
        if (buffs == null)
            return false;

        lock (_lock)
        {
            foreach (var buff in _effects)
            {
                if (!buff.IsEnded() && buffs.Contains(buff.Template.Id))
                    return true;
            }
        }

        return false;
    }

    public Buff GetEffectFromBuffId(uint id)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var effect in effects.ToList())
            if (effect != null && effect.Template.BuffId > 0 && effect.Template.BuffId == id)
                return effect;
        return null;
    }

    public IEnumerable<Buff> GetBuffsRequiring(uint buffId)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        return effects.Where(b => b.Template.RequireBuffId == buffId);
    }

    public bool CheckBuffs(List<uint> ids)
    {
        if (ids is not { Count: not 0 })
            return false;

        var buffIdsSet = new HashSet<uint>(ids);

        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var effect in effects)
        {
            if (effect?.Template?.BuffId > 0 && buffIdsSet.Contains(effect.Template.BuffId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Same as <see cref="CheckBuffs"/> but ignores any active buff that ALSO carries
    /// one of <paramref name="excludedTagIds"/>. Used by the NPC movement gate to
    /// stop on pure Shackle / Snare roots but allow movement when the buff additionally
    /// carries the DecreaseMoveSpeed tag (slow debuffs that ride the Shackle family
    /// — e.g. Charged Bolt — should slow, not stop).
    /// </summary>
    public bool CheckBuffsExcludingTags(List<uint> ids, uint[] excludedTagIds)
    {
        if (ids is not { Count: not 0 })
            return false;

        var buffIdsSet = new HashSet<uint>(ids);

        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var effect in effects)
        {
            if (effect?.Template?.BuffId is null or 0)
                continue;
            if (!buffIdsSet.Contains(effect.Template.BuffId))
                continue;

            var tags = SkillManager.Instance.GetBuffTags(effect.Template.BuffId);
            if (tags != null && excludedTagIds.Any(ex => tags.Contains(ex)))
                continue;

            return true;
        }

        return false;
    }
    public int GetBuffCountById(uint buffId)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        var count = 0;
        foreach (var effect in effects.ToList())
            if (effect.Template.BuffId == buffId)
                count++;
        return count;
    }

    public void GetAllBuffs(List<Buff> goodBuffs, List<Buff> badBuffs, List<Buff> hiddenBuffs, bool includeAllPassives)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var buff in effects.ToList())
        {
            switch (buff.Template.Kind)
            {
                case BuffKind.Good:
                    if (buff.Passive && !includeAllPassives)
                        continue;
                    goodBuffs.Add(buff);
                    break;
                case BuffKind.Bad:
                    if (buff.Passive && !includeAllPassives)
                        continue;
                    badBuffs.Add(buff);
                    break;
                case BuffKind.Hidden:
                    // Always include passives of Hidden Buffs, required by for example WorldBoss and Queen Bee captures
                    hiddenBuffs.Add(buff);
                    break;
                default:
                    throw new NotSupportedException(nameof(buff.Template.Kind));
            }
        }
    }

    public void AddBuff(uint buffId, BaseUnit caster)
    {
        var buff = SkillManager.Instance.GetBuffTemplate(buffId);
        if (buff == null)
            return;

        var casterObj = new SkillCasterUnit(caster.ObjId);
        AddBuff(new Buff(GetOwner(), caster, casterObj, buff, null, DateTime.UtcNow));
    }

    public void AddBuff(Buff buff, uint index = 0, int forcedDuration = 0)
    {
        AddBuff(buff, index, forcedDuration, null);
    }

    private void AddBuff(Buff buff, uint index, int forcedDuration, CrowdChange? crowdChange)
    {
        Buff started = null;
        List<Buff> replaced = null;
        List<BrokenBuff> broken = null;
        try
        {
            AddBuffCore(buff, index, forcedDuration, ref started, ref replaced, ref broken, crowdChange);
        }
        finally
        {
            if (broken != null)
                CompleteBrokenBuffs(broken);
        }

        try
        {
            CompleteBuffApplication(started, replaced);
        }
        finally
        {
            if (broken != null)
                RaiseBroken(broken);
        }
    }

    private void CompleteBuffApplication(Buff started, List<Buff> replaced)
    {

        if (replaced != null)
        {
            foreach (var oldBuff in replaced)
                oldBuff.CompleteDispel();
        }

        if (started == null || started.IsEnded())
            return;

        ApplyFormPassives(started);

        // Cancelling a channel can remove a buff from another unit, and start triggers
        // can apply effects there. Keep both outside the owner's collection lock.
        var template = started.Template;
        if (template.BuffId > 0)
        {
            if (started.Owner is Character { IsRiding: true } character && (template.Stun || template.Sleep || template.Root))
            {
                var mateList = character.ParentWorld.MateManager.GetActiveMates(character.Id);
                foreach (var mate in mateList)
                    character.ParentWorld.MateManager.UnMountMate(character, mate.TlId, AttachPointKind.Driver, AttachUnitReason.None);
            }

            if (template.Stun || template.Silence || template.Sleep)
                started.Owner.InterruptSkills();
        }

        if (!started.IsEnded() && !started.IsRestored)
        {
            if ((template.Taunt || template.TauntWithTopAggro) && started.Caster is { Hp: > 0 } taunter &&
                !ReferenceEquals(started.Owner, taunter))
            {
                if (started.Owner is Npc npc)
                    npc.BeginTaunt(taunter, template.TauntWithTopAggro);
                else if (template.ImpossibleChangeTargeting && started.Owner is Character player &&
                         ReferenceEquals(player.ParentWorld, taunter.ParentWorld))
                {
                    player.CurrentTarget = taunter;
                    player.BroadcastPacket(new Core.Packets.G2C.SCTargetChangedPacket(player.ObjId, taunter.ObjId), true);
                }
            }

            started.Events.OnBuffStarted(started, new OnBuffStartedArgs());
        }
    }

    private void AddBuffCore(Buff buff, uint index, int forcedDuration, ref Buff started, ref List<Buff> replaced,
        ref List<BrokenBuff> broken, CrowdChange? crowdChange = null)
    {
        if (buff?.Template == null)
            return;

        lock (_lock)
        {
            var owner = GetOwner();
            if (owner == null || _worldEffectsDetached)
                return;
            if (crowdChange is { } change && !CanApplyCrowdGrant(buff, change))
                return;
            if (buff.IsRestored && CheckBuff(buff.Template.Id))
                return;
            if (buff.AuraSource != null && CheckBuff(buff.Template.Id))
                return;
            if (buff.FormPassiveSource is { } form &&
                (form.IsEnded() || !_effects.Contains(form) || CheckBuff(buff.Template.Id)))
                return;

            buff.State = EffectState.Created;
            if (index == 0)
            {
                buff.Index = _nextIndex; // TODO need safe increment...

                if (++_nextIndex == uint.MaxValue)
                    _nextIndex = FirstBuffIndex; // [GEAR-SLOT-FIX] skip GearBonusesIndex on wrap
            }
            else
            {
                buff.Index = index;
            }

            var buffIds = SkillManager.Instance.GetBuffTags(buff.Template.Id);
            if (!buff.IsRestored)
            {
                if (!TryAdvanceTolerance(buff, buffIds, out var toleranceCounter, ref started, ref replaced, ref broken))
                    return;

                BreakBuffs(buff, buffIds, ref broken);

                buff.Duration = buff.Template.GetDuration(buff.AbLevel);
                if (buff.Caster != null)
                    buff.Duration = (int)buff.Caster.BuffModifiersCache.ApplyModifiers(buff.Template, BuffAttribute.Duration, buff.Duration);
                buff.Duration = (int)buff.Owner.BuffModifiersCache.ApplyModifiers(buff.Template, BuffAttribute.InDuration, buff.Duration);

                if (toleranceCounter != null)
                {
                    buff.Duration = (int)(buff.Duration * ((100 - toleranceCounter.CurrentStep.TimeReduction) / 100.0));

                    if (buff.Caster is Character && buff.Owner is Character)
                        buff.Duration = (int)(buff.Duration * ((100 - toleranceCounter.Tolerance.CharacterTimeReduction) / 100.0));
                }

                if (forcedDuration != 0)
                    buff.Duration = forcedDuration;

                buff.Duration = buff.Template.ClampDuration(buff.Duration);
                buff.BaseDuration = buff.Duration;
            }
            var maxStack = (int)Math.Clamp((long)buff.Template.MaxStack, 1L, int.MaxValue);
            if (buff.Template.StackRule is BuffStackRule.Multiple or BuffStackRule.Independent)
                buff.Stack = Math.Clamp(buff.Stack, 1, maxStack);
            if (buff.StartTime == DateTime.MinValue)
                buff.StartTime = DateTime.UtcNow;
            if (buff.Duration > 0)
                buff.EndTime = buff.StartTime.AddMilliseconds(buff.Duration);

            Buff last = null;
            switch (buff.Template.StackRule)
            {
                case BuffStackRule.Refresh:
                    last = FindLiveBuff(buff, casterScoped: false);
                    if (last != null && buff.GetTimeLeft() < last.GetTimeLeft())
                        return;
                    break;
                case BuffStackRule.ChargeRefresh:
                    last = FindLiveBuff(buff, casterScoped: false);
                    buff.InitializeCharge();
                    if (last != null && buff.Charge < last.Charge)
                        return;
                    break;
                case BuffStackRule.ChargeExtend:
                    last = FindLiveBuff(buff, casterScoped: false);
                    buff.InitializeCharge();
                    if (last != null)
                    {
                        last.ExtendCharge(buff.Charge);
                        return;
                    }
                    break;
                case BuffStackRule.Extend:
                    last = FindLiveBuff(buff, casterScoped: false);
                    break;
                case BuffStackRule.Multiple:
                case BuffStackRule.Independent:
                    last = FindLiveBuff(buff, casterScoped: true);
                    if (last != null && last.Stack < maxStack)
                    {
                        var stack = (int)Math.Min(maxStack, (long)last.Stack + buff.Stack);
                        if (stack == maxStack && buff.Template.TransformBuffId != 0 &&
                            ApplyReplacementBuff(last, buff, buff.Template.TransformBuffId, ref started, ref replaced, ref broken))
                            return;

                        last.IncreaseStack(stack);
                        return;
                    }
                    break;
            }
            if (last != null)
            {
                if (last.Duration == 0 && buff.Duration == 0)
                {
                    last.IsCrowdGrant = false;
                    return;
                }

                last.OverwriteWith(buff);
            }
            else
            {
                if (!buff.PrepareRestoredSource())
                    return;

                _effects.Add(buff);
                if (buff.FormPassiveSource is { } passiveSource)
                    (passiveSource.GrantedFormPassives ??= []).Add(buff);
                buff.Triggers.SubscribeEvents();

                if (buff.Template.BuffId > 0)
                {
                    owner.SkillModifiersCache.AddModifiers(buff.Template.BuffId);
                    owner.BuffModifiersCache.AddModifiers(buff.Template.BuffId);
                    owner.CombatBuffs.AddCombatBuffs(buff.Template.BuffId);
                }

                buff.Start();
                RegisterCrowdSource(buff);
                started = buff;

                // If Owner has buffs that prevent it from doing combat, then remove the aggro for it
                if (buffIds.Contains((uint)TagsEnum.NoFight) || buffIds.Contains((uint)TagsEnum.Returning))
                {
                    // Unit entered a "safe zone"
                    if (owner is Unit unit)
                    {
                        unit.ClearAllAggro();
                        unit.IsInBattle = false;
                    }
                }
            }
        }
    }

    private bool TryAdvanceTolerance(Buff buff, List<uint> buffTags, out BuffToleranceCounter counter,
        ref Buff started, ref List<Buff> replaced, ref List<BrokenBuff> broken)
    {
        counter = null;
        foreach (var tag in buffTags)
        {
            var tolerance = BuffGameData.Instance.GetBuffToleranceForBuffTag(tag);
            if (tolerance == null)
                continue;

            if (CheckBuff(tolerance.FinalStepBuffId))
                return false;
            if (tolerance.Steps.Count == 0)
                return true;

            var now = DateTime.UtcNow;
            var steps = tolerance.Steps;
            if (!_toleranceCounters.TryGetValue(tolerance.Id, out counter))
            {
                counter = new BuffToleranceCounter { Tolerance = tolerance };
                _toleranceCounters.Add(tolerance.Id, counter);
            }

            // Every 3.5 ladder with multiple rows ends with an immunity step, even if its
            // consecutive reductions are equal. A refused application leaves the ladder fresh.
            var position = (now - counter.LastStep).TotalSeconds > tolerance.StepDuration
                ? 0
                : steps.IndexOf(counter.CurrentStep) + 1;
            var immunityStep = steps.Count > 1 && position >= steps.Count - 1;
            counter.CurrentStep = steps[immunityStep ? 0 : Math.Min(position, steps.Count - 1)];
            counter.LastStep = immunityStep ? DateTime.MinValue : now;

            if (immunityStep && ApplyReplacementBuff(null, buff, tolerance.FinalStepBuffId, ref started, ref replaced, ref broken))
                return false;

            return true;
        }

        return true;
    }

    private Buff FindLiveBuff(Buff incoming, bool casterScoped)
    {
        foreach (var effect in _effects)
        {
            if (!effect.InUse || effect.IsEnded() || effect.Template.Id != incoming.Template.Id)
                continue;

            if (!casterScoped || HasSameCaster(effect, incoming))
                return effect;
        }

        return null;
    }

    private static bool HasSameCaster(Buff live, Buff incoming)
    {
        if (live.Caster != null && incoming.Caster != null)
            return ReferenceEquals(live.Caster, incoming.Caster) ||
                   (live.Caster.ObjId != 0 && live.Caster.ObjId == incoming.Caster.ObjId);

        return live.Caster == incoming.Caster && live.SkillCaster?.Type == incoming.SkillCaster?.Type &&
               live.SkillCaster?.ObjId == incoming.SkillCaster?.ObjId;
    }

    private bool ApplyReplacementBuff(Buff live, Buff incoming, uint replacementId, ref Buff started,
        ref List<Buff> replaced, ref List<BrokenBuff> broken)
    {
        if (replacementId == 0 || replacementId == incoming.Template.Id ||
            SkillManager.Instance.GetBuffTemplate(replacementId) is not { } template ||
            !template.CanApplyTo(incoming.Owner, incoming.Caster, incoming.Skill))
            return false;

        if (live != null && live.TryBeginDispel())
            (replaced ??= []).Add(live);

        var replacement = new Buff(incoming.Owner, incoming.Caster, incoming.SkillCaster, template, incoming.Skill,
            DateTime.UtcNow) { AbLevel = incoming.AbLevel, Passive = incoming.Passive };
        AddBuffCore(replacement, 0, 0, ref started, ref replaced, ref broken);
        return true;
    }

    private readonly record struct BrokenBuff(Buff Victim, EventHandler<OnBrokenArgs> Handler, BaseUnit Breaker);

    [ThreadStatic]
    private static HashSet<Buffs> s_dispatchingBreakers;

    private void BreakBuffs(Buff arriving, List<uint> tags, ref List<BrokenBuff> broken)
    {
        if (tags.Count == 0)
            return;

        HashSet<uint> victimIds = null;
        foreach (var tag in tags)
        {
            if (BuffGameData.Instance.GetBuffsBrokenByTag(tag) is { } ids)
                (victimIds ??= []).UnionWith(ids);
        }
        if (victimIds == null)
            return;

        // Mark victims under the collection lock so stacking and tag checks see the removal
        // immediately. Their effects and packets are dispatched after acquiring no unit lock.
        foreach (var victim in _effects)
        {
            if (victimIds.Contains(victim.Template.Id) && victim.TryBeginDispel())
                (broken ??= []).Add(new BrokenBuff(victim, victim.Events.OnBroken, arriving.Caster));
        }
    }

    private static void CompleteBrokenBuffs(List<BrokenBuff> broken)
    {
        foreach (var (victim, _, _) in broken)
        {
            try
            {
                victim.CompleteDispel();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to finish broken buff {0}", victim.Template.Id);
            }
        }
    }

    private void RaiseBroken(List<BrokenBuff> broken)
    {
        // One breaker effect can itself grant a buff which breaks another member of this collection.
        var dispatching = s_dispatchingBreakers ??= [];
        if (!dispatching.Add(this))
            return;

        try
        {
            foreach (var (victim, handler, breaker) in broken)
            {
                var args = new OnBrokenArgs { Breaker = breaker };
                foreach (EventHandler<OnBrokenArgs> row in handler.GetInvocationList())
                {
                    try
                    {
                        row(victim, args);
                    }
                    catch (Exception exception)
                    {
                        Logger.Error(exception, "Failed to run breaker trigger for buff {0}", victim.Template.Id);
                    }
                }
            }
        }
        finally
        {
            dispatching.Remove(this);
        }
    }

    public void RemoveEffect(Buff buff)
    {
        var own = GetOwner();
        if (own == null)
            return;

        if (buff == null)
            return;

        List<Buff> endedFormPassives = null;
        bool ended;
        lock (_lock)
        {
            ended = buff.IsEnded();
            if (ended)
            {
                if (!_effects.Remove(buff))
                    return;

                endedFormPassives = DetachFormPassives(buff);

                if (IsCrowdSource(buff))
                    _crowdRevision++;

                own.SkillModifiersCache.RemoveModifiers(buff.Template.BuffId);
                own.BuffModifiersCache.RemoveModifiers(buff.Template.BuffId);
                own.CombatBuffs.RemoveCombatBuff(buff.Template.BuffId);
            }
            else if (!_effects.Contains(buff))
                return;
        }

        if (endedFormPassives != null)
        {
            foreach (var passive in endedFormPassives)
                passive.CompleteDispel();
        }
        if (!ended)
            buff.Exit();
        else if (buff.Template.Gliding)
            TriggerRemoveOn(BuffRemoveOn.Land);
    }

    public void RemoveEffect(uint templateId, uint skillId)
    {
        Buff[] effects;
        lock (_lock)
            effects = _effects.ToArray();

        foreach (var buff in effects)
        {
            var skill = buff.Skill;
            if (buff.Template.Id == templateId && skill?.Template?.Id == skillId)
                buff.ExitSkillEffect(skill, honorTimeout: false);
        }
    }

    public void RemoveEffect(uint templateId, Skill skill, bool honorTimeout = false)
    {
        Buff[] effects;
        lock (_lock)
            effects = _effects.ToArray();

        foreach (var buff in effects)
        {
            if (buff.Template.Id == templateId)
                buff.ExitSkillEffect(skill, honorTimeout);
        }
    }

    public void RemoveEffect(uint index)
    {
        Buff toRemove = null;
        lock (_lock)
        {
            foreach (var buff in _effects)
            {
                if (buff.Index == index && !buff.IsEnded())
                {
                    toRemove = buff;
                    break;
                }
            }
        }

        toRemove?.Exit();
    }

    public void RemoveBuff(uint buffId)
    {
        Buff toRemove = null;
        lock (_lock)
        {
            foreach (var buff in _effects)
            {
                if (buff.Template.BuffId == buffId && !buff.IsEnded())
                {
                    toRemove = buff;
                    break;
                }
            }
        }

        toRemove?.Exit();
    }

    internal bool TryConsumeBeneficialBuff()
    {
        Buff consumed = null;
        lock (_lock)
        {
            foreach (var buff in _effects)
            {
                if (buff is not { Passive: false, AuraSource: null, IsCrowdGrant: false, WorldEffectsDetached: false,
                        Template: { Kind: BuffKind.Good, System: false, Exempt: false } template } ||
                    template is { AuraRadius: > 0, AuraSlaveBuffId: > 0 } || !buff.TryBeginDispel())
                    continue;

                consumed = buff;
                break;
            }
        }

        if (consumed == null)
            return false;

        consumed.CompleteDispel();
        return true;
    }

    public void RemoveBuffs(BuffKind kind, int count, uint buffTagId = 0)
    {
        var own = GetOwner();
        if (own == null)
            return;

        var taggedBuffs = SkillManager.Instance.GetBuffsByTagId(buffTagId);

        if (_effects == null)
            return;

        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var buff in effects.ToList())
            if (buff != null)
            {
                var buffTemplate = buff.Template;

                if (buffTemplate == null)
                    continue;

                if (buffTagId == 0 && buffTemplate.System)
                    continue;
                if (buffTagId == 0 && buffTemplate.Kind != kind)
                    continue;
                if (buffTagId > 0 && !taggedBuffs.Contains(buffTemplate.Id))
                    continue;

                buff.Exit();
                count--;
                if (count == 0)
                    return;
            }
    }

    public void RemoveTaggedApplications(uint buffTagId, int applications)
    {
        var templates = SkillManager.Instance.GetBuffsByTagId(buffTagId);
        if (templates.Count == 0 || applications <= 0)
            return;

        Buff[] effects;
        lock (_lock)
            effects = _effects.ToArray();

        foreach (var effect in effects)
        {
            if (!templates.Contains(effect.Template.Id))
                continue;

            applications -= effect.TakeStacks(applications);
            if (applications <= 0)
                return;
        }
    }

    public void RemoveBuffs(uint buffTagId, int count)
    {
        var own = GetOwner();
        if (own == null)
            return;

        if (_effects == null)
            return;

        // Create a copy of the list of effects to avoid changing the list while iterating
        Buff[] effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        var buffIds = SkillManager.Instance.GetBuffsByTagId(buffTagId);
        if (buffIds == null)
            return;

        foreach (var e in effects)
            if (e != null)
            {
                if (!buffIds.Contains(e.Template.BuffId))
                    continue;

                e.Exit();
                count--;
                if (count == 0)
                    return;
            }
    }

    public void RemoveAllEffects()
    {
        var own = GetOwner();
        if (own == null)
            return;

        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var e in effects.ToList())
            if (e != null /* && (e.Template.Skill == null || e.Template.Skill.Type != SkillTypes.Passive)*/)
                e.Exit();
    }

    public void TriggerRemoveOn(BuffRemoveOn on, uint value = 0)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var effect in effects.ToList())
        {
            if (effect != null)
            {
                var template = effect.Template;

                if (template.RemoveOnAttackBuffTrigger && on == BuffRemoveOn.AttackBuffTrigger)
                    effect.Exit();
                else if (template.RemoveOnAttackedBuffTrigger && on == BuffRemoveOn.AttackedBuffTrigger)
                    effect.Exit();
                else if (template.RemoveOnAttackedEtc && on == BuffRemoveOn.AttackedEtc)
                    effect.Exit();
                else if (template.RemoveOnAttackedEtcDot && on == BuffRemoveOn.AttackedEtcDot)
                    effect.Exit();
                else if (template.RemoveOnAttackedSpellDot && on == BuffRemoveOn.AttackedSpellDot)
                    effect.Exit();
                else if (template.RemoveOnAttackEtc && on == BuffRemoveOn.AttackEtc)
                    effect.Exit();
                else if (template.RemoveOnAttackEtcDot && on == BuffRemoveOn.AttackEtcDot)
                    effect.Exit();
                else if (template.RemoveOnAttackSpellDot && on == BuffRemoveOn.AttackSpellDot)
                    effect.Exit();
                else if (template.RemoveOnAutoAttack && on == BuffRemoveOn.AutoAttack)
                    effect.Exit();
                else if (template.RemoveOnDamageBuffTrigger && on == BuffRemoveOn.DamageBuffTrigger)
                    effect.Exit();
                else if (template.RemoveOnDamagedBuffTrigger && on == BuffRemoveOn.DamagedBuffTrigger)
                    effect.Exit();
                else if (template.RemoveOnDamagedEtc && on == BuffRemoveOn.DamagedEtc)
                    effect.Exit();
                else if (template.RemoveOnDamagedEtcDot && on == BuffRemoveOn.DamagedEtcDot)
                    effect.Exit();
                else if (template.RemoveOnDamagedSpellDot && on == BuffRemoveOn.DamagedSpellDot)
                    effect.Exit();
                else if (template.RemoveOnDamageEtc && on == BuffRemoveOn.DamageEtc)
                    effect.Exit();
                else if (template.RemoveOnDamageEtcDot && on == BuffRemoveOn.DamageEtcDot)
                    effect.Exit();
                else if (template.RemoveOnDamageSpellDot && on == BuffRemoveOn.DamageSpellDot)
                    effect.Exit();
                else if (template.RemoveOnDeath && on == BuffRemoveOn.Death)
                    effect.Exit();
                else if (template.RemoveOnExempt && on == BuffRemoveOn.Exempt)
                    effect.Exit();
                else if (template.RemoveOnInteraction && on == BuffRemoveOn.Interaction)
                    effect.Exit();
                else if (template.RemoveOnLand && on == BuffRemoveOn.Land)
                    effect.Exit();
                else if (template.RemoveOnMount && on == BuffRemoveOn.Mount)
                    effect.Exit();
                else if (template.RemoveOnMove && on == BuffRemoveOn.Move)
                    effect.Exit();
                else if (template.RemoveOnSourceDead && on == BuffRemoveOn.SourceDead && value == effect.Caster?.ObjId)
                    effect.Exit();
                else if (template.RemoveOnStartSkill && on == BuffRemoveOn.StartSkill)
                {
                    if (value == 0)
                        effect.Exit();
                    else
                    {
                        var tags = SkillManager.Instance.GetBuffTags(effect.Template.BuffId);
                        if (!tags.Contains(value))
                            effect.Exit();
                    }
                }
                else if (template.RemoveOnUnmount && on == BuffRemoveOn.Unmount)
                    effect.Exit();
                else if (template.RemoveOnUseSkill && on == BuffRemoveOn.UseSkill)
                    effect.Exit();
            }
        }
    }

    public void RemoveEffectsOnDeath()
    {
        var own = GetOwner();
        if (own == null)
            return;

        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var e in effects.ToList())
            if (e != null && e.Template.RemoveOnDeath)
                e.Exit();
    }

    public void SetOwner(BaseUnit owner)
    {
        _owner = owner == null ? null : new WeakReference(owner);
    }

    public void RemoveStealth()
    {
        var own = GetOwner();
        if (own == null)
            return;

        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var e in effects.ToList())
            if (e != null && e.Template.Stealth)
                e.Exit();
    }

    private BaseUnit GetOwner()
    {
        return _owner?.Target as BaseUnit;
    }

    // The caller holds SyncRoot while checking lethal damage and consuming one-time protection.
    internal Buff GetImmortalityForDamage(DamageType? damageType, KillReason killReason)
    {
        Buff protection = null;
        foreach (var buff in _effects)
        {
            if (!buff.InUse || buff.IsEnded() || buff.WorldEffectsDetached)
                continue;

            var protects = damageType switch
            {
                DamageType.Melee => buff.Template.MeleeImmortality,
                DamageType.Ranged => buff.Template.RangedImmortality,
                DamageType.Magic => buff.Template.SpellImmortality,
                DamageType.Siege => buff.Template.SiegeImmortality,
                _ => killReason == KillReason.Fall && buff.Template.FallDamageImmortality
            };
            if (!protects)
                continue;
            if (!buff.Template.OneTimeImmortality)
                return buff;

            protection ??= buff;
        }
        return protection;
    }

    // The caller holds SyncRoot until the resulting HP and MP are committed.
    internal (int Damage, int ManaCost, int Reflected) ResolveDamageProtection(int damage, int availableMana,
        DamageType? reflectableDamageType)
    {
        if (damage <= 0)
            return (damage, 0, 0);

        var reflectionMask = reflectableDamageType switch
        {
            DamageType.Melee => 1u,
            DamageType.Magic => 2u,
            DamageType.Ranged => 4u,
            DamageType.Siege => 8u,
            _ => 0u
        };
        var reflected = 0;
        uint shieldRatio = 0;
        foreach (var effect in _effects)
        {
            if (!effect.InUse || effect.IsEnded() || effect.WorldEffectsDetached)
                continue;

            var template = effect.Template;
            shieldRatio = Math.Max(shieldRatio, template.ManaShieldRatio);
            if ((template.ReflectionTypeId & reflectionMask) == 0 || template.ReflectionChance == 0 ||
                Random.Shared.Next(100) >= template.ReflectionChance)
                continue;

            reflected = (int)Math.Min((long)reflected + (long)damage * template.ReflectionTargetRatio / 100, int.MaxValue);
            damage = (int)Math.Min((long)damage * template.ReflectionRatio / 100, int.MaxValue);
        }

        if (availableMana <= 0 || shieldRatio == 0 || damage == 0)
            return (damage, 0, reflected);

        // 10000 absorbs one HP per MP. The 15000 shield spends mana more efficiently.
        var covered = (int)Math.Min((long)availableMana * shieldRatio / 10000, damage);
        var manaCost = (int)(((long)covered * 10000 + shieldRatio - 1) / shieldRatio);
        return (damage - covered, manaCost, reflected);
    }

    public IEnumerable<Buff> GetAbsorptionEffects()
    {
        List<Buff> effects = null;
        lock (_lock)
        {
            foreach (var effect in _effects)
            {
                if (effect.InUse && !effect.IsEnded() && effect.Template.DamageAbsorptionTypeId > 0)
                    (effects ??= []).Add(effect);
            }
        }

        return effects == null ? Array.Empty<Buff>() : effects;
    }

    internal bool RestrictsTargetSelection(BaseUnit targeter)
    {
        List<(SkillTargetRelation Relation, BaseUnit Subject)> rules = null;
        lock (_lock)
        {
            foreach (var effect in _effects)
            {
                if (effect is not { InUse: true, Template.ImpossibleTargeting: true } || effect.IsEnded())
                    continue;

                var template = effect.Template;
                var subject = template.TargetingUseOriginSource ? effect.Caster ?? effect.Owner : effect.Owner;
                if (template.TargetingRelationId <= byte.MaxValue)
                    (rules ??= []).Add(((SkillTargetRelation)template.TargetingRelationId, subject));
            }
        }

        if (rules == null)
            return false;

        foreach (var (relation, subject) in rules)
        {
            var blocked = relation switch
            {
                SkillTargetRelation.Any => true,
                SkillTargetRelation.Party => subject.ObjId == targeter.ObjId ||
                    TeamManager.Instance.GetTeamByObjId(subject.ObjId)?.IsObjMember(targeter.ObjId) == true,
                SkillTargetRelation.Friendly or SkillTargetRelation.Hostile or SkillTargetRelation.Raid or
                    SkillTargetRelation.Others or SkillTargetRelation.Family =>
                    SkillTargetingUtil.IsRelationValid(relation, subject, targeter),
                _ => false
            };
            if (blocked)
                return true;
        }
        return false;
    }

    internal Unit GetTauntSource()
    {
        lock (_lock)
        {
            for (var index = _effects.Count - 1; index >= 0; index--)
            {
                var effect = _effects[index];
                if (effect.InUse && !effect.IsEnded() && !effect.WorldEffectsDetached &&
                    (effect.Template.Taunt || effect.Template.TauntWithTopAggro) &&
                    effect.Caster is { Hp: > 0 } caster && !ReferenceEquals(caster, effect.Owner) &&
                    ReferenceEquals(caster.ParentWorld, effect.Owner.ParentWorld))
                    return caster;
            }
        }
        return null;
    }

    internal bool HoldsCurrentTarget()
    {
        lock (_lock)
        {
            foreach (var effect in _effects)
            {
                if (effect.InUse && !effect.IsEnded() && !effect.WorldEffectsDetached && effect.Template.ImpossibleChangeTargeting)
                    return true;
            }
        }
        return false;
    }

    public bool HasEffectsMatchingCondition(Func<Buff, bool> predicate)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var effect in effects)
        {
            if (predicate(effect))
                return true;
        }
        return false;
    }

    public (bool Frozen, bool DriveBlocked, bool TurnBlocked) GetShipControlRestrictions()
    {
        var driveBlocked = false;
        var turnBlocked = false;
        lock (_lock)
        {
            foreach (var effect in _effects)
            {
                if (effect?.Template is not { } template)
                    continue;
                if (template.FreezeShip)
                    return (true, true, true);
                var incapacitated = template.Stun || template.Sleep;
                driveBlocked |= incapacitated || template.Root;
                turnBlocked |= incapacitated || template.Psychokinesis;
            }
        }
        return (false, driveBlocked, turnBlocked);
    }

    #region Buff Persistence
    /// <summary>
    /// Saves the active buff snapshot in the caller's character transaction.
    /// </summary>
    public void SaveActiveBuffs(MySqlConnection connection, MySqlTransaction transaction, uint characterId)
    {
        var rows = new List<PersistedBuff>();
        lock (_lock)
        {
            // A selecting character is already in the world index. Don't overwrite
            // the saved snapshot before that character has read it.
            if (!_persistentBuffsLoaded)
                return;

            var now = DateTime.UtcNow;
            foreach (var buff in _effects)
            {
                if (PersistedBuff.Capture(buff, characterId, now) is { } row)
                    rows.Add(row);
            }
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        try
        {
            command.CommandText = "SAVEPOINT character_active_buffs_save";
            command.ExecuteNonQuery();
            try
            {
                command.CommandText = "DELETE FROM character_active_buffs WHERE character_id = @characterId";
                command.Parameters.AddWithValue("@characterId", characterId);
                command.ExecuteNonQuery();

                var supportsSourceIdentity = true;
                foreach (var row in rows)
                {
                    BindBuffRow(command, row, characterId);
                    command.CommandText = supportsSourceIdentity ? InsertBuffWithSourceSql : InsertLegacyBuffSql;

                    try
                    {
                        command.ExecuteNonQuery();
                    }
                    catch (MySqlException ex) when (supportsSourceIdentity && ex.Number == (int)MySqlErrorCode.BadFieldError)
                    {
                        // Installations with updates deferred still save and load ordinary buffs.
                        // The legacy schema cannot represent source identity.
                        supportsSourceIdentity = false;
                        command.CommandText = InsertLegacyBuffSql;
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch
            {
                command.CommandText = "ROLLBACK TO SAVEPOINT character_active_buffs_save";
                command.ExecuteNonQuery();
                throw;
            }
            finally
            {
                command.CommandText = "RELEASE SAVEPOINT character_active_buffs_save";
                command.ExecuteNonQuery();
            }

            if (rows.Count > 0)
                Logger.Info($"Saved {rows.Count} active buff(s) for character {characterId}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to save active buffs for character {characterId}");
        }
    }

    // REPLACE preserves the schema's single row per character and buff template.
    private const string InsertBuffWithSourceSql = """
        REPLACE INTO character_active_buffs
            (character_id, buff_id, caster_id, caster_kind, caster_life, skill_id, ab_level,
             duration, time_left, charge, stack_count, real_time, saved_at)
        VALUES (@characterId, @buffId, @casterId, @casterKind, @casterLife, @skillId, @abLevel,
                @duration, @timeLeft, @charge, @stackCount, @realTime, @savedAt)
        """;

    private const string InsertLegacyBuffSql = """
        REPLACE INTO character_active_buffs
            (character_id, buff_id, caster_id, skill_id, ab_level, duration, time_left, charge, stack_count, real_time, saved_at)
        VALUES (@characterId, @buffId, @casterId, @skillId, @abLevel, @duration, @timeLeft, @charge, @stackCount, @realTime, @savedAt)
        """;

    private static void BindBuffRow(MySqlCommand command, PersistedBuff row, uint characterId)
    {
        command.Parameters.Clear();
        command.Parameters.AddWithValue("@characterId", characterId);
        command.Parameters.AddWithValue("@buffId", row.BuffId);
        command.Parameters.AddWithValue("@casterId", row.CasterId);
        command.Parameters.AddWithValue("@casterKind", (byte)row.SourceKind);
        command.Parameters.AddWithValue("@casterLife", (object)row.CasterLife?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("@skillId", row.SkillId);
        command.Parameters.AddWithValue("@abLevel", row.AbilityLevel);
        command.Parameters.AddWithValue("@duration", row.Duration);
        command.Parameters.AddWithValue("@timeLeft", row.TimeLeft);
        command.Parameters.AddWithValue("@charge", row.Charge);
        command.Parameters.AddWithValue("@stackCount", row.Stack);
        command.Parameters.AddWithValue("@realTime", row.RealTime);
        command.Parameters.AddWithValue("@savedAt", row.SavedAt);
    }

    public void LoadActiveBuffs(Character character)
    {
        try
        {
            var rows = new List<PersistedBuff>();
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM character_active_buffs WHERE character_id = @characterId";
            command.Parameters.AddWithValue("@characterId", character.Id);

            using (var reader = command.ExecuteReader())
            {
                var casterKindColumn = -1;
                var casterLifeColumn = -1;
                for (var column = 0; column < reader.FieldCount; column++)
                {
                    if (reader.GetName(column) == "caster_kind")
                        casterKindColumn = column;
                    else if (reader.GetName(column) == "caster_life")
                        casterLifeColumn = column;
                }

                while (reader.Read())
                {
                    var casterKind = casterKindColumn >= 0
                        ? (PersistedBuffSourceKind)reader.GetByte(casterKindColumn)
                        : PersistedBuffSourceKind.Legacy;
                    Guid? casterLife = null;
                    if (casterLifeColumn >= 0)
                    {
                        casterLife = reader.GetValue(casterLifeColumn) switch
                        {
                            Guid life => life,
                            string value when Guid.TryParse(value, out var life) => life,
                            _ => null
                        };
                    }

                    rows.Add(new PersistedBuff(reader.GetUInt32("buff_id"), casterKind, reader.GetUInt32("caster_id"),
                        casterLife, reader.GetUInt32("skill_id"), reader.GetUInt16("ab_level"), reader.GetInt32("duration"),
                        reader.GetInt32("time_left"), reader.GetInt32("charge"), reader.GetInt32("stack_count"),
                        reader.GetBoolean("real_time"), reader.GetDateTime("saved_at")));
                }
            }

            var restoredCount = 0;
            var manager = SkillManager.Instance;
            var now = DateTime.UtcNow;
            foreach (var row in rows)
            {
                if (manager.GetBuffTemplate(row.BuffId) is not { } template)
                {
                    Logger.Warn($"LoadActiveBuffs: BuffTemplate {row.BuffId} not found, skipping");
                    continue;
                }

                var skill = row.SkillId != 0 ? manager.GetSkillTemplate(row.SkillId) : null;
                var buff = row.CreateBuff(character, template, skill, FindOnlineCaster, now);
                if (buff == null)
                    continue;

                AddBuff(buff);
                if (buff.InUse)
                    restoredCount++;
            }

            // Keep the prior snapshot until a successful save replaces it. A disconnect
            // during selection can stop restoration before all buffs are active.
            lock (_lock)
            {
                if (!_worldEffectsDetached)
                    _persistentBuffsLoaded = true;
            }

            if (restoredCount > 0)
                Logger.Info($"Restored {restoredCount} buff(s) for character {character.Id} ({character.Name})");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to load active buffs for character {character.Id}");
        }
    }

    private static Character FindOnlineCaster(uint characterId)
    {
        var character = WorldManager.Instance.GetCharacterById(characterId);
        return character is { IsOnline: true } ? character : null;
    }

    /// <summary>
    /// Detaches runtime buff effects before logout while keeping the buffs available for saving.
    /// </summary>
    public void CancelAllEffectTasks()
    {
        Buff[] effects;
        lock (_lock)
        {
            if (_worldEffectsDetached)
                return;

            _worldEffectsDetached = true;
            s_crowdHolders.Remove(this);
            effects = _effects.ToArray();
        }

        foreach (var buff in effects)
        {
            buff.StopWorldEffects();
            if (buff.IsCrowdGrant)
                RemovePermanentCrowdGrant(buff);
        }

        if (GetOwner() is Unit owner)
            owner.Events.DetachBuffSource();
    }

    internal void ResumeAfterLogout()
    {
        Buff[] effects;
        lock (_lock)
        {
            if (!_worldEffectsDetached)
                return;

            effects = _effects.ToArray();
        }

        // Returning to character selection can reuse the same Character object.
        // Unregister bonuses and events from the previous session before loading.
        foreach (var buff in effects)
            buff.Exit(replace: true);

        lock (_lock)
        {
            _toleranceCounters.Clear();
            _persistentBuffsLoaded = false;
            _worldEffectsDetached = false;
        }

        if (GetOwner() is Unit owner)
            owner.Events.ResumeBuffSource();
    }
    #endregion
}
