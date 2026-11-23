using System.Collections.Concurrent;

using AAEmu.Game.Models.Game.Skills.Templates;
using NLog;

namespace AAEmu.Game.Models.Game.Units;

public class UnitCooldowns
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private ConcurrentDictionary<int, DateTime> _tagCooldowns;

    public ConcurrentDictionary<uint, DateTime> Cooldowns { get; set; } = new();

    public void AddCooldown(uint skillId, uint duration)
    {
        var now = DateTime.UtcNow;
        SetCooldown(Cooldowns, skillId, now, now.AddMilliseconds(duration));
    }

    public bool CheckCooldown(uint skillId) => IsActive(Cooldowns, skillId, DateTime.UtcNow);

    internal void AddSkillCooldown(SkillTemplate template, uint duration)
    {
        if (duration == 0)
            return;

        var now = DateTime.UtcNow;
        var endTime = SetCooldown(Cooldowns, template.Id, now, now.AddMilliseconds(duration));
        if (template.CooldownTagId > 0 || template.SecondCooldownTagId > 0 || template.ThirdCooldownTagId > 0)
        {
            var tags = LazyInitializer.EnsureInitialized(ref _tagCooldowns);
            SetTagCooldown(tags, template.CooldownTagId, endTime);
            SetTagCooldown(tags, template.SecondCooldownTagId, endTime);
            SetTagCooldown(tags, template.ThirdCooldownTagId, endTime);
        }
    }

    internal bool CheckSkillCooldown(SkillTemplate template)
    {
        var now = DateTime.UtcNow;
        var tags = Volatile.Read(ref _tagCooldowns);
        return IsActive(Cooldowns, template.Id, now) ||
               tags != null && (IsActive(tags, template.CooldownTagId, now) ||
                                IsActive(tags, template.SecondCooldownTagId, now) ||
                                IsActive(tags, template.ThirdCooldownTagId, now));
    }

    internal void RemoveTagCooldown(int tagId)
    {
        Volatile.Read(ref _tagCooldowns)?.TryRemove(tagId, out _);
    }

    public void RemoveCooldown(uint skillId)
    {
        Cooldowns.TryRemove(skillId, out _);
    }

    private static DateTime SetCooldown(ConcurrentDictionary<uint, DateTime> cooldowns, uint id, DateTime now, DateTime endTime)
    {
        return cooldowns.AddOrUpdate(id, static (_, times) => times.End,
            static (_, existing, times) => existing <= times.Now ? times.End : existing,
            (Now: now, End: endTime));
    }

    private static void SetTagCooldown(ConcurrentDictionary<int, DateTime> tags, int id, DateTime endTime)
    {
        if (id > 0)
            tags.AddOrUpdate(id, static (_, expiration) => expiration,
                static (_, existing, expiration) => expiration > existing ? expiration : existing, endTime);
    }

    private static bool IsActive<TKey>(ConcurrentDictionary<TKey, DateTime> cooldowns, TKey id, DateTime now) where TKey : notnull
    {
        while (cooldowns.TryGetValue(id, out var endTime))
        {
            if (endTime - now > TimeSpan.FromMilliseconds(250))
                return true;
            if (cooldowns.TryRemove(new KeyValuePair<TKey, DateTime>(id, endTime)))
                return false;
        }

        return false;
    }
}
