namespace AAEmu.Game.Models.Game.Slaves;

/// <summary>AAC 3.5 collision gains from <c>slave_collision_damages</c>.</summary>
public sealed class SlaveCollisionDamageDesc
{
    public uint Id { get; init; }
    public float FrontGain { get; init; } = 1f;
    public float SideGain { get; init; } = 1f;
    public float RearGain { get; init; } = 1f;
    public float BottomGain { get; init; } = 1f;

    /// <summary>The in-process hull's struck face: front 0, side 1, rear 2, bottom 3.</summary>
    public float GainFor(byte part) => part switch
    {
        0 => FrontGain,
        2 => RearGain,
        3 => BottomGain,
        _ => SideGain
    };
}
