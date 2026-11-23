using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.NPChar;
public class Aggro(Unit owner)
{
    private readonly object _lock = new();

    public Unit Owner { get; } = owner;

    //Considering using interlocked methods instead of a lock, need to research how they work..
    private int _damageAggro;
    public int DamageAggro
    {
        get
        {
            lock (_lock)
            {
                return _damageAggro;
            }
        }
    }

    private int _healAggro;
    private int _otherAggro;
    public int HealAggro
    {
        get
        {
            lock (_lock)
            {
                return _healAggro;
            }
        }
    }

    public int TotalAggro
    {
        get
        {
            lock (_lock)
            {
                return (int)Math.Clamp((long)_damageAggro + _healAggro + _otherAggro, int.MinValue, int.MaxValue);
            }
        }
    }

    public void AddAggro(AggroKind kind, int amount)
    {
        lock (_lock)
        {
            if (kind == AggroKind.Damage)
                _damageAggro += amount;
            else if (kind == AggroKind.Heal)
                _healAggro += (int)(amount * 0.6f);
            else if (kind == AggroKind.Etc)
                _otherAggro += amount;
        }
    }

    internal void RaiseTotalAggroTo(int minimum)
    {
        lock (_lock)
        {
            var other = (long)minimum - _damageAggro - _healAggro;
            if (other > _otherAggro)
                _otherAggro = (int)Math.Min(other, int.MaxValue);
        }
    }

    public void RemoveAggro(AggroKind kind, int amount) => AddAggro(kind, -amount);

    internal int ResetComponents(bool damage, bool heal, bool other, int value)
    {
        lock (_lock)
        {
            if (damage)
                _damageAggro = value;
            if (heal)
                _healAggro = value;
            if (other)
                _otherAggro = value;
            return (int)Math.Clamp((long)_damageAggro + _healAggro + _otherAggro, int.MinValue, int.MaxValue);
        }
    }

    internal (int Damage, int Heal, int Other) GetComponents()
    {
        lock (_lock)
            return (_damageAggro, _healAggro, _otherAggro);
    }

    internal void SetComponents(int damage, int heal, int other)
    {
        lock (_lock)
        {
            _damageAggro = damage;
            _healAggro = heal;
            _otherAggro = other;
        }
    }
}
