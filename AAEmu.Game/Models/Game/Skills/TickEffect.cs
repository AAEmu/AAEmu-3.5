namespace AAEmu.Game.Models.Game.Skills;

public class TickEffect
{
    public bool CheckNoTargetTagSrc { get; set; }
    public bool CheckTargetTagSrc { get; set; }
    public uint EffectId { get; set; }
    public uint TargetBuffTagId { get; set; }
    public uint TargetNoBuffTagId { get; set; }
}
