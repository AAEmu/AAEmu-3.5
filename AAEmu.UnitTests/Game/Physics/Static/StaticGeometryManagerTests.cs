using System.Collections.Concurrent;
using System.Numerics;

using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Xml;
using AAEmu.Game.Physics.Static;
using AAEmu.Geometry;
using AAEmu.Geometry.Abstractions;

namespace AAEmu.UnitTests.Game.Physics.Static;

/// <summary>
/// Менеджер статической геометрии: зона читается из клиентских данных в фоне, поэтому первый запрос не должен
/// ни блокировать поток, ни врать. Настоящий клиент не нужен — сцена синтетическая.
/// </summary>
public class StaticGeometryManagerTests
{
    [Test]
    public async Task RetainedZones_SurviveCapacityPressureUntilAllOwnersReleaseThem()
    {
        var template = Template();
        template.XmlWorldZones[138] = template.XmlWorldZones[137];
        template.XmlWorldZones[139] = template.XmlWorldZones[137];
        var manager = new StaticGeometryManager(new MemoryFiles(Scene()), maxZones: 1);
        var first = new object();
        var second = new object();
        manager.SetRetainedZones(first, template, [137]);
        manager.SetRetainedZones(second, template, [137]);
        await manager.EnsureZoneAsync(template, 137);
        await manager.EnsureZoneAsync(template, 138);
        await Assert.That(manager.IsZoneReady(template, 137)).IsTrue();
        manager.SetRetainedZones(first, template, []);
        await manager.EnsureZoneAsync(template, 139);
        await Assert.That(manager.IsZoneReady(template, 137)).IsTrue();
        manager.SetRetainedZones(second, template, []);
        await manager.EnsureZoneAsync(template, 138);
        await Assert.That(manager.IsZoneReady(template, 137)).IsFalse();
    }

    [Test]
    public async Task RetainedPrefetch_ExceedsIdleCapacityWithoutDisplacingOrdinaryReaders()
    {
        var template = Template();
        template.XmlWorldZones[138] = template.XmlWorldZones[137];
        template.XmlWorldZones[139] = template.XmlWorldZones[137];
        var manager = new StaticGeometryManager(new MemoryFiles(Scene()), maxZones: 1);
        await manager.EnsureZoneAsync(template, 137);
        var owner = new object();
        manager.SetRetainedZones(owner, template, [138, 139]);
        await Assert.That(manager.TryPrefetchZone(template, 138)).IsTrue();
        await manager.EnsureZoneAsync(template, 138);
        await Assert.That(manager.TryPrefetchZone(template, 139)).IsTrue();
        await manager.EnsureZoneAsync(template, 139);
        await Assert.That(manager.IsZoneReady(template, 137)).IsTrue();
        await Assert.That(manager.IsZoneReady(template, 138)).IsTrue();
        await Assert.That(manager.IsZoneReady(template, 139)).IsTrue();
        manager.SetRetainedZones(owner, template, []);
        await Assert.That(manager.CachedZones).IsEqualTo(1);
    }

    [Test]
    public async Task Prefetch_ExistingZoneDoesNotRefreshItsLruPosition()
    {
        var template = Template();
        template.XmlWorldZones[138] = template.XmlWorldZones[137];
        template.XmlWorldZones[139] = template.XmlWorldZones[137];
        var manager = new StaticGeometryManager(new MemoryFiles(Scene()), maxZones: 2);
        await manager.EnsureZoneAsync(template, 137);
        await manager.EnsureZoneAsync(template, 138);
        await Assert.That(manager.TryPrefetchZone(template, 137)).IsTrue();
        await manager.EnsureZoneAsync(template, 139);
        await Assert.That(manager.IsZoneReady(template, 137)).IsFalse();
        await Assert.That(manager.IsZoneReady(template, 138)).IsTrue();
        await Assert.That(manager.IsZoneReady(template, 139)).IsTrue();
    }

    [Test]
    public async Task Prefetch_WhenCacheIsFull_DoesNotEvictExistingZone()
    {
        var template = Template();
        template.XmlWorldZones[138] = template.XmlWorldZones[137];
        var manager = new StaticGeometryManager(new MemoryFiles(Scene()), maxZones: 1);
        await manager.EnsureZoneAsync(template, 137);
        await Assert.That(manager.IsZoneReady(template, 137)).IsTrue();
        await Assert.That(manager.TryPrefetchZone(template, 138)).IsFalse();
        await Assert.That(manager.IsZoneReady(template, 137)).IsTrue();
        await Assert.That(manager.CachedZones).IsEqualTo(1);
    }

