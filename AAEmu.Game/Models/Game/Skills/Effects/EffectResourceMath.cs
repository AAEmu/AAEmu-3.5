namespace AAEmu.Game.Models.Game.Skills.Effects;

internal static class EffectResourceMath
{
    public static int RollPercentageOfPool(int pool, int first, int second)
    {
        var min = Math.Min(first, second);
        var max = Math.Max(first, second);
        var percentage = min == max ? min : (int)Random.Shared.NextInt64(min, (long)max + 1);
        return PercentageOfPool(pool, percentage);
    }

    public static int PercentageOfPool(int pool, int percentage) =>
        (int)Math.Clamp((long)Math.Max(0, pool) * percentage / 100, int.MinValue, int.MaxValue);

    public static int ApplyActability(int amount, int points, int step, float add, float multiplier)
    {
        if (step <= 0 || amount < 0)
            return amount;

        var steps = points / step;
        var adjusted = amount + Math.Truncate(steps * (double)add);
        return (int)Math.Clamp(adjusted + Math.Truncate(adjusted * steps * multiplier), int.MinValue, int.MaxValue);
    }
}
