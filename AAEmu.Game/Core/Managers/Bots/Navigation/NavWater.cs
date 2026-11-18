using System.Numerics;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Core.Managers.Bots.Navigation;

/// <summary>
/// Where the water is, for <see cref="NavOptions.MaxWaterDepth"/>. The client mesh files carry no usable water
/// depths (every link says 0), so the depth comes from the world: metres of water over the ground at a point.
/// </summary>
public interface INavWater
{
    /// <summary>The depth function for one world: (x, y, ground z) to metres of water over that ground, 0 when dry.</summary>
    Func<float, float, float, float> For(WorldTemplate world);

    /// <summary>
    /// The depth function a block is built with, which then keeps its depths for as long as it is held. A world whose
    /// rivers and lakes come with its terrain cells loads the cells around the block first; the default is
    /// <see cref="For(WorldTemplate)"/>. Called on planner threads only.
    /// </summary>
    Func<float, float, float, float> ForBlock(WorldTemplate world, int blockX, int blockY) => For(world);
}

/// <summary>The sea only: ground below the world's ocean level is under water (main_world: 100 m).</summary>
public sealed class OceanNavWater : INavWater
{
    public static OceanNavWater Instance { get; } = new();

    public Func<float, float, float, float> For(WorldTemplate world)
    {
        var ocean = world?.OceanLevel ?? float.NaN;
        return (_, _, ground) => NavRules.Depth(ocean, ground);
    }
}

/// <summary>
/// The sea plus the rivers and lakes the server loaded for the world (<see cref="WaterBodies"/>, read under its own
/// lock). Used by the live planner; blocks are built on planner threads, never on the tick thread. Rivers and lakes
/// reach <see cref="WaterBodies"/> only when the 1 km terrain cell that lists them is read, so before a block is built
/// the cells around it are loaded (<see cref="CellsAround"/>): a block built first would keep the dry depths of a lake
/// nobody had loaded yet, and every bot would walk into it. With heightmaps off no cell is loaded, as elsewhere.
/// </summary>
public sealed class WorldNavWater : INavWater
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Func<bool> _cellsEnabled;

    public WorldNavWater(Func<bool> cellsEnabled = null)
    {
        _cellsEnabled = cellsEnabled ?? (() => AppConfiguration.Instance.HeightMapsEnable);
    }

    public static WorldNavWater Instance { get; } = new();

    public Func<float, float, float, float> For(WorldTemplate world)
    {
        var ocean = OceanNavWater.Instance.For(world);
        var bodies = world == null ? null : WorldManager.Instance.GetWorldsByTemplate(world.Id).FirstOrDefault()?.Water;
        if (bodies == null)
            return ocean;
        return (x, y, ground) =>
        {
            var surface = bodies.GetWaterSurface(new Vector3(x, y, ground), out _);
            return MathF.Max(ocean(x, y, ground), NavRules.Depth(surface, ground));
        };
    }

    public Func<float, float, float, float> ForBlock(WorldTemplate world, int blockX, int blockY)
    {
        if (world != null && _cellsEnabled())
        {
            foreach (var (x, y) in CellsAround(blockX, blockY))
            {
                try
                {
                    world.GetCell(x, y)?.VerifyCellLoaded();
                }
                catch (Exception e)
                {
                    // The block is built with the water known so far.
                    Logger.Warn("Nav block {0}_{1}: terrain cell {2} {3} of {4} could not be loaded for its water ({5})",
                        blockX, blockY, x, y, world.Name, e.Message);
                }
            }
        }

        return For(world);
    }

    /// <summary>
    /// The 1 km terrain cells whose water may lie on block (<paramref name="blockX"/>, <paramref name="blockY"/>): the
    /// cell holding it, and the neighbouring cell on each side the block touches within <paramref name="margin"/>
    /// metres (a lake listed with the next cell can spill over the line). At most four.
    /// </summary>
    public static List<(int X, int Y)> CellsAround(int blockX, int blockY, float margin = NavBlock.Size,
        float cellSize = WorldManager.CELL_SIZE)
    {
        var minX = blockX * NavBlock.Size - margin;
        var maxX = (blockX + 1) * NavBlock.Size + margin;
        var minY = blockY * NavBlock.Size - margin;
        var maxY = (blockY + 1) * NavBlock.Size + margin;
        var cells = new List<(int X, int Y)>();
        for (var y = (int)MathF.Floor(minY / cellSize); y <= (int)MathF.Floor((maxY - 0.001f) / cellSize); y++)
        {
            for (var x = (int)MathF.Floor(minX / cellSize); x <= (int)MathF.Floor((maxX - 0.001f) / cellSize); x++)
            {
                if (x >= 0 && y >= 0)
                    cells.Add((x, y));
            }
        }

        return cells;
    }
}