    private sealed class MemoryFiles(Dictionary<string, byte[]> files, ManualResetEventSlim? gate = null) : IGeometryFileSource
    {
        public byte[]? TryReadBytes(string path)
        {
            // Замок держит чтение, пока тест не отпустит сборку зоны.
            gate?.Wait(TimeSpan.FromSeconds(10));
            return files.GetValueOrDefault(path);
        }
    }

    private const string CellObjectDat = "game/worlds/main_world/cells/000_000/client/object.dat";
    private const string BoxAsset = "game/objects/test_box.cgf";

    private static WorldTemplate Template()
    {
        var template = new WorldTemplate { Id = 1, Name = "main_world", OceanLevel = 100f };
        template.XmlWorldZones = new ConcurrentDictionary<uint, XmlWorldZone>
        {
            [137] = new XmlWorldZone
            {
                Id = 137,
                Name = "test_zone",
                Cells = new ConcurrentDictionary<(int, int), XmlWorldCell>
                {
                    [(0, 0)] = new XmlWorldCell { X = 0, Y = 0 }
                }
            }
        };
        return template;
    }

    private static Dictionary<string, byte[]> Scene()
    {
        return new Dictionary<string, byte[]>
        {
            [CellObjectDat] = Geometry.ObjectDat.WithBoxBrushes(BoxAsset, [new Vector3(100f, 100f, 10f)]),
            [BoxAsset] = Geometry.CgfFixture.WithBoxProxy()
        };
    }

    [Test]
    public async Task GetZone_WhileBuilding_DoesNotBlockAndAnswersNothing()
    {
        using var gate = new ManualResetEventSlim(false);
        var manager = new StaticGeometryManager(new MemoryFiles(Scene(), gate));

        // The zone is being read: the caller must get an empty index right away (the height then comes from the
        // terrain), not a 700 ms stall.
        var collision = manager.GetZone(Template(), 137);
        await Assert.That(collision.Instances).IsEqualTo(0);

        gate.Set();
        var built = await manager.EnsureZoneAsync(Template(), 137);
        await Assert.That(built.Instances).IsEqualTo(1);
        await Assert.That(manager.IsZoneReady(Template(), 137)).IsTrue();
    }

    [Test]
    public async Task GetZone_AfterBuild_AnswersFloorQueries()
    {
        var manager = new StaticGeometryManager(new MemoryFiles(Scene()));
        await manager.EnsureZoneAsync(Template(), 137);

        // The fixture box is 2 m across and centred on the placement, so its top is at 11 m.
        var found = manager.TryFloor(Template(), 137, 100f, 100f, above: 50f, below: 0f, out var z);
        await Assert.That(found).IsTrue();
        await Assert.That(z).IsEqualTo(11f).Within(0.01f);
    }

    [Test]
    public async Task PhysicsShapes_TakesTheBrushesOfTheZoneCells()
    {
        var manager = new StaticGeometryManager(new MemoryFiles(Scene()));
        var collision = await manager.EnsureZoneAsync(Template(), 137);

        // Shapes retain game coordinates (Z up).
        var shapes = manager.PhysicsShapes(Template(), 137, collision).ToList();
        await Assert.That(shapes.Count).IsEqualTo(1);
        await Assert.That(shapes[0].Origin.X).IsEqualTo(100f).Within(0.01f);
        await Assert.That(shapes[0].Origin.Z).IsEqualTo(10f).Within(0.01f);
    }

    [Test]
    public async Task GetZone_WithoutClientData_StaysEmpty()
    {
        // No client data at all: the zone must not throw and must not pretend to have a floor.
        var manager = new StaticGeometryManager(new MemoryFiles(new Dictionary<string, byte[]>()));
        var collision = await manager.EnsureZoneAsync(Template(), 137);

        await Assert.That(collision.Instances).IsEqualTo(0);
        await Assert.That(manager.TryFloor(Template(), 137, 100f, 100f, above: 50f, below: 0f, out _)).IsFalse();
    }

