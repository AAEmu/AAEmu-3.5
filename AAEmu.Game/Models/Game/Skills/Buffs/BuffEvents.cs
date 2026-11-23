using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Buffs;

public class BuffEvents
{
    public EventHandler<OnBuffStartedArgs> OnBuffStarted = delegate { };
    public EventHandler<OnDispelledArgs> OnDispelled = delegate { };
    public EventHandler<OnTimeoutArgs> OnTimeout = delegate { };
    public EventHandler<OnBrokenArgs> OnBroken = delegate { };
    public EventHandler<OnImmortalityArgs> OnImmortality = delegate { };
}

public sealed class OnImmortalityArgs : EventArgs
{
    public BaseUnit Attacker { get; init; }
    public int Amount { get; init; }
}

public sealed class OnBrokenArgs : EventArgs
{
    public BaseUnit Breaker { get; init; }
}

public class OnBuffStartedArgs : EventArgs
{

}

public class OnDispelledArgs : EventArgs
{

}

public class OnTimeoutArgs : EventArgs
{

}
