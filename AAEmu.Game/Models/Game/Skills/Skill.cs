using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Procs;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Physics;
using AAEmu.Game.Models.Tasks.Skills;
using AAEmu.Game.Utils;

using NLog;

#pragma warning disable IDE0079 // Remove unnecessary suppression

namespace AAEmu.Game.Models.Game.Skills;

public class Skill
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public uint Id { get; set; }
    public SkillTemplate Template { get; set; }
    internal bool IsItemProc { get; private set; }
    public byte Level { get; set; }
    public ushort TlId { get; set; }
    public PlotState ActivePlotState { get; set; }
    public Dictionary<uint, SkillHitType> HitTypes { get; set; }
    public BaseUnit InitialTarget { get; set; }//Temp Hack Fix. Replace this with UnitsEffected
    private bool _bypassGcd;
    private byte? _castEffectLevel;
    public bool Cancelled { get; set; } = false;
    internal int? EarlyCastingProgress { get; set; }
    public Action Callback { get; set; }

    /// <summary>
    /// Multiplier that can be added as an additional modifier to casting times
    /// </summary>
    public float CastTimeMultiplier { get; set; } = 1f;

    /// <summary>Counter for auto-attack animation cycling (incremented each attack)</summary>
    public int AutoAttackIndex { get; set; }

    public Skill()
    {
        HitTypes = [];
    }

    public Skill(SkillTemplate template, Unit owner = null)
    {
        if (template == null)
            return;
        HitTypes = [];
        Id = template.Id;
        Template = template;
        Level = owner != null && template.LevelStep > 0
            ? (byte)Math.Clamp((owner.GetAbLevel(template.AbilityId) - template.AbilityLevel) / template.LevelStep + 1, 1, byte.MaxValue)
            : (byte)1;
    }

    internal int GetEffectLevel(BaseUnit caster) => _castEffectLevel ?? CalculateEffectLevel(caster);

    private byte CalculateEffectLevel(BaseUnit caster)
    {
        var level = Math.Max(1, (int)Level);
        return (byte)Math.Clamp(caster?.ApplySkillModifiers(this, SkillAttribute.SkillLevel, level) ?? level, 1d, byte.MaxValue);
    }

    /// <summary>
    /// Runs the skill and returns it's error code if any
    /// </summary>
    /// <param name="caster"></param>
    /// <param name="casterCaster"></param>
    /// <param name="targetCaster"></param>
    /// <param name="skillObject">null by default</param>
    /// <param name="bypassGcd">false by default</param>
    /// <param name="skillResultValueUInt">Additional skill error data</param>
    /// <returns></returns>
    public SkillResult Use(BaseUnit caster, SkillCaster casterCaster, SkillCastTarget targetCaster, SkillObject skillObject, bool bypassGcd, out uint skillResultValueUInt)
    {
        skillResultValueUInt = 0;
        // Check if the source is an actual Unit
        if (caster is not Unit unit)
        {
            return SkillResult.InvalidSource;
        }

        // Cast character for future reference
        var character = caster as Character;

        if (character != null && Template.HighAbilityId > 0 &&
            character.Abilities?.IsHighAbilityActive(Template.HighAbilityId, Template.AbilityId) != true)
            return SkillResult.InvalidSkill;

        _castEffectLevel = CalculateEffectLevel(unit);
        unit.ConditionChance = true;

        if (Id == CharacterMates.RecoveryPotionSkillId)
        {
            if (character == null || casterCaster is not SkillItem potion)
                return SkillResult.InvalidSource;
            if (character.Mates?.CanUseRecoveryPotion(potion.ItemId, targetCaster, DateTime.UtcNow) != true)
                return SkillResult.InvalidTarget;
        }

        var requirementResult = UnitRequirementsGameData.Instance.CanUseSkill(Template, caster, casterCaster, targetCaster);
        if (requirementResult.ResultKey != SkillResultKeys.ok)
        {
            if (character != null)
                Logger.Warn($"{character.Name} ({character.Id}) failed requirements to use skill {Template?.Id} - {requirementResult.ResultKey}");
            Cancelled = true;
            skillResultValueUInt = requirementResult.ResultUInt;
            return SkillResultHelper.SkillResultErrorKeyToId(requirementResult.ResultKey);
        }

        // The skill's minimum is a required cost, not an upper bound on accumulated resource.
        if (Math.Max(0, unit.HighAbilityRsc) / 100 < Template.MinHighAbilityResource)
            return SkillResult.LackHighAbilityResource;

        _bypassGcd = bypassGcd;
        if (!_bypassGcd)
        {
            lock (unit.GcdLock)
            {
                // Commented out the line to eliminate the hanging of the skill
                // TODO: added for quest Id = 886 - скилл срабатывает часто, что не дает работать квесту - крысы не появляются
                var delay = 150;
                if (Id == 2 || Id == 3 || Id == 4)
                {
                    delay = character != null ? 500 : 1500;
                }

                if (unit.SkillLastUsed.AddMilliseconds(delay) > DateTime.UtcNow)
                {
                    // Will delay for 150 Milliseconds to eliminate the hanging of the skill
                    if (!caster.CheckInterval(delay))
                    {
                        Logger.Trace($"Skill: CooldownTime [{delay}]!");
                        return SkillResult.CooldownTime;
                    }
                }

                // Commented out the line to eliminate the hanging of the skill
                if (unit.GlobalCooldown >= DateTime.UtcNow && !Template.IgnoreGlobalCooldown)
                {
                    // Will delay for 50 Milliseconds to eliminate the hanging of the skill
                    if (!caster.CheckInterval(delay))
                    {
                        Logger.Trace($"Skill: CooldownTime [{delay}]!");
                        return SkillResult.CooldownTime;
                    }
                }

                if ((UsesPlayerCooldown(unit, casterCaster) || Id == CharacterMates.RecoveryPotionSkillId) &&
                    unit.Cooldowns.CheckSkillCooldown(Template))
                    return SkillResult.CooldownTime;

                unit.SkillLastUsed = DateTime.UtcNow;
            }
        }

        // Cancel buffs if Template asks for it
        if (!bypassGcd && Template.CancelOngoingBuffs)
        {
            if (caster is Units.Mate)
                caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.UseSkill, Template.CancelOngoingBuffExceptionTagId);
            caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.StartSkill, Template.CancelOngoingBuffExceptionTagId);
        }

        // Create a new skillObject if needed
        skillObject ??= new SkillObject();
        IsItemProc = skillObject.IsItemProc;

        // Grab current target
        var target = GetInitialTarget(caster, casterCaster, targetCaster);
        if (SkillTargetingUtil.IsTargetRestricted(caster, target))
            return SkillResult.NoTarget;
        if (character != null && target is Unit && !ReferenceEquals(target, character) &&
            !ReferenceEquals(target, character.CurrentTarget) && character.Buffs is Units.Buffs effects && effects.HoldsCurrentTarget())
            return SkillResult.NoTarget;
        InitialTarget = target;
        if (target == null)
        {
            if (caster is Npc npc)
            {
                npc.Ai?.OnNoAggroTarget();
            }
            Logger.Trace($"Skill: SkillResult.NoTarget! - Skill {Template.Id}, Caster {caster.Name} ({caster.ObjId})");
            return SkillResult.NoTarget; // We should try to make sure this doesn't happen, but can happen with NPC skills
        }

        // Unmount character if skill asks for it
        if (character is { IsRiding: true } && Template.Unmount)
        {
            var mateList = character.ParentWorld.MateManager.GetActiveMates(character.Id);
            foreach (var mate in mateList)
            {
                // TODO: Handle this better so it works for passengers as well
                if (mate.Passengers.GetValueOrDefault(AttachPointKind.Driver)?._objId == character.ObjId)
                    character.ParentWorld.MateManager.UnMountMate(character, mate.TlId, AttachPointKind.Driver, AttachUnitReason.None);
            }
        }

        // Check initial mana cost
        if (ManaCost(unit) > unit.Mp)
            return SkillResult.LackMana;

        // Get a TlId for this skill
        TlId = SkillTlIdManager.GetNextId(caster);
        // if (caster is Character)
        Logger.Debug($"Created SkillTlId {TlId} for Skill {Template.Id}, Caster {caster.Name} ({caster.TemplateId}:{caster.ObjId}) with target {target.Name} ({target.TemplateId}:{target.ObjId})");

        // If skill uses Plots, then start the plot
        if (Template.Plot != null)
        {
            Task.Run(() => Template.Plot.RunAsync(caster, casterCaster, target, targetCaster, skillObject, this));
            if (Template.PlotOnly)
                return SkillResult.Success;
        }

        // Check if target is within range
        var skillRange = caster.ApplySkillModifiers(this, SkillAttribute.Range, Template.MaxRange);
        var targetDist = unit.GetDistanceTo(target, true);

        var minRangeCheck = Template.MinRange * 1.0;
        var maxRangeCheck = skillRange;

        // HackFix: for quest Unblock the Spring ( 3707 ), unable to use the boulder because of being "too close"
        // The range of skill Remove Stone ( 16462 ) is defined as 100~200 which can't possibly be correct 
        if (Template.TargetType == SkillTargetType.Doodad && Template.MinRange >= 100)
        {
            minRangeCheck = Template.MinRange / 100.0;
        }

        // HACKFIX : Used mostly for boats, since the actual position of the doodad is the boat's origin, and not where it is displayed
        // TODO: Do a check based on model size or bounding box instead

        // If weapon is used to calculate range, use that
        if (Template.WeaponSlotForRangeId > 0)
        {
            var minWeaponRange = 0.0f; // Fist default
            var maxWeaponRange = 3.0f; // Fist default
            if (unit.Equipment.GetItemBySlot(Template.WeaponSlotForRangeId)?.Template is WeaponTemplate weaponTemplate)
            {
                minWeaponRange = weaponTemplate.HoldableTemplate.MinRange;
                maxWeaponRange = weaponTemplate.HoldableTemplate.MaxRange;
            }

            minRangeCheck = minWeaponRange;
            maxRangeCheck = maxWeaponRange;
        }

        var rangeFailure = CheckTargetRange(caster, target, targetDist, minRangeCheck, maxRangeCheck);
        if (rangeFailure == SkillResult.TooCloseRange)
        {
            SkillTlIdManager.ReleaseId(TlId);
            TlId = 0;
            Logger.Info($"TooCloseRange targetDist={targetDist}, minRangeCheck={minRangeCheck}, SkillTlId {TlId} for Skill {Template.Id}, Caster {caster.Name} ({caster.TemplateId}:{caster.ObjId}) with target {target.Name} ({target.TemplateId}:{target.ObjId})");
            return SkillResult.TooCloseRange;
        }

        // TODO: Remove exception for doodads
        // TODO: Remove exceptions for slave initiated by Doodads (needed to fix repair points on ships)
        if (rangeFailure == SkillResult.TooFarRange)
        {
            SkillTlIdManager.ReleaseId(TlId);
            TlId = 0;
            Logger.Info($"TooFarRange targetDist={targetDist}, maxRangeCheck={maxRangeCheck}, SkillTlId {TlId} for Skill {Template.Id}, Caster {caster.Name} ({caster.TemplateId}:{caster.ObjId}) with target {target.Name} ({target.TemplateId}:{target.ObjId})");
            return SkillResult.TooFarRange;
        }

        if (character is { AccessLevel: < 100 })
        {
            Portal trp = null;
            // copy Return.cs
            if (Template.Effects.Count > 0 && Template.Effects.First()?.Template is SpecialEffect specialEffect)
            {
                if (specialEffect.SpecialEffectTypeId == SpecialType.Return)
                {
                    if (specialEffect.Value1 > 0)
                    {
                        // Worldgates
                        trp = PortalManager.Instance.GetWorldGatesById((uint)specialEffect.Value1);
                    }
                    else
                    {
                        var returnPointId =
                            PortalManager.Instance.GetDistrictReturnPoint(character.ReturnDistrictId,
                                character.Faction.Id);
                        trp = PortalManager.Instance.GetRecallById(returnPointId);
                    }
                }
            }

            if (Template.Effects.Count > 0 && Template.Effects.First()?.Template is OpenPortalEffect)
            {
                if (WorldManager.DefaultInstanceId != caster.Transform.InstanceId)
                {
                    return SkillResult.InvalidLocation;
                }

                // copy OpenPortalEffect.cs
                var portalInfo = (SkillObjectUnk1)skillObject;
                trp = character.Portals.GetPortalInfo((uint)portalInfo.Id);
            }

            if (trp != null)
            {
                var zone = ZoneManager.Instance.GetZoneByKey(trp.ZoneId);
                if (zone is null or { Closed: true })
                {
                    // No more appropriate error type has been found yet
                    return SkillResult.NoPerm;
                }
            }
        }

        // Calculate casting time if needed
        var castTime = 0;
        if (Template.CastingTime > 0)
            castTime = (int)(unit.CastTimeMul * unit.SkillModifiersCache.ApplyModifiers(this, SkillAttribute.CastTime, Template.CastingTime));
        castTime = (int)Math.Round(castTime * CastTimeMultiplier);

        /*
        // TODO: Replace Old code
        else if (character != null && (Id == 2 || Id == 3 || Id == 4) && !caster.IsAutoAttack)
        {
            character.IsAutoAttack = true; // enable auto attack
            character.SkillId = Id;
            character.TlId = TlId;
            character.BroadcastPacket(new SCSkillStartedPacket(Id, 0, casterCaster, targetCaster, this, skillObject)
            {
                CastTime = Template.CastingTime
            }, true);
            character.AutoAttackTask = new MeleeCastTask(this, character, casterCaster, target, targetCaster, skillObject);
            TaskManager.Instance.Schedule(character.AutoAttackTask, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(1300));
        }
        */

        if (castTime > 0)
        {
            var task = new CastTask(this, caster, casterCaster, target, targetCaster, skillObject, castTime, Environment.TickCount64);
            if (skillObject.IsItemProc && !unit.TrySetSkillTask(task))
            {
                SkillTlIdManager.ReleaseId(TlId);
                TlId = 0;
                return SkillResult.Failure;
            }

            // Has casting time, schedule a task for it
            caster.BroadcastPacket(new SCSkillStartedPacket(Id, TlId, casterCaster, targetCaster, this, skillObject)
            {
                BaseCastTimeDiv10 = (ushort)(castTime / 10),
                RealCastTimeDiv10 = (ushort)(castTime / 10), // calculate with adjustments
            }, true);

            if (!skillObject.IsItemProc)
                unit.SkillTask = task;
            TaskManager.Instance.Schedule(task, TimeSpan.FromMilliseconds(castTime));
        }
        else
        {
            // Immediate skill
            Cast(caster, casterCaster, target, targetCaster, skillObject);
        }

        return SkillResult.Success;
    }

    internal static SkillResult? CheckTargetRange(BaseUnit caster, BaseUnit target, double distance, double minRange, double maxRange)
    {
        // Self-targeted attacks can still have an authored range for the NPC's choice of cast.
        if (ReferenceEquals(caster, target))
            return null;

        if (distance < minRange)
            return SkillResult.TooCloseRange;

        return distance > maxRange && target is not Doodad && target is not Slave ? SkillResult.TooFarRange : null;
    }

    private BaseUnit GetInitialTarget(BaseUnit caster, SkillCaster skillCaster, SkillCastTarget targetCaster)
    {
        if (caster is not Unit)
            return null;

        var target = caster;
        if (targetCaster == null || skillCaster == null) // проверяем, так как иногда бывает null
            return null;

        // HACKFIX : Mounts and Turbulence
        if (skillCaster.Type == SkillCasterType.Mount || skillCaster.Type == SkillCasterType.Unit)
            target = caster.ParentWorld.GetUnit(skillCaster.ObjId);

        switch (Template.TargetType)
        {
            case SkillTargetType.Self:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    break;
                }
            case SkillTargetType.Friendly:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        target = targetCaster.ObjId > 0 ? caster.ParentWorld.GetBaseUnit(targetCaster.ObjId) : caster;
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    if (target != null)
                    {
                        var relation = caster.GetRelationStateTo(target);
                        if (relation != RelationState.Friendly && relation != RelationState.Neutral)
                            return null; // Target isn't friendly
                    }

                    break;
                }
            case SkillTargetType.Hostile:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        target = targetCaster.ObjId > 0 ? caster.ParentWorld.GetBaseUnit(targetCaster.ObjId) : caster;
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    if (target != null)
                    {
                        var relation = caster.GetRelationStateTo(target);
                        if (relation != RelationState.Hostile && relation != RelationState.Neutral)
                            if (!caster.CanAttack(target))
                            {
                                return null; // Target isn't hostile
                            }
                    }

                    break;
                }
            case SkillTargetType.AnyUnit:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        target = targetCaster.ObjId > 0 ? caster.ParentWorld.GetBaseUnit(targetCaster.ObjId) : caster;
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    break;
                }
            case SkillTargetType.Doodad:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        target = targetCaster.ObjId > 0 ? caster.ParentWorld.GetBaseUnit(targetCaster.ObjId) : caster;
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    break;
                }
            case SkillTargetType.Item:
                // TODO ...
                break;
            case SkillTargetType.Others:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        target = targetCaster.ObjId > 0 ? caster.ParentWorld.GetBaseUnit(targetCaster.ObjId) : caster;
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    if (target != null && caster.ObjId == target.ObjId)
                    {
                        return null; //TODO отправлять ошибку?
                    }

                    break;
                }
            case SkillTargetType.FriendlyOthers:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        target = targetCaster.ObjId > 0 ? caster.ParentWorld.GetBaseUnit(targetCaster.ObjId) : caster;
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    if (target != null && caster.ObjId == target.ObjId)
                    {
                        return null; // Not allowed on self
                    }

                    var relation2 = caster.GetRelationStateTo(target);
                    if (relation2 != RelationState.Friendly && relation2 != RelationState.Neutral)
                        return null; // Target isn't friendly

                    break;
                }
            case SkillTargetType.Building:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        target = targetCaster.ObjId > 0 ? caster.ParentWorld.GetBaseUnit(targetCaster.ObjId) : caster;
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    if (target != null && caster.ObjId == target.ObjId)
                    {
                        return null; //TODO отправлять ошибку?
                    }

                    break;
                }
            case SkillTargetType.Slave:
                return targetCaster is SkillCastUnitTarget ? caster.ParentWorld?.GetSlaveByObjId(targetCaster.ObjId) : null;
            case SkillTargetType.Pos:
                {
                    target = SetInitialTarget(caster, targetCaster);
                    if (caster.ObjId == target.ObjId)
                        return null; //TODO отправлять ошибку?
                    break;
                }
            case SkillTargetType.BallisticPos:
                {
                    target = SetInitialTarget(caster, targetCaster);
                    if (caster.ObjId == target.ObjId)
                        return null; //TODO отправлять ошибку?
                    break;
                }
            case SkillTargetType.Party:
                break;
            case SkillTargetType.Raid:
                break;
            case SkillTargetType.Line:
                break;
            case SkillTargetType.Pet:
                break;
            case SkillTargetType.SummonPos:
                break;
            // Ship harpoon Launch Harpoon (13749) uses target_type_id 13 = RelativePos with a world Position from the client.
            case SkillTargetType.RelativePos:
                {
                    if (targetCaster is SkillCastPositionTarget or SkillCastPosition2Target or SkillCastPosition3Target)
                    {
                        target = SetInitialTarget(caster, targetCaster);
                        if (caster.ObjId == target.ObjId)
                            return null;
                    }

                    break;
                }
            case SkillTargetType.SourcePos:
                break;
            case SkillTargetType.ArtilleryPos:
                {
                    target = SetInitialTarget(caster, targetCaster);
                    if (caster.ObjId == target.ObjId)
                        return null; //TODO отправлять ошибку?
                    break;
                }
            case SkillTargetType.CursorPos:
                break;
            default:
                throw new NotSupportedException($"SkillTargetType not supported {Template.TargetType}");
        }

        return target;
    }

    private static BaseUnit SetInitialTarget(BaseUnit caster, SkillCastTarget targetCaster)
    {
        var positionUnit = new BaseUnit { ObjId = uint.MaxValue };
        positionUnit.Transform = caster.Transform.CloneDetached(positionUnit);
        switch (targetCaster)
        {
            case SkillCastDoodadTarget doodadTarget:
                break;
            case SkillCastItemTarget itemTarget:
                break;
            case SkillCastUnitTarget unitTarget:
                break;
            case SkillCastPositionTarget positionTarget:
                {
                    if (caster is Npc { CurrentTarget: not null } npc)
                        positionUnit.Transform.Local.SetPosition(npc.CurrentTarget.Transform.Local.Position.X, npc.CurrentTarget.Transform.Local.Position.Y, npc.CurrentTarget.Transform.Local.Position.Z);
                    else if (positionTarget.ObjId1 != 0)
                    {
                        var worldInst = caster.ParentWorld ?? WorldManager.Instance.GetWorld(caster.Transform.InstanceId);
                        if (worldInst?.GetBaseUnit(positionTarget.ObjId1) is BaseUnit basisUnit)
                        {
                            // Hit in basis unit's local frame (e.g. harpoon on hull); Pos* are not world meters.
                            var basisRot = basisUnit.Transform.World.ToQuaternion();
                            var basisScale = basisUnit.Scale;
                            var localHit = new Vector3(positionTarget.PosX, positionTarget.PosY, positionTarget.PosZ);
                            var worldHit = Vector3.Transform(localHit * basisScale, basisRot) + basisUnit.Transform.World.Position;
                            positionUnit.Transform.Local.SetPosition(worldHit.X, worldHit.Y, worldHit.Z);
                        }
                        else
                            positionUnit.Transform.Local.SetPosition(caster.Transform.World.Position.X, caster.Transform.World.Position.Y, caster.Transform.World.Position.Z);
                    }
                    else
                        positionUnit.Transform.Local.SetPosition(positionTarget.PosX, positionTarget.PosY, positionTarget.PosZ);
                    break;
                }
            case SkillCastPosition2Target position2Target:
                {
                    positionUnit.Transform.Local.SetPosition(position2Target.PosX, position2Target.PosY, position2Target.PosZ);
                    break;
                }
            case SkillCastPosition3Target position3Target:
                {
                    positionUnit.Transform.Local.SetPosition(position3Target.PosX, position3Target.PosY, position3Target.PosZ);
                    break;
                }
        }

        positionUnit.Region = WorldManager.Instance.GetRegion(positionUnit);

        return positionUnit;
    }

    private bool UsesPlayerCooldown(Unit unit, SkillCaster caster) =>
        !_bypassGcd && unit is Character { IgnoreSkillCooldowns: false } && caster is SkillCasterUnit &&
        Template.AbilityId is not (AbilityType.General or AbilityType.None) && Template.Id is not (2 or 3 or 4);

    internal void RecordCooldown(Unit unit, SkillCaster caster)
    {
        // The Ember's acquisition and return timers start after their effects succeed.
        if (Template.Id is TerritoryMemoryEmber.AcquireSkillId or TerritoryMemoryEmber.ReturnSkillId)
            return;

        if (UsesPlayerCooldown(unit, caster))
        {
            var modified = unit.ApplySkillModifiers(this, SkillAttribute.Cooldown, Template.CooldownTime);
            lock (unit.GcdLock)
                unit.Cooldowns.AddSkillCooldown(Template, (uint)Math.Clamp(modified, 0, uint.MaxValue));
        }
        else
        {
            unit.Cooldowns.AddCooldown(Template.Id, (uint)Math.Max(Template.CooldownTime, 0));
        }
    }

    public void Cast(BaseUnit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject)
    {
        if (caster is not Unit unit) { return; }

        if (!_bypassGcd)
        {
            var gcd = Template.CustomGcd;
            if (Template.DefaultGcd)
                gcd = caster is Npc ? 1500 : 1000;

            unit.GlobalCooldown = DateTime.UtcNow.AddMilliseconds(gcd * (unit.GlobalCooldownMul / 100));
        }

        if (caster is Npc && Template.SkillControllerId != 0)
        {
            var scTemplate = SkillManager.Instance.GetEffectTemplate(Template.SkillControllerId, "SkillController") as SkillControllerTemplate;

            // Get a random number (from 0 to n)
            var value = Random.Shared.Next(0, 1);
            // для skillId = 2 - for skillId = 2
            // 87 (35) - удар наотмаш, chr - overhead swing, chr
            // 2 (00) - удар сбоку, NPC - side strike, NPC
            // 3 (46) - удар сбоку, chr - side strike, chr
            // 1 (00) - удар похож на 2 удар сбоку, NPC - strike similar to 2, side strike, NPC
            // 91 - удар сверху (немного справа) - strike from above (slightly from the right)
            // 92 - удар наотмашь слева вниз направо - swing from left to right downwards
            // 0 - удар не наносится (расстояние большое и надо подойти поближе), no strike is made (distance is too great and need to get closer) f=1, c=15 
            var effectDelay = new Dictionary<int, short> { { 0, 46 }, { 1, 35 } };
            var fireAnimId = new Dictionary<int, int> { { 0, 3 }, { 1, 87 } };
            var effectDelay2 = new Dictionary<int, short> { { 0, 0 }, { 1, 0 } };
            var fireAnimId2 = new Dictionary<int, int> { { 0, 1 }, { 1, 2 } };

            //var targetUnit = (Unit)target; // unnecessary type cast
            var dist = MathUtil.CalculateDistance(caster.Transform.World.Position, target.Transform.World.Position, true);
            if (dist >= SkillManager.Instance.GetSkillTemplate(Id).MinRange && dist <= SkillManager.Instance.GetSkillTemplate(Id).MaxRange)
            {
                var sc = SkillController.CreateSkillController(scTemplate, caster, target);
#pragma warning disable CA1508 // Avoid dead conditional code
                if (sc != null)
                {
                    if (unit.ActiveSkillController != null)
                        unit.ActiveSkillController.End(force: true);
                    unit.ActiveSkillController = sc;
                    sc.Execute();
                }
#pragma warning restore CA1508 // Avoid dead conditional code
            }
        }
        if (unit.SkillTask is CastTask currentCast && ReferenceEquals(currentCast.Skill, this))
            unit.TryClearSkillTask(currentCast);

        ConsumeMana(caster);
        RecordCooldown(unit, casterCaster);

        // if (Id == 2 || Id == 3 || Id == 4)
        // {
        //     if (caster is Character && caster.CurrentTarget == null)
        //     {
        //         StopSkill(caster);
        //         return;
        //     }
        //
        //     // Get a random number (from 0 to n)
        //     var value = Rand.Next(0, 1);
        //     // для skillId = 2
        //     // 87 (35) - удар наотмаш, chr
        //     //  2 (00) - удар сбоку, NPC
        //     //  3 (46) - удар сбоку, chr
        //     //  1 (00) - удар похож на 2 удар сбоку, NPC
        //     // 91 - удар сверху (немного справа)
        //     // 92 - удар наотмашь слева вниз направо
        //     //  0 - удар не наносится (расстояние большое и надо подойти поближе), f=1, c=15
        //     var effectDelay = new Dictionary<int, short> { { 0, 46 }, { 1, 35 } };
        //     var fireAnimId = new Dictionary<int, int> { { 0, 3 }, { 1, 87 } };
        //     var effectDelay2 = new Dictionary<int, short> { { 0, 0 }, { 1, 0 } };
        //     var fireAnimId2 = new Dictionary<int, int> { { 0, 1 }, { 1, 2 } };
        //
        //     var trg = (Unit)target;
        //     var dist = MathUtil.CalculateDistance(caster.Position, trg.Position, true);
        //     if (dist >= SkillManager.Instance.GetSkillTemplate(Id).MinRange && dist <= SkillManager.Instance.GetSkillTemplate(Id).MaxRange)
        //     {
        //         caster.BroadcastPacket(caster is Character
        //                 ? new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject, effectDelay[value], fireAnimId[value])
        //                 : new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject, effectDelay2[value], fireAnimId2[value]),
        //             true);
        //     }
        //     else
        //     {
        //         caster.BroadcastPacket(caster is Character
        //                 ? new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject, effectDelay[value], fireAnimId[value], false)
        //                 : new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject, effectDelay2[value], fireAnimId2[value], false),
        //             true);
        //
        //         if (caster is Character chr)
        //         {
        //             chr.SendMessage("Target is too far ...");
        //         }
        //         return;
        //     }
        // }

        // Validate cast Item
        if (caster is Character player && casterCaster is SkillItem castItem)
        {
            var castItemTemplate = ItemManager.Instance.GetTemplate(castItem.ItemTemplateId);
            if (castItemTemplate.UseSkillAsReagent)
            {
                var useItem = ItemManager.Instance.GetItemByItemId(castItem.ItemId);
                if (useItem == null)
                {
                    Logger.Warn("SkillItem does not exists {0} (templateId: {1})", castItem.ItemId, castItem.ItemTemplateId);
                    return; // Item does not exists
                }

                if (useItem.HoldingContainer.OwnerId != player.Id)
                {
                    Logger.Warn("SkillItem {0} (itemId:{1}) is not owned by player {2} ({3})", useItem.Template.Name, useItem.Id, player.Name, player.Id);
                    return; // Item is not in the player's possessions
                }

                var itemCount = player.Inventory.GetItemsCount(useItem.TemplateId);
                var itemsRequired = 1; // TODO: This probably needs a check if it doesn't require multiple of source item to use, instead of just 1
                if (itemCount < itemsRequired)
                {
                    Logger.Warn("SkillItem, player does not own enough of {0} (count: {1}/{2}, templateId: {3})", useItem.Id, itemCount, itemsRequired, castItem.ItemTemplateId);
                    return; // not enough of item
                }
            }
        }

        if (Template.ChannelingTime > 0)
        {
            StartChanneling(caster, casterCaster, target, targetCaster, skillObject);
        }
        else
        {
            ScheduleEffects(caster, casterCaster, target, targetCaster, skillObject);
        }

        if (skillObject?.IsItemProc != true)
            unit.Procs?.RollProcsForKind(ProcChanceKind.FireSkill, target as Unit, this);
    }

    /// <summary>
    /// Only used to stop/cancel base melee/ranged skills
    /// </summary>
    /// <param name="caster"></param>
    public void StopSkill(BaseUnit caster)
    {
        if (caster is not Unit unit) { return; }

        if (unit.AutoAttackTask != null)
            unit.AutoAttackTask.Cancelled = true;

        // await unit.AutoAttackTask.Cancel();
        caster.BroadcastPacket(new SCSkillEndedPacket(TlId), true);
        caster.BroadcastPacket(new SCSkillStoppedPacket(unit.ObjId, Id), true);
        //unit.AutoAttackTask = null;
        //unit.IsAutoAttack = false; // turned off auto attack
        SkillTlIdManager.ReleaseId(TlId);
        TlId = 0;
    }

    public void StartChanneling(BaseUnit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject)
    {
        if (caster is not Unit unit) { return; }
        if (Template.ChannelingBuffId != 0)
        {
            var buff = SkillManager.Instance.GetBuffTemplate(Template.ChannelingBuffId);
            buff.Apply(caster, casterCaster, caster, new SkillCastUnitTarget(caster.ObjId), new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.UtcNow);
        }

        if (Template.ChannelingTargetBuffId != 0)
        {
            var buff = SkillManager.Instance.GetBuffTemplate(Template.ChannelingTargetBuffId);
            buff.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.UtcNow);
        }

        Doodad doodad = null;
        if (Template.ChannelingDoodadId > 0)
        {
            doodad = DoodadManager.Instance.Create(unit.ParentWorld, 0, Template.ChannelingDoodadId, caster, true);
            doodad.Transform = caster.Transform.CloneDetached(doodad);
            doodad.InitDoodad();
            doodad.Spawn();
        }

        var duration = (int)Math.Clamp(unit.ApplySkillModifiers(this, SkillAttribute.ChannelingTime, Template.ChannelingTime), 0d, int.MaxValue);
        caster.BroadcastPacket(new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject)
            .SetChannelingTime(duration), true);
        var channel = new EndChannelingTask(this, unit, casterCaster, target, doodad, duration);
        channel.Start();
    }

    internal void EndChanneling(Unit caster, BaseUnit target, Doodad channelDoodad, SkillCaster casterCaster)
    {
        if (Template.ChannelingBuffId != 0)
        {
            caster.Buffs.RemoveEffect(Template.ChannelingBuffId, this, honorTimeout: !Cancelled);
        }
        if (Template.ChannelingTargetBuffId != 0)
        {
            target.Buffs.RemoveEffect(Template.ChannelingTargetBuffId, this, honorTimeout: !Cancelled);
        }
        if (Cancelled && Template.ToggleBuffId != 0)
        {
            caster.Buffs.RemoveEffect(Template.ToggleBuffId, this);
        }

        channelDoodad?.Delete();

        EndSkill(caster);

        // TODO: добавил, так как для квеста 3469 нет события OnItemUse
        // TODO: added since there is no OnItemUse event for quest 3469 and other quests that require the use on non-consuming items
        if (Cancelled == false && casterCaster is SkillItem { ItemTemplateId: > 0 } item && caster is Character player)
        {
            player.ItemUse(item.ItemId);
        }

        caster.Events.OnChannelingCancel(this, new OnChannelingCancelArgs());
    }

    public void ScheduleEffects(BaseUnit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject)
    {
        if (caster is not Unit unit) { return; }
        if (Template.ToggleBuffId != 0)
        {
            var buff = SkillManager.Instance.GetBuffTemplate(Template.ToggleBuffId);
            buff.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.UtcNow);
        }

        var totalDelay = 0;
        if (Template.EffectDelay > 0)
            totalDelay += Template.EffectDelay;
        // Only authored projectile speeds of at least 1 m/s contribute travel time.
        if (Template.EffectSpeed >= 1f)
            totalDelay += (int)(unit.GetDistanceTo(target) / Template.EffectSpeed * 1000.0f);
        if (Template.FireAnim != null && Template.UseAnimTime)
            totalDelay += (int)(Template.FireAnim.CombatSyncTime * (unit.GlobalCooldownMul / 100));

        // Determine weapon-based animation for auto-attacks (skill 2/3/4).
        // 0 means "no override" — packet keeps its default (skill template's FireAnim).
        var weaponAnimId = GetWeaponAttackAnimId(caster);
        var firedPacket = new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject)
            .SetEffectDelay(totalDelay);
        if (weaponAnimId > 0)
            firedPacket.SetFireAnimId(weaponAnimId);
        caster.BroadcastPacket(firedPacket, true);

        if (totalDelay > 0)
        {
            var thisSkillTask = new ApplySkillTask(this, caster, casterCaster, target, targetCaster, skillObject);
            TaskManager.Instance.Schedule(thisSkillTask, TimeSpan.FromMilliseconds(totalDelay));
        }
        else
        {
            ApplyEffects(caster, casterCaster, target, targetCaster, skillObject);
            EndSkill(caster);
        }
    }

    /// <summary>
    /// Get the weapon-based attack animation ID for auto-attack skills (2/3/4).
    /// Returns 0 for non-auto-attack skills (packet will use FireAnim from template).
    /// NPCs cycle between melee animation IDs 1 and 2 so AI mobs always have a visible swing.
    /// </summary>
    private uint GetWeaponAttackAnimId(BaseUnit caster)
    {
        if (Template.Id is not (2 or 3 or 4))
            return 0;

        if (caster is NPChar.Npc)
        {
            // NPCs cycle between two melee attack animations (side strikes).
            // Without this, NPC auto-attacks would inherit the skill template's
            // FireAnim — often null or wrong, causing the "AI feels broken" symptom.
            var npcAnim = (uint)((AutoAttackIndex % 2) + 1); // animation IDs 1 and 2
            AutoAttackIndex++;
            return npcAnim;
        }

        if (caster is not Character character)
            return 0;

        var slot = Template.Id switch
        {
            3 => EquipmentItemSlot.Offhand,
            4 => EquipmentItemSlot.Ranged,
            _ => EquipmentItemSlot.Mainhand
        };

        var weapon = character.Equipment?.GetItemBySlot((int)slot);
        if (weapon?.Template is WeaponTemplate wt && wt.HoldableTemplate != null)
        {
            var leftHand = Template.Id == 3; // Offhand = left hand
            var animId = wt.HoldableTemplate.GetAttackAnimId(AutoAttackIndex, leftHand);
            AutoAttackIndex++;
            return animId;
        }

        // No weapon equipped — fist animations (cycle between 1 and 2)
        var fistAnim = (AutoAttackIndex % 2 == 0) ? 1u : 2u;
        AutoAttackIndex++;
        return fistAnim;
    }

    internal float GetAreaRadius(BaseUnit caster) => Template.TargetAreaRadius > 0
        ? (float)Math.Max(0, caster.ApplySkillModifiers(this, SkillAttribute.AreaRadius, Template.TargetAreaRadius))
        : 0;

    internal List<BaseUnit> SelectAreaTargets(BaseUnit caster, BaseUnit center, List<BaseUnit> nearby)
    {
        // The world query excludes the center. A resolved unit can still receive the hit.
        if (Template.TargetSelection is SkillTargetSelection.Source or SkillTargetSelection.Target &&
            center.ObjId is > 0 and not uint.MaxValue)
            nearby.Add(center);

        var sweep = Template.TargetAreaAngle is > 0 and < 360 ? Template.TargetAreaAngle : Template.FrontAngle;
        var halfAngle = sweep is > 0 and < 360 ? sweep / 2d : 0d;
        var targets = new List<BaseUnit>(nearby.Count);
        foreach (var target in SkillTargetingUtil.FilterWithRelation(Template.TargetRelation, caster, nearby))
        {
            if (target.ObjId == uint.MaxValue)
                continue;
            if (halfAngle > 0 && target.ObjId != caster.ObjId &&
                Math.Abs(MathUtil.ClampDegAngle(MathUtil.CalculateAngleFrom(caster, target))) > halfAngle)
                continue;

            targets.Add(target);
        }

        return targets;
    }

    public void ApplyEffects(BaseUnit caster, SkillCaster casterCaster, BaseUnit targetSelf, SkillCastTarget targetCaster, SkillObject skillObject)
    {
        if (caster is not Unit unit)
            return;
        var player = caster as Character;
        var possibleTargets = new List<BaseUnit>(); // TODO crutches
        // Get a list of all possible targets
        if (Template.TargetSiege && Template.TargetSelection == SkillTargetSelection.Source && caster is Slave)
        {
            // Hack for ship skills, only TargetSiege or possibly CombatDice seem to be different from other AoE skills
            possibleTargets.Add(targetSelf);
        }
        else
        {
            var radius = GetAreaRadius(caster);
            if (radius > 0)
            {
                var units = WorldManager.GetAround<BaseUnit>(targetSelf, radius, true);
                possibleTargets = SelectAreaTargets(caster, targetSelf, units);
            }
            else
            {
                possibleTargets.Add(targetSelf);
            }
        }

        ShipSiegeAoEHit.AppendHostileShipsHitBySiegeHullAoE(caster, Template, targetSelf, targetCaster, possibleTargets);

        // Filter out duplicate entries and non-existing
        possibleTargets = possibleTargets.Distinct().ToList();
        // Add origin in case of no targets and using a target position cast
        if (possibleTargets.Count <= 0 && targetCaster is SkillCastPositionTarget)
        {
            possibleTargets.Add(caster);
        }

        if (Template.TargetAreaCount > 0 && possibleTargets.Count > Template.TargetAreaCount)
        {
            possibleTargets = possibleTargets
                .OrderBy(t => t.GetDistanceTo(targetSelf))
                .Take(Template.TargetAreaCount)
                .ToList();
        }

        HitTypes.Clear();
        foreach (var target in possibleTargets)
        {
            if (target is Doodad doodad)
                doodad.OnSkillHit(caster, Id);
        }

        var packets = new CompressedGamePackets();
        var consumedItems = new List<(Item, int)>();
        var consumedItemTemplates = new List<(uint, int)>(); // itemTemplateId, amount

        var effectsToApply = new List<(BaseUnit target, SkillEffect effect)>(possibleTargets.Count * Template.Effects.Count);
        SkillEffect lastAppliedEffect = null;

        // Loop Skill Effects
        foreach (var effect in Template.Effects)
        {
            if (!IsHighAbilityResourceInRange(unit.HighAbilityRsc, effect.StartHighAbilityResource, effect.EndHighAbilityResource))
                continue;

            if (EarlyCastingProgress is { } progress && !effect.MatchesCastingUseProgress(progress))
                continue;

            // Get targets for this effect
            var effectedTargets = new List<BaseUnit>();
            switch (effect.ApplicationMethod)
            {
                case SkillEffectApplicationMethod.Target:
                    effectedTargets = possibleTargets;//keep target
                    break;
                case SkillEffectApplicationMethod.Source:
                    effectedTargets.Add(caster);//Diff between Source and SourceOnce?
                    break;
                case SkillEffectApplicationMethod.SourceOnce:
                    // TODO: HACKFIX for owner's mark
                    if (casterCaster.Type == SkillCasterType.Mount && targetSelf is Units.Mate || targetSelf is Slave)
                        effectedTargets = possibleTargets;
                    else
                        effectedTargets.Add(caster);//idk
                    break;
                case SkillEffectApplicationMethod.SourceToPos:
                    effectedTargets = possibleTargets;
                    break;
            }

            // Loop targets for this effect
            foreach (var target in effectedTargets)
            {
                var targetNpc = target as Npc;
                var relationState = caster.GetRelationStateTo(target);
                // Level range check
                if (effect.StartLevel > unit.Level || effect.EndLevel < unit.Level)
                {
                    continue;
                }

                // Relations checks
                if (effect.Friendly && !effect.NonFriendly && relationState != RelationState.Friendly)
                {
                    continue;
                }

                if (!effect.Friendly && effect.NonFriendly && relationState != RelationState.Hostile)
                {
                    if (relationState == RelationState.Friendly && !unit.ForceAttack || caster.ObjId == target.ObjId)
                    {
                        continue;
                    }
                }

                // Position check
                if (effect.Front && !effect.Back && !MathUtil.IsFront(caster, target))
                {
                    continue;
                }

                if (!effect.Front && effect.Back && MathUtil.IsFront(caster, target))
                {
                    continue;
                }

                // Blocking buffs and tags checks 
                if (effect.SourceBuffTagId > 0 && !SkillTargetingUtil.HasBuffTag(caster, effect.SourceBuffTagId, caster, effect.CheckSourceTagSrc))
                {
                    // TODO Commented out the code for the Id=2255 quest to work. Restore after finding a solution to the lack of a debuff.
                    continue;
                }

                if (effect.SourceNoBuffTagId > 0 && SkillTargetingUtil.HasBuffTag(caster, effect.SourceNoBuffTagId, caster, effect.CheckNoSourceTagSrc))
                {
                    continue;
                }

                if (effect.TargetBuffTagId > 0 && !SkillTargetingUtil.HasBuffTag(target, effect.TargetBuffTagId, caster, effect.CheckTargetTagSrc))
                {
                    continue;
                }

                if (effect.TargetNoBuffTagId > 0 && SkillTargetingUtil.HasBuffTag(target, effect.TargetNoBuffTagId, caster, effect.CheckNoTargetTagSrc))
                {
                    continue;
                }

                if (effect.TargetNpcTagId > 0)
                {
                    if (targetNpc == null || !TagsGameData.Instance
                            .GetIdsByTagId(TagsGameData.TagType.Npcs, effect.TargetNpcTagId)
                            .Contains(targetNpc.TemplateId))
                        continue;
                }

                // Dice
                if (effect.Chance < 100 && Random.Shared.Next(100) > effect.Chance)
                {
                    continue;
                }

                // prevents an NPC Spawn Skill to be duplicated 
                if (lastAppliedEffect != null &&
                    effect.Template is NpcSpawnerSpawnEffect &&
                    effect.EffectId == lastAppliedEffect.EffectId &&
                    (effect.Template as NpcSpawnerSpawnEffect).SpawnerId == (lastAppliedEffect.Template as NpcSpawnerSpawnEffect).SpawnerId)
                {
                    continue;
                }

                // Apply the effect
                effectsToApply.Add((target, effect));
                lastAppliedEffect = effect;
                //effect.Template?.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.UtcNow, packets);
            }
        }

        var damageType = (DamageType)Template.DamageTypeId;
        var diceKind = Template.LevelRuleNoConsideration
            ? CombatDiceKind.AlwaysHit : CombatDiceResolver.GetKind(Template.CombatDiceId, damageType);
        if (diceKind != CombatDiceKind.EachEffectRollDice)
        {
            // Position and area skills may damage units without declaring a hostile cast target.
            foreach (var (target, effect) in effectsToApply)
            {
                if (target is not Unit victim || ReferenceEquals(caster, victim) || effect.Template is not DamageEffect damage ||
                    HitTypes.ContainsKey(victim.ObjId))
                    continue;

                var attackType = damageType == 0 ? damage.DamageType : damageType;
                HitTypes.Add(victim.ObjId, CombatDiceResolver.Roll(caster, victim, diceKind, attackType));
            }

            if (Template.TargetType == SkillTargetType.Hostile)
            {
                foreach (var target in possibleTargets)
                {
                    if (target is Unit victim && !HitTypes.ContainsKey(victim.ObjId))
                        HitTypes.Add(victim.ObjId, CombatDiceResolver.Roll(caster, victim, diceKind, damageType));
                }
            }
        }

        SelectWeightedEffects(effectsToApply);
        var handlesSourceItemConsumption = false;
        if (player != null && !TryQueueEffectItemConsumption(player, casterCaster, effectsToApply,
                consumedItems, consumedItemTemplates, out handlesSourceItemConsumption))
        {
            Cancelled = true;
            return;
        }

        // This will handle all items with a reagent/product
        var reagents = SkillManager.Instance.GetSkillReagentsBySkillId(Template.Id);
        var skillProducts = SkillManager.Instance.GetSkillProductsBySkillId(Template.Id);
        if (reagents.Count > 0 || skillProducts.Count > 0)
        {
            if (player != null)
            {
                if (reagents.Count > 0)
                {
                    var foundValidReagents = false;
                    foreach (var reagent in reagents)
                    {
                        player.Inventory.Bag.GetAllItemsByTemplate(reagent.ItemId, -1, out _, out var totalCount);
                        if (totalCount >= reagent.Amount)
                        {
                            consumedItemTemplates.Add((reagent.ItemId, reagent.Amount));
                            foundValidReagents = true;
                            if (Template.FirstReagentOnly)
                                break;
                        }
                        else
                        if (!Template.FirstReagentOnly)
                        {
                            // Not enough reagent items
                            Cancelled = true;
                            return;
                        }
                    }

                    if (!foundValidReagents)
                    {
                        // Not enough reagent items
                        Cancelled = true;
                        return;
                    }
                }

                if (skillProducts.Count > 0)
                {
                    foreach (var product in skillProducts)
                    {
                        player.Inventory.Bag.AcquireDefaultItem(ItemTaskType.SkillEffectGainItem, product.ItemId, product.Amount);
                    }
                }
            }
        }

        // Apply the effects that need to happen
        foreach (var (target, effect) in effectsToApply)
        {
            // Template can be null for some reason.
            if (effect.Template != null)
            {
                var thisTargetCaster = target.ObjId == targetCaster.ObjId ||
                                       (ReferenceEquals(target, caster) && Template.TargetType == SkillTargetType.Item &&
                                        targetCaster is SkillCastItemTarget)
                    ? targetCaster
                    : new SkillCastUnitTarget(target.ObjId);

                var effectSource = new EffectSource(this)
                {
                    CombatHit = ResolveEffectCombatHit(caster, target, effect, diceKind, damageType)
                };
                effect.Template.Apply(caster, casterCaster, target, thisTargetCaster, new CastSkill(Template.Id, TlId),
                    effectSource, skillObject, DateTime.UtcNow, packets);
                if (player is { SkillCancelled: true })
                    Cancelled = true;

                // Implement consumption of item sets
                if (effect.ItemSetId > 0)
                {
                    // TODO: Check what KindId does (only 1 used in 1.2)
                    // TODO: Verify items before validating skill
                    var itemSet = ItemManager.Instance.GetItemSet(effect.ItemSetId);
                    if (itemSet != null)
                    {
                        foreach (var itemSetItem in itemSet.Items)
                        {
                            consumedItemTemplates.Add((itemSetItem.Value.ItemId, itemSetItem.Value.Count));
                            // player.Inventory.ConsumeItem(null, ItemTaskType.SkillEffectConsumption, itemSetItem.Value.ItemId, itemSetItem.Value.Count, null);
                        }
                    }
                }
            }
            else
                Logger.Error($"Template not found for Skill[{Template.Id}] Effect[{effect.EffectId}]");
        }

        // TODO Call OnItemUse() moved to the ApplyEffects() method from the effects and add trigger ConditionChance;
        // If the probability of passing the effect is greater than the chance, then run the check on the use of the item for the quest
        if (!handlesSourceItemConsumption && casterCaster is SkillItem skillItem && unit.ConditionChance)
        {
            if (player == null)
                return;
            player.ItemUse(skillItem.ItemId);

            // This fixes the issue where "dropping" a Portable Harpoon Cannon (item 23836) would not consume the cannon
            // Related skill Discard Portable Harpoon Cannon (skill 17735) has no reagents attached
            // The item however is marked with use_skill_as_reagent, so if it requires reagent according to the item
            // but has none attached, consume 1 of the source item instead
            // TODO: Check if this is intended behaviour, or if this is a bug in the compact.sqlite3 file
            if (Id != CharacterMates.RecoveryPotionSkillId && reagents.Count <= 0 &&
                skillProducts.Count <= 0 && consumedItems.Count <= 0 &&
                skillItem.ResolveSourceItem(player) && skillItem.SkillSourceItem is { Template.UseSkillAsReagent: true } item)
            {
                consumedItems.Add((item, 1));
                Logger.Debug($"Consumed item template 1 x {item.TemplateId} ({item.Id}) because of missing reagent information with skill {Template.Id}");
            }
        }

        // Quick Hack
        if (packets.Packets.Count > 0)
            caster.BroadcastPacket(packets, true);

        // Hack to consume TreasureMap items (don't know how else to add this)
        if (player != null && Template.Id == SkillsEnum.DigUpTreasureChestMarkedOnMap)
        {
            var treasureMapToUse = UnitRequirementsGameData.Instance.GetTreasureMapWithCoordinatesNearbyItem(player, 5.0);
            if (treasureMapToUse != null)
            {
                consumedItems.Add((treasureMapToUse, 1));
            }
            else
            {
                Logger.Error($"Unable to find a treasure map to take from user {player.Name} ({player.Id}) when digging up treasure");
            }
        }

        if (!Cancelled)
        {
            if (player != null)
            {
                // Actually consume the to be consumed items
                // Specific Items
                foreach (var (item, amount) in consumedItems)
                    if (item?.HoldingContainer != null && item.OwnerId == player.Id)
                    {
                        item.HoldingContainer.ConsumeItem(ItemTaskType.SkillReagents, item.TemplateId, amount, item);
                    }

                // Doesn't matter, but by Template
                foreach (var (templateId, amount) in consumedItemTemplates)
                    player.Inventory.ConsumeItem(null, ItemTaskType.SkillEffectConsumption, templateId,
                        amount, null);
            }
        }
    }


    private static void SelectWeightedEffects(List<(BaseUnit target, SkillEffect effect)> effects)
    {
        long weight = 0;
        foreach (var (_, effect) in effects)
            weight += Math.Max(0, effect.Weight);
        if (weight == 0)
            return;

        var roll = Random.Shared.NextInt64(weight);
        var kept = 0;
        for (var index = 0; index < effects.Count; index++)
        {
            var entry = effects[index];
            if (entry.effect.Weight > 0)
            {
                if (roll < 0)
                    continue;
                roll -= entry.effect.Weight;
                if (roll >= 0)
                    continue;
            }

            effects[kept++] = entry;
        }

        effects.RemoveRange(kept, effects.Count - kept);
    }

    private bool TryQueueEffectItemConsumption(Character player, SkillCaster caster, List<(BaseUnit target, SkillEffect effect)> effects,
        List<(Item, int)> items, List<(uint, int)> itemTemplates, out bool handlesSourceItemConsumption)
    {
        handlesSourceItemConsumption = false;
        var castItem = Id == CharacterMates.RecoveryPotionSkillId ? null : caster as SkillItem;
        var sourceCost = 0;
        var sourceReagentCount = 0;
        Dictionary<uint, int> templateCosts = null;
        HashSet<SkillEffect> counted = null;

        foreach (var (_, effect) in effects)
        {
            if (effect.Template == null)
                continue;

            handlesSourceItemConsumption |= effect.Template.HandlesSourceItemConsumption;
            sourceReagentCount = Math.Max(sourceReagentCount, effect.ConsumeItemCount);

            var consumesSource = castItem != null && effect.ConsumeSourceItem;
            if (consumesSource)
                sourceCost = Math.Max(sourceCost, effect.ConsumeItemCount);

            if (effect.ConsumeItemId == 0 || effect.ConsumeItemCount <= 0 || effect.Template.HandlesItemTemplateConsumption ||
                consumesSource && effect.ConsumeItemId == castItem.ItemTemplateId)
                continue;

            // A row can be selected for multiple targets. Its cost belongs to the cast.
            counted ??= [];
            if (!counted.Add(effect))
                continue;

            templateCosts ??= [];
            templateCosts.TryGetValue(effect.ConsumeItemId, out var count);
            templateCosts[effect.ConsumeItemId] = checked(count + effect.ConsumeItemCount);
        }

        Item sourceItem = null;
        var sourceAmount = 0;
        if (!handlesSourceItemConsumption && castItem != null && (sourceCost > 0 || sourceReagentCount > 0))
        {
            if (castItem.ResolveSourceItem(player))
                sourceItem = castItem.SkillSourceItem;

            if (sourceCost > 0)
            {
                if (sourceItem?.HoldingContainer == null ||
                    !sourceItem.HoldingContainer.GetAllItemsByTemplate(sourceItem.TemplateId, -1, out _, out var available) ||
                    available < sourceCost)
                    return false;

                sourceAmount = sourceCost;
            }
            else if (sourceItem?.Template.UseSkillAsReagent == true)
            {
                sourceAmount = sourceReagentCount;
            }

            if (sourceAmount > 0)
                items.Add((sourceItem, sourceAmount));
        }

        if (templateCosts != null)
        {
            foreach (var (templateId, amount) in templateCosts)
            {
                var available = (long)player.Inventory.GetItemsCount(SlotType.Inventory, templateId) +
                    player.Inventory.GetItemsCount(SlotType.Equipment, templateId);
                if (sourceItem?.TemplateId == templateId)
                    available -= sourceAmount;
                if (available < amount)
                    return false;

                itemTemplates.Add((templateId, amount));
            }
        }

        return true;
    }

    /// <summary>
    /// End skill in a normal way
    /// </summary>
    /// <param name="caster"></param>
    public void EndSkill(BaseUnit caster)
    {
        if (caster is not Unit unit)
            return;

        if (caster is Character character)
        {
            var laborCost = Template.ConsumeLaborPower;
            // Adjust labor cost if needed
            if (character.Actability.Actabilities.TryGetValue((byte)Template.ActabilityGroupId, out var actAbility))
            {
                laborCost = (int)Math.Round(laborCost * actAbility.GetLaborCostMultiplier());
            }

            // Lower cap at 1
            if (Template.ConsumeLaborPower > 0 && laborCost < 1)
                laborCost = 1;

            if (laborCost > 0 && !Cancelled && character.LaborPower >= laborCost)
            {
                // Consume labor only if there is enough of it
                character.ChangeLabor((short)-laborCost, Template.ActabilityGroupId);
            }

            // Add vocation where needed
            if (Template.GainLifePoint > 0 && !Cancelled)
            {
                // We multiply the BASE value for server settings, not the total (although I don't think this would affect anything since we don't really have a +1 badge/action buff)
                character.ChangeGamePoints(GamePointKind.Vocation, (int)Math.Ceiling(AppConfiguration.Instance.World.VocationRate * Template.GainLifePoint));
            }
        }

        Callback?.Invoke();
        unit.OnSkillEnd(this);
        caster.BroadcastPacket(new SCSkillEndedPacket(TlId), true);
        SkillTlIdManager.ReleaseId(TlId);
        TlId = 0;

        if (caster is Character character1 && character1.IgnoreSkillCooldowns)
            character1.ResetSkillCooldown(Template.Id, false);
    }

    /// <summary>
    /// Used for interrupting skills
    /// </summary>
    public void Stop(BaseUnit caster)
    {
        if (caster is Unit unit && unit.SkillTask is { } task && ReferenceEquals(task.Skill, this))
            Stop(unit, task);
    }

    internal void Stop(Unit unit, SkillTask task)
    {
        if (task is EndChannelingTask channel)
        {
            channel.Interrupt();
            return;
        }

        if (task is CastTask cast)
        {
            // The scheduled callback can already be queued. The cast task arbitrates which path wins.
            if (!cast.TryStop(unit))
                return;
            cast.Cancel();
        }
        else
        {
            if (!unit.TryClearSkillTask(task))
                return;
            Cancelled = true;
            task.Cancel();
        }
        FinishStoppedCast(unit);
    }

    internal void FinishStoppedCast(Unit unit)
    {
        Cancelled = true;
        if (Template.ToggleBuffId != 0)
        {
            unit.Buffs.RemoveEffect(Template.ToggleBuffId, Template.Id);
        }
        unit.BroadcastPacket(new SCCastingStoppedPacket(TlId, 0), true);
        unit.BroadcastPacket(new SCSkillEndedPacket(TlId), true);
        Callback?.Invoke();
        unit.OnSkillEnd(this);
        SkillTlIdManager.ReleaseId(TlId);
        TlId = 0;

        if (unit is Character character && character.IgnoreSkillCooldowns)
            character.ResetSkillCooldown(Template.Id, false);
    }

    private SkillHitType? ResolveEffectCombatHit(BaseUnit attacker, BaseUnit target, SkillEffect effect,
        CombatDiceKind kind, DamageType damageType)
    {
        if (target is not Unit victim || effect.Template is not (DamageEffect or BuffEffect))
            return null;

        var effectDamageType = effect.Template is DamageEffect damage ? damage.DamageType : damageType;
        if (kind == CombatDiceKind.EachEffectRollDice)
        {
            var contestsHit = effect.Template is DamageEffect ||
                              !ReferenceEquals(attacker, victim) && (effect.NonFriendly || Template.TargetType == SkillTargetType.Hostile);
            if (contestsHit)
                return CombatDiceResolver.Roll(attacker, victim, effect.AlwaysHit ? CombatDiceKind.AlwaysHit : kind, effectDamageType);
        }
        else if (effect.AlwaysHit && HitTypes.TryGetValue(victim.ObjId, out var hit) && CombatDiceResults.PreventsHit(hit))
        {
            return CombatDiceResolver.Roll(attacker, victim, CombatDiceKind.AlwaysHit, effectDamageType);
        }

        return null;
    }

    public SkillHitType RollCombatDice(BaseUnit attacker, BaseUnit target)
    {
        if (target is not Unit victim)
            return SkillHitType.Invalid;

        var damageType = (DamageType)Template.DamageTypeId;
        var kind = CombatDiceResolver.GetKind(Template.CombatDiceId, damageType);
        return CombatDiceResolver.Roll(attacker, victim, kind, damageType);
    }

    public bool SkillMissed(uint objId)
    {
        if (HitTypes.TryGetValue(objId, out var hitType))
        {
            return CombatDiceResults.PreventsHit(hitType);
        }
        Logger.Error($"Unit[{objId}] was not found in the CbtDiceRolls.");
        return true;
    }

    /// <summary>
    /// Gets the amount of a Mana a skill would use with the caster's modifiers applied
    /// </summary>
    /// <param name="caster"></param>
    /// <returns></returns>
    public int ManaCost(Unit caster)
    {
        var baseCost = ((caster.GetAbLevel(Template.AbilityId) - 1) * 1.6 + 8) * 3 / 3.65;
        var cost2 = baseCost * Template.ManaLevelMd + Template.ManaCost;
        var manaCost = (int)caster.SkillModifiersCache.ApplyModifiers(this, SkillAttribute.ManaCost, cost2);
        return manaCost;
    }

    internal static bool IsHighAbilityResourceInRange(int resource, int minimum, int maximum)
    {
        if (minimum == 0 && maximum == 0)
            return true;

        var level = Math.Max(0, resource) / 100;
        return level >= minimum && level <= maximum;
    }

    public void ConsumeMana(BaseUnit caster)
    {
        if (caster is not Unit unit)
            return;

        var manaCost = ManaCost(unit);
        unit.ReduceCurrentMp(null, manaCost);

        if (caster is not Character character)
            return;

        character.LastCast = DateTime.UtcNow;
        character.IsInPostCast = true;
    }
}