    [Test]
    public async Task GetZone_RecentRepeatedReadsKeepTheZoneInTheBoundedCache()
    {
        var template = Template();
        template.XmlWorldZones[138] = template.XmlWorldZones[137];
        template.XmlWorldZones[139] = template.XmlWorldZones[137];
        var manager = new StaticGeometryManager(new MemoryFiles(Scene()), maxZones: 2);
        await manager.EnsureZoneAsync(template, 137);
        await manager.EnsureZoneAsync(template, 138);
        for (var i = 0; i < 10000; i++)
            manager.GetZone(template, 137);

        await manager.EnsureZoneAsync(template, 139);

        await Assert.That(manager.CachedZones).IsEqualTo(2);
        await Assert.That(manager.IsZoneReady(template, 137)).IsTrue();
        await Assert.That(manager.IsZoneReady(template, 138)).IsFalse();
        await Assert.That(manager.IsZoneReady(template, 139)).IsTrue();
    }

    [Test]
    public async Task Invalidate_CompletedZonePublishesDropAfterBuild()
    {
        var template = Template();
        var manager = new StaticGeometryManager(new MemoryFiles(Scene()));
        var events = new List<string>();
        manager.ZoneBuilt += (_, key, _) => events.Add($"built:{key}");
        manager.ZoneDropped += (_, key) => events.Add($"dropped:{key}");
        await manager.EnsureZoneAsync(template, 137);

        manager.Invalidate(template.Id);

        await Assert.That(string.Join(",", events)).IsEqualTo("built:137,dropped:137");
        await Assert.That(manager.CachedZones).IsEqualTo(0);
        await Assert.That(manager.IsZoneReady(template, 137)).IsFalse();
    }

    [Test]
    public async Task Evict_ZoneLoadedByEnsurePublishesDrop()
    {
        var template = Template();
        template.XmlWorldZones[138] = template.XmlWorldZones[137];
        var manager = new StaticGeometryManager(new MemoryFiles(Scene()), maxZones: 1);
        var drops = new List<uint>();
        manager.ZoneDropped += (_, key) => drops.Add(key);
        await manager.EnsureZoneAsync(template, 137);
        await manager.EnsureZoneAsync(template, 138);

        await Assert.That(drops.Count).IsEqualTo(1);
        await Assert.That(drops[0]).IsEqualTo(137u);
    }

    [Test]
    public async Task Invalidate_BuildInProgressCannotPublishStaleGeometry()
    {
        using var gate = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        var template = Template();
        var manager = new StaticGeometryManager(new BlockingFiles(gate, entered));
        var builds = 0;
        manager.ZoneBuilt += (_, _, _) => Interlocked.Increment(ref builds);
        var build = manager.EnsureZoneAsync(template, 137);
        var reading = entered.Wait(TimeSpan.FromSeconds(5));
        manager.Invalidate(template.Id);
        gate.Set();
        await build;

        await Assert.That(reading).IsTrue();
        await Assert.That(builds).IsEqualTo(0);
        await Assert.That(manager.CachedZones).IsEqualTo(0);
    }

    [Test]
    public async Task Invalidate_RequestWaitingForABuildSlotCannotRepopulateTheCache()
    {
        using var gate = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        var template = Template();
        var source = new BlockingFiles(gate, entered);
        var manager = new StaticGeometryManager(source);
        var builds = 0;
        manager.ZoneBuilt += (_, _, _) => Interlocked.Increment(ref builds);
        var tasks = new Task<StaticCollision>[5];
        for (var i = 0; i < tasks.Length; i++)
        {
            var key = (uint)(137 + i);
            template.XmlWorldZones[key] = template.XmlWorldZones[137];
            tasks[i] = manager.EnsureZoneAsync(template, key);
        }
        var fourReaders = SpinWait.SpinUntil(() => source.MaximumReaders == 4, TimeSpan.FromSeconds(5));

        manager.Invalidate(template.Id);
        gate.Set();
        await Task.WhenAll(tasks);

        await Assert.That(fourReaders).IsTrue();
        await Assert.That(builds).IsEqualTo(0);
        await Assert.That(manager.CachedZones).IsEqualTo(0);
        await Assert.That(manager.PendingZones).IsEqualTo(0);
    }

