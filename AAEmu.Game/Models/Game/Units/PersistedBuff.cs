using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Units;

internal enum PersistedBuffSourceKind : byte
{
    Legacy,
    Self,
    Character,
    Unavailable
}

internal readonly record struct PersistedBuff(uint BuffId, PersistedBuffSourceKind SourceKind, uint CasterId, Guid? CasterLife,
    uint SkillId, ushort AbilityLevel, int Duration, int TimeLeft, int Charge, int Stack, bool RealTime, DateTime SavedAt)
{
    private const int MinimumNormalDuration = 60000;

    internal static PersistedBuff? Capture(Buff buff, uint characterId, DateTime now)
    {
        if (buff is not { State: EffectState.Acting, Passive: false, Duration: > 0, AuraSource: null, IsCrowdGrant: false } ||
            buff.Template.SaveRuleId == (uint)BuffSaveRuleType.DontSave)
            return null;

        var duration = Math.Max(buff.Duration, buff.BaseDuration);
        if (buff.Template.SaveRuleId == (uint)BuffSaveRuleType.Normal &&
            (buff.Template.Kind == BuffKind.Bad || duration < MinimumNormalDuration))
            return null;

        var remaining = (int)Math.Min(int.MaxValue, (buff.StartTime.AddMilliseconds(buff.Duration) - now).TotalMilliseconds);
        if (remaining <= 0)
            return null;

        var (kind, casterId) = buff switch
        {
            { IsRestored: true, Caster: null } => (buff.RestoredSourceKind, buff.RestoredCasterId),
            { Caster: Character character } when ReferenceEquals(character, buff.Owner) =>
                (PersistedBuffSourceKind.Self, characterId),
            { Caster: Character { Id: > 0 } character } => (PersistedBuffSourceKind.Character, character.Id),
            _ => (PersistedBuffSourceKind.Unavailable, 0u)
        };

        if (buff.Template.RemoveOnSourceDead && kind != PersistedBuffSourceKind.Self &&
            (kind != PersistedBuffSourceKind.Character || buff.SourceLife == null || buff.Caster is not { IsDead: false } source ||
             source.Events.BuffSourceLife != buff.SourceLife))
            return null;

        return new PersistedBuff(buff.Template.Id, kind, casterId, buff.SourceLife, buff.Skill?.Template?.Id ?? 0,
            buff.AbLevel, duration, remaining, buff.Charge, Math.Max(1, buff.Stack), buff.Template.RealTime, now);
    }

    internal Buff CreateBuff(Character owner, BuffTemplate template, SkillTemplate skill, Func<uint, Character> findCharacter,
        DateTime now)
    {
        var elapsed = RealTime ? Math.Max(0L, (long)(now - SavedAt).TotalMilliseconds) : 0L;
        var remaining = template.ClampDuration(TimeLeft - elapsed);
        if (remaining <= 0)
            return null;

        var caster = SourceKind switch
        {
            PersistedBuffSourceKind.Self when CasterId == owner.Id => owner,
            PersistedBuffSourceKind.Character when CasterId > 0 && CasterId != owner.Id => findCharacter(CasterId),
            _ => null
        };

        if (template.RemoveOnSourceDead &&
            (caster is not { IsDead: false } || (!ReferenceEquals(caster, owner) &&
             (CasterLife == null || caster.Events.BuffSourceLife != CasterLife))))
            return null;

        return new Buff(owner, caster, new SkillCasterUnit(caster?.ObjId ?? 0), template,
            skill == null ? null : new Skill(skill), now)
        {
            IsRestored = true,
            RestoredSourceKind = SourceKind,
            RestoredCasterId = CasterId,
            SourceLife = CasterLife,
            AbLevel = AbilityLevel,
            Duration = remaining,
            BaseDuration = Math.Max(Duration, remaining),
            Charge = Charge,
            Stack = Math.Max(1, Stack)
        };
    }
}
