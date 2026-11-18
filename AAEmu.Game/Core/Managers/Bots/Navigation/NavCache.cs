using System.Collections.Concurrent;
using System.Diagnostics;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Core.Managers.Bots.Navigation;

/// <summary>What the block cache holds and has done since it started.</summary>
public readonly record struct NavCacheStats(int Blocks, long Bytes, long BudgetBytes, long Hits, long Misses, long Evictions,
    long LoadFailures, TimeSpan LoadTime);

/// <summary>
/// The mesh blocks planners have loaded, least recently used first out once their estimated size passes the memory
/// budget (<c>Navigation.CacheMegabytes</c>). Loading reads files, so it happens only on planner threads; two planners
/// asking for the same block share one load. A block the world does not have is remembered as missing, and so is one
/// whose files cannot be read (warned once). An evicted block that a running search still holds stays valid: blocks
/// never change once built.
/// </summary>
public sealed class NavCache
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>What a missing block costs in the cache, so remembering empty sea is not free.</summary>
    private const long MissingBytes = 64;

    private readonly record struct Key(string World, int X, int Y);

    private sealed class Entry(Key key, NavBlock block, long bytes)
    {
        public Key Key { get; } = key;
        public NavBlock Block { get; } = block;
        public long Bytes { get; } = bytes;
    }

    private readonly INavBlockSource _source;
    private readonly INavWater _water;
    private readonly float _seamTolerance;
    private readonly Lock _lock = new();
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _recent = new();
    private readonly ConcurrentDictionary<Key, Lazy<NavBlock>> _loading = new();
    private long _bytes;
    private long _hits;
    private long _misses;
    private long _evictions;
    private long _failures;
    private long _loadTicks;

    /// <param name="source">Where blocks are read from.</param>
    /// <param name="budgetBytes">Memory budget for held blocks.</param>
    /// <param name="seamTolerance">Metres an edge may lie off a block side and still join the neighbour.</param>
    /// <param name="water">Where the water is; null is the sea only.</param>
    /// <param name="structureGrids">Blocks keep the waypoint grids of structures (<c>Navigation.UseStructureGrids</c>).</param>
    public NavCache(INavBlockSource source, long budgetBytes, float seamTolerance, INavWater water = null, bool structureGrids = false)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        BudgetBytes = Math.Max(1L, budgetBytes);
        _seamTolerance = seamTolerance;
        _water = water ?? OceanNavWater.Instance;
        StructureGrids = structureGrids;
    }

    private static readonly Lazy<NavCache> Live = new(() =>
    {
        var config = AAEmu.Game.Models.Game.BotNavigationConfig.Current;
        INavBlockSource source = string.IsNullOrWhiteSpace(config.WorldsFolder)
            ? new ClientNavBlockSource()
            : new FolderNavBlockSource(config.WorldsFolder);
        return new NavCache(source, config.CacheBytes, config.PlannerSettings().SeamTolerance, WorldNavWater.Instance,
            config.UseStructureGrids);
    });

    /// <summary>Blocks are built with the waypoint grids of structures.</summary>
    public bool StructureGrids { get; }

    /// <summary>The live server's cache, set up from <c>Bots.Navigation</c> on first use.</summary>
    public static NavCache Instance => Live.Value;

    public long BudgetBytes { get; }

    /// <summary>
    /// Block (<paramref name="blockX"/>, <paramref name="blockY"/>) of <paramref name="world"/>, loading it when it is
    /// not held; null when the world has no block there. Blocks on a planner thread while loading.
    /// </summary>
    public NavBlock Get(WorldTemplate world, int blockX, int blockY)
    {
        if (world == null || blockX < 0 || blockY < 0)
            return null;
        var key = new Key(world.Name ?? "", blockX, blockY);
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var held))
            {
                _recent.Remove(held);
                _recent.AddFirst(held);
                _hits++;
                return held.Value.Block;
            }

            _misses++;
        }

        var loading = _loading.GetOrAdd(key, k => new Lazy<NavBlock>(() => Load(world, k), LazyThreadSafetyMode.ExecutionAndPublication));
        var block = loading.Value;
        lock (_lock)
        {
            if (!_entries.ContainsKey(key))
            {
                var entry = new Entry(key, block, block?.EstimatedBytes ?? MissingBytes);
                _entries.Add(key, _recent.AddFirst(entry));
                _bytes += entry.Bytes;
                // The newest entry always stays, even when it alone is over the budget.
                while (_bytes > BudgetBytes && _recent.Count > 1)
                {
                    var oldest = _recent.Last;
                    _recent.RemoveLast();
                    _entries.Remove(oldest.Value.Key);
                    _bytes -= oldest.Value.Bytes;
                    _evictions++;
                }
            }
        }

        _loading.TryRemove(new KeyValuePair<Key, Lazy<NavBlock>>(key, loading));
        return block;
    }

    public NavCacheStats Stats()
    {
        lock (_lock)
            return new NavCacheStats(_entries.Count, _bytes, BudgetBytes, _hits, _misses, _evictions,
                Interlocked.Read(ref _failures), TimeSpan.FromTicks(Interlocked.Read(ref _loadTicks)));
    }

    /// <summary>Drops every block, for example after the client files changed.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _recent.Clear();
            _bytes = 0;
        }
    }

    private NavBlock Load(WorldTemplate world, Key key)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var data = _source.Load(key.World, key.X, key.Y);
            if (data == null)
                return null;
            // The water is asked for this block: the live world loads the terrain cells (and so the rivers and lakes)
            // around it first, since the block keeps these depths while it is held.
            var block = NavBlock.Build(key.X, key.Y, data, _seamTolerance, _water.ForBlock(world, key.X, key.Y), StructureGrids);
            Logger.Trace("Nav block {0} {1}_{2}: {3} triangles, {4} links, {5} seam edges, ~{6} KB",
                key.World, key.X, key.Y, block.TriangleCount, block.LinkCount, block.SeamEdgeCount, block.EstimatedBytes / 1024);
            return block;
        }
        catch (Exception e)
        {
            Interlocked.Increment(ref _failures);
            Logger.Warn("Nav block {0} {1}: cannot be read ({2}); treated as having no mesh", key.World,
                NavBlockFiles.FolderName(key.X, key.Y), e.Message);
            return null;
        }
        finally
        {
            Interlocked.Add(ref _loadTicks, Stopwatch.GetElapsedTime(started).Ticks);
        }
    }
}