    [Test]
    public async Task Invalidate_NewGenerationOfTheSameZonePublishesOnlyOnce()
    {
        using var gate = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        var template = Template();
        var manager = new StaticGeometryManager(new BlockingFiles(gate, entered));
        var builds = 0;
        manager.ZoneBuilt += (_, _, _) => Interlocked.Increment(ref builds);
        var oldBuild = manager.EnsureZoneAsync(template, 137);
        var reading = entered.Wait(TimeSpan.FromSeconds(5));
        manager.Invalidate(template.Id);
        var replacement = manager.EnsureZoneAsync(template, 137);
        gate.Set();
        await Task.WhenAll(oldBuild, replacement);

        await Assert.That(reading).IsTrue();
        await Assert.That(builds).IsEqualTo(1);
        await Assert.That(manager.CachedZones).IsEqualTo(1);
        await Assert.That(manager.IsZoneReady(template, 137)).IsTrue();
    }

    [Test]
    public async Task Evict_BuildInProgressCannotPublishAfterItsReplacement()
    {
        using var gate = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        var template = Template();
        template.XmlWorldZones[138] = new XmlWorldZone
        {
            Id = 138,
            Name = "replacement",
            Cells = new ConcurrentDictionary<(int, int), XmlWorldCell>
            {
                [(1, 0)] = new XmlWorldCell { X = 1, Y = 0 }
            }
        };
        var manager = new StaticGeometryManager(new BlockingFiles(gate, entered, "cells/000_000/"), maxZones: 1);
        var builds = new ConcurrentQueue<uint>();
        manager.ZoneBuilt += (_, key, _) => builds.Enqueue(key);
        var oldBuild = manager.EnsureZoneAsync(template, 137);
        var reading = entered.Wait(TimeSpan.FromSeconds(5));
        try
        {
            await manager.EnsureZoneAsync(template, 138);
        }
        finally
        {
            gate.Set();
        }
        var stale = await oldBuild;

        await Assert.That(reading).IsTrue();
        await Assert.That(string.Join(",", builds)).IsEqualTo("138");
        await Assert.That(stale.Instances).IsEqualTo(0);
        await Assert.That(manager.CachedZones).IsEqualTo(1);
        await Assert.That(manager.IsZoneReady(template, 138)).IsTrue();
    }

    [Test]
    public async Task EnsureZoneAsync_SourceFailureLeavesAReadyEmptyFallbackWithoutRepeatedReads()
    {
        var template = Template();
        var source = new FailingFiles();
        var manager = new StaticGeometryManager(source);

        var collision = await manager.EnsureZoneAsync(template, 137);
        for (var i = 0; i < 100; i++)
            manager.GetZone(template, 137);

        await Assert.That(collision.Instances).IsEqualTo(0);
        await Assert.That(manager.IsZoneReady(template, 137)).IsTrue();
        await Assert.That(manager.PendingZones).IsEqualTo(0);
        await Assert.That(source.Reads).IsEqualTo(1);
    }

    [Test]
    public async Task EnsureZoneAsync_ConcurrentRequestsNeverReadMoreThanFourZones()
    {
        using var gate = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        var source = new BlockingFiles(gate, entered);
        var manager = new StaticGeometryManager(source, maxZones: 16);
        var template = Template();
        var tasks = new Task<StaticCollision>[12];
        for (var i = 0; i < tasks.Length; i++)
        {
            var key = (uint)(137 + i);
            template.XmlWorldZones[key] = template.XmlWorldZones[137];
            tasks[i] = manager.EnsureZoneAsync(template, key);
        }

        // A fifth source reader would prove that the cache's admission limit was bypassed.
        var exceededLimit = await Task.Run(() => SpinWait.SpinUntil(() => source.MaximumReaders > 4, TimeSpan.FromSeconds(2)));
        var pending = manager.PendingZones;
        gate.Set();
        await Task.WhenAll(tasks);

        await Assert.That(exceededLimit).IsFalse();
        await Assert.That(pending <= 4).IsTrue();
        await Assert.That(source.MaximumReaders <= 4).IsTrue();
        await Assert.That(manager.PendingZones).IsEqualTo(0);
    }

