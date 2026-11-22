namespace AAEmu.Game.Physics.Hulls;

/// <summary>
/// Tracks capacity overload: it appears immediately and drains at the peak-dependent authored rate.
/// </summary>
/// <remarks>
/// Port of the zone host's <c>UpdateMasses</c> extra-mass rule (<c>HullSystem.cs</c>): past its capacity each unit
/// aboard adds the ship's mass times <c>added_ship_mass_per_unit</c>; the extra mass comes at once and goes at its
/// peak times <c>ShipMassReductionPerSecond</c> per second. The target uses single-precision arithmetic and truncates
/// like the original. Decay carries its fractional part through short ticks.
/// </remarks>
public sealed class HullMassState
{
    private int _overload;
    private int _peak;
    private long? _updatedAtMs;
    private double _decayRemainder;

    public float Update(long nowMs, float hullMass, int capacity, float massPerUnit, int units, float attributeMass, float reductionPerSecond)
    {
        var target = 0;
        if (capacity > 0 && units - capacity is var over and > 0 &&
            float.IsFinite(hullMass) && hullMass > 0f && float.IsFinite(massPerUnit) && massPerUnit > 0f)
        {
            var requestedMass = over * hullMass * massPerUnit;
            target = requestedMass >= int.MaxValue ? int.MaxValue : (int)requestedMass;
        }
        var previous = _updatedAtMs ?? nowMs;
        var current = Math.Max(previous, nowMs);
        _updatedAtMs = current;
        if (_overload <= target)
        {
            _peak = target;
            _decayRemainder = 0d;
        }
        else
        {
            var rate = float.IsFinite(reductionPerSecond) && reductionPerSecond > 0f ? reductionPerSecond : 0f;
            const double millisecondsPerSecond = 1000d;
            var accumulated = _decayRemainder + (double)_peak * rate * (current - previous);
            var remaining = _overload - target;
            if (accumulated < remaining * millisecondsPerSecond)
            {
                var drop = (int)(accumulated / millisecondsPerSecond);
                _decayRemainder = accumulated - drop * millisecondsPerSecond;
                target = _overload - drop;
            }
            else
            {
                _decayRemainder = 0d;
            }
        }
        _overload = target;
        return MathF.Max(0f, _overload + (float.IsFinite(attributeMass) ? attributeMass : 0f));
    }
}
