using System.Runtime.CompilerServices;

using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

internal static class AoeDiminishing
{
    private static readonly ConditionalWeakTable<Unit, HitCounts> s_counts = new();
    private static readonly TimeSpan s_window = TimeSpan.FromSeconds(10);

    internal static float Advance(Unit target, uint skillId, DateTime time, IReadOnlyList<float> rates)
    {
        if (rates.Count == 0)
            return 1;

        var counts = s_counts.GetValue(target, static _ => new HitCounts());
        lock (counts)
        {
            if (time >= counts.CleanupAt)
            {
                counts.Expired.Clear();
                foreach (var (id, previous) in counts.BySkill)
                {
                    if (time - previous.Time > s_window)
                        counts.Expired.Add(id);
                }
                foreach (var id in counts.Expired)
                    counts.BySkill.Remove(id);
                counts.CleanupAt = time.Add(s_window);
            }

            var position = 0;
            if (counts.BySkill.TryGetValue(skillId, out var hit) && time - hit.Time <= s_window)
                position = Math.Min(hit.Position + 1, rates.Count - 1);

            counts.BySkill[skillId] = (position, time);
            return rates[position] * 0.01f;
        }
    }

    internal static void Reset(Unit unit)
    {
        if (!s_counts.TryGetValue(unit, out var counts))
            return;
        lock (counts)
            counts.BySkill.Clear();
    }

    private sealed class HitCounts
    {
        public readonly Dictionary<uint, (int Position, DateTime Time)> BySkill = [];
        public readonly List<uint> Expired = [];
        public DateTime CleanupAt;
    }
}
