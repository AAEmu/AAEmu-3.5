namespace AAEmu.Game.Models.Game;

public class ExperienceHeirLevelTemplate
{
    public int Id { get; set; }
    public byte Level { get; set; }
    public int ReqItemCount { get; set; }
    public int ReqItemId { get; set; }
    public uint ReqTotalExp { get; set; }
    public int Step { get; set; }
}
