namespace AAEmu.Game.Models.Game.Skills;

public partial class Buff
{
    internal Buff FormPassiveSource { get; set; }
    internal uint FormPassiveId { get; init; }
    internal List<Buff> GrantedFormPassives { get; set; }
}
