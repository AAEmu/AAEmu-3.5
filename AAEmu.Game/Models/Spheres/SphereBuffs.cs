namespace AAEmu.Game.Models.Spheres;

/// <summary>
/// Buff applied by a sphere.
/// </summary>
public class SphereBuffs
{
    public uint Id { get; set; }
    public uint BuffId { get; set; }
    public bool AndPet { get; set; }
    public uint RemoveOnLeaveBuffId { get; set; }
}
