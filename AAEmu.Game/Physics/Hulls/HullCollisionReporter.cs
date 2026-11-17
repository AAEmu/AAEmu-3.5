#nullable enable
namespace AAEmu.Game.Physics.Hulls;

/// <summary>Flushes the physics engine's strongest contact per hull on the ArcheageZones 500 ms grid.</summary>
public sealed class HullCollisionReporter
{
    public const int IntervalMs = 500;
    public const float RestingImpact = 0.09f;
    private readonly HashSet<uint> _wasInContact = [];
    private readonly List<HullCollisionWindow> _windows = [];
    private long _nextFlush = -1;

    public void Remove(uint id) => _wasInContact.Remove(id);

    public void Flush(long nowMs, IEnumerable<uint> ids, IHullPhysics physics, Action<uint, HullCollisionWindow, float> deliver)
    {
        if (_nextFlush < 0) _nextFlush = nowMs - nowMs % IntervalMs + IntervalMs;
        if (nowMs < _nextFlush) return;
        _nextFlush += IntervalMs;
        if (_nextFlush <= nowMs) _nextFlush = nowMs - nowMs % IntervalMs + IntervalMs;
        foreach (var id in ids)
        {
            _windows.Clear();
            physics.TakeCollisionWindows(id, _windows);
            if (_windows.Count == 0)
            {
                _wasInContact.Remove(id);
                continue;
            }
            var continued = _wasInContact.Contains(id);
            foreach (var window in _windows)
            {
                var impact = continued && physics.UsesRestingImpact ? MathF.Max(window.Impact, RestingImpact) : window.Impact;
                if (float.IsFinite(impact) && impact > 0f) deliver(id, window, impact);
            }
            _wasInContact.Add(id);
        }
    }
}
