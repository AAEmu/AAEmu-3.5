using AAEmu.Game.Core.Managers.Bots.Navigation;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Core.Managers.Bots.Navigation;

internal sealed class OceanNavWater
{
    public static OceanNavWater Instance { get; } = new();

    public Func<float, float, float, float> For(WorldTemplate world) =>
        (_, _, ground) => NavRules.Depth(world.OceanLevel, ground);
}