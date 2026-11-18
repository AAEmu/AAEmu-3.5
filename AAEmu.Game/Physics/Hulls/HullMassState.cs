namespace AAEmu.Game.Physics.Hulls;

/// <summary>
/// Tracks capacity overload: it appears immediately and drains at the peak-dependent authored rate.
/// </summary>
/// <remarks>
/// Port of the zone host's <c>UpdateMasses</c> extra-mass rule (<c>HullSystem.cs</c>): past its capacity each unit
/// aboard adds the ship's mass times <c>added_ship_mass_per_unit</c>; the extra mass comes at once and goes at its
/// peak times <c>ShipMassReductionPerSecond</c> per second. The arithmetic stays in <see cref="float"/> and truncates
/// like the original: widening to double changes results by a whole unit (14 400 becomes 14 399 for a 0.48 factor).
/// </remarks>
public sealed class HullMassState
{
    private int _overload;
    private int _peak;
    private float? _updatedAt;

    public float Update(long nowMs, float hullMass, int capacity, float massPerUnit, int units, float attributeMass, float reductionPerSecond)
    {
        var now = nowMs / 1000f;
        var target = 0;
        if (capacity != 0 && units - capacity is var over and > 0 &&
            float.IsFinite(hullMass) && hullMass > 0f && float.IsFinite(massPerUnit) && massPerUnit > 0f)
            target = (int)(over * hullMass * massPerUnit);
        var elapsed = now - (_updatedAt ?? now);
        _updatedAt = now;
        if (_overload - target < 1)
            _peak = target;
        else if ((int)(_peak * reductionPerSecond * elapsed) is var drop && drop < _overload - target)
            target = _overload - drop;
        _overload = target;
        return MathF.Max(0f, _overload + (float.IsFinite(attributeMass) ? attributeMass : 0f));
    }
}
