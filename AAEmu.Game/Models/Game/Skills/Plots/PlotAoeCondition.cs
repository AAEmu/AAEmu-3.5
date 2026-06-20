namespace AAEmu.Game.Models.Game.Skills.Plots;

public class PlotAoeCondition
{
    public uint Id { get; set; }
    public PlotCondition Condition { get; set; }
    public uint ConditionId { get; set; }
    public uint EventId { get; set; }
    public int Position { get; set; }
}
