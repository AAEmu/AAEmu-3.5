namespace AAEmu.Game.Physics.Hulls;

/// <summary>Tracks capacity overload: it appears immediately and drains at the peak-dependent authored rate.</summary>
public sealed class HullMassState
{
    private int _overload;
    private int _peak;
    private long? _updatedAt;

    public float Update(long nowMs, float hullMass, int capacity, float massPerUnit, int units, float attributeMass, float reductionPerSecond)
    {
        var target = 0;
        if (capacity > 0 && units > capacity && float.IsFinite(hullMass) && hullMass > 0f && float.IsFinite(massPerUnit) && massPerUnit > 0f)
        {
            var requested = (double)(units - capacity) * hullMass * massPerUnit;
            target = requested >= int.MaxValue ? int.MaxValue : (int)requested;
        }
        var elapsed = _updatedAt is { } previous ? Math.Max(0d, (nowMs - previous) / 1000d) : 0d;
        _updatedAt = nowMs;
        if (_overload <= target)
            _peak = target;
        else
        {
            var rate = float.IsFinite(reductionPerSecond) ? Math.Max(0f, reductionPerSecond) : 0f;
            var drop = (double)_peak * rate * elapsed;
            if (drop < _overload - target)
                target = _overload - (int)drop;
        }
        _overload = target;
        return MathF.Max(0f, _overload + (float.IsFinite(attributeMass) ? attributeMass : 0f));
    }
}