    [Test]
    public async Task PhysicsShapes_PreservesDistinctInstancesAcrossOverlappingCells()
    {
        var template = Template();
        template.XmlWorldZones[137].Cells[(1, 0)] = new XmlWorldCell { X = 1, Y = 0 };
        var mesh = CollisionMesh.Build([new Vector3(-1f), new Vector3(1f), new Vector3(-1f, 1f, -1f)], [0, 1, 2]);
        var collision = StaticCollision.FromInstances([
            (mesh, Matrix4x4.CreateTranslation(1024f, 100f, 10f)),
            (mesh, Matrix4x4.CreateTranslation(1024f, 110f, 10f)),
            (mesh, Matrix4x4.CreateScale(2f) * Matrix4x4.CreateTranslation(1024f, 100f, 10f)),
            (mesh, Matrix4x4.CreateTranslation(1024f, 100f, 10f))
        ]);
        var manager = new StaticGeometryManager(new MemoryFiles(Scene()));
        var shapes = new List<StaticCollision.StaticShape>(manager.PhysicsShapes(template, 137, collision));

        // Four placements, including two identical boxes, each appear once although both cells select them.
        await Assert.That(shapes.Count).IsEqualTo(4);
    }

    [Test]
    public async Task ZoneBuilt_CallbacksAreSerializedAndInvalidateDropsAfterBuild()
    {
        using var callbackEntered = new ManualResetEventSlim(false);
        using var releaseCallback = new ManualResetEventSlim(false);
        var template = Template();
        var manager = new StaticGeometryManager(new MemoryFiles(Scene()));
        var events = new ConcurrentQueue<string>();
        var callbacks = 0;
        var overlappingCallbacks = 0;
        manager.ZoneBuilt += (_, _, _) =>
        {
            if (Interlocked.Increment(ref callbacks) > 1)
                Interlocked.Increment(ref overlappingCallbacks);
            events.Enqueue("built");
            callbackEntered.Set();
            releaseCallback.Wait(TimeSpan.FromSeconds(5));
            Interlocked.Decrement(ref callbacks);
        };
        manager.ZoneDropped += (_, _) =>
        {
            if (Interlocked.Increment(ref callbacks) > 1)
                Interlocked.Increment(ref overlappingCallbacks);
            events.Enqueue("dropped");
            Interlocked.Decrement(ref callbacks);
        };
        var build = manager.EnsureZoneAsync(template, 137);
        var entered = callbackEntered.Wait(TimeSpan.FromSeconds(5));
        var invalidation = Task.Run(() => manager.Invalidate(template.Id));
        releaseCallback.Set();
        await Task.WhenAll(build, invalidation);

        await Assert.That(entered).IsTrue();
        await Assert.That(string.Join(",", events)).IsEqualTo("built,dropped");
        await Assert.That(overlappingCallbacks).IsEqualTo(0);
    }

    [Test]
    public async Task ZoneBuilt_ReentrantInvalidationDefersDropUntilTheCallbackReturns()
    {
        var template = Template();
        var manager = new StaticGeometryManager(new MemoryFiles(Scene()));
        var events = new List<string>();
        manager.ZoneBuilt += (_, _, _) =>
        {
            events.Add("build-start");
            manager.Invalidate(template.Id);
            events.Add("build-end");
        };
        manager.ZoneDropped += (_, _) => events.Add("drop");

        await manager.EnsureZoneAsync(template, 137);

        await Assert.That(string.Join(",", events)).IsEqualTo("build-start,build-end,drop");
        await Assert.That(manager.CachedZones).IsEqualTo(0);
    }

    private sealed class FailingFiles : IGeometryFileSource
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public byte[]? TryReadBytes(string path)
        {
            Interlocked.Increment(ref _reads);
            throw new IOException("Synthetic geometry read failure.");
        }
    }

    private sealed class BlockingFiles(ManualResetEventSlim gate, ManualResetEventSlim entered, string? blockedPathPart = null) : IGeometryFileSource
    {
        private int _readers;
        private int _maximumReaders;

        public int MaximumReaders => Volatile.Read(ref _maximumReaders);

        public byte[]? TryReadBytes(string path)
        {
            if (blockedPathPart != null && !path.Contains(blockedPathPart, StringComparison.Ordinal))
                return null;
            var readers = Interlocked.Increment(ref _readers);
            var previous = Volatile.Read(ref _maximumReaders);
            while (readers > previous)
            {
                var observed = Interlocked.CompareExchange(ref _maximumReaders, readers, previous);
                if (observed == previous)
                    break;
                previous = observed;
            }

            entered.Set();
            try
            {
                gate.Wait(TimeSpan.FromSeconds(10));
                return null;
            }
            finally
            {
                Interlocked.Decrement(ref _readers);
            }
        }
    }
}
