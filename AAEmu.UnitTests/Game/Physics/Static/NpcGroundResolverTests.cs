using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using AAEmu.Game.Core.Managers.World;

using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Xml;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Physics.Static;
using AAEmu.Geometry;
using AAEmu.Geometry.Abstractions;
using AAEmu.UnitTests.Geometry;

namespace AAEmu.UnitTests.Game.Physics.Static;

/// <summary>
/// План S3: НПЦ оседает на настоящую поверхность под точкой спавна, а не на 0.4 м ниже пола. Сцена синтетическая —
/// одна кисть-ящик, без клиентских данных.
/// </summary>
public class NpcGroundResolverTests
{
    [Test]
    public async Task DeferredSpawnSettlement_WaitsForGeometryAndCorrectsAnIdleNpcWithoutAi()
    {
        using var gate = new ManualResetEventSlim(false);
        var files = new Dictionary<string, byte[]>
        {
            [CellObjectDat] = AAEmu.UnitTests.Geometry.ObjectDat.WithBoxBrushes(BoxAsset, [new Vector3(100f, 100f, 10f)]),
            [BoxAsset] = AAEmu.UnitTests.Geometry.CgfFixture.WithBoxProxy()
        };
        var template = Template();
        var geometry = new StaticGeometryManager(new WaitingFiles(files, gate));
        var pending = new NpcSpawnGrounding(geometry, new NpcGroundResolver(geometry));
        var npc = new Npc { Hp = 1, IsVisible = true };
        typeof(GameObject).GetField("_parentWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(npc, new WorldInstance(template, 0, true, 0));
        npc.Transform.Local.SetPosition(100f, 100f, 12f);
        try
        {
            pending.Track(npc, 137);
            pending.Tick();
            await Assert.That(npc.Transform.World.Position.Z).IsEqualTo(12f);
            gate.Set();
            await geometry.EnsureZoneAsync(template, 137);
            pending.Tick();
            await Assert.That(npc.Transform.World.Position.Z).IsEqualTo(11f).Within(0.01f);
        }
        finally { gate.Set(); }
    }

    [Test]
    public async Task DeferredSpawnSettlement_DoesNotMoveNpcThatHasLeftItsSpawn()
    {
        var template = Template();
        var geometry = NewManager();
        await geometry.EnsureZoneAsync(template, 137);
        var pending = new NpcSpawnGrounding(geometry, new NpcGroundResolver(geometry));
        var npc = new Npc { Hp = 1, IsVisible = true };
        typeof(GameObject).GetField("_parentWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(npc, new WorldInstance(template, 0, true, 0));
        npc.Transform.Local.SetPosition(100f, 100f, 12f);
        pending.Track(npc, 137);
        npc.Transform.Local.SetPosition(101f, 100f, 12f);
        pending.Tick();
        await Assert.That(npc.Transform.World.Position.Z).IsEqualTo(12f);
    }

    [Test]
    public async Task DeferredSpawnSettlement_UpdatesAiHomeAndIgnoresDespawnedNpc()
    {
        var template = Template();
        var geometry = NewManager();
        await geometry.EnsureZoneAsync(template, 137);
        var pending = new NpcSpawnGrounding(geometry, new NpcGroundResolver(geometry));
        var world = new WorldInstance(template, 0, true, 0);
        var npc = new Npc { Hp = 1, IsVisible = true };
        typeof(GameObject).GetField("_parentWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(npc, world);
        var spawn = new Vector3(100f, 100f, 12f);
        npc.Transform.Local.SetPosition(spawn.X, spawn.Y, spawn.Z);
        npc.Ai = new DefaultAiCharacter { Owner = npc, HomePosition = spawn, IdlePosition = spawn };
        pending.Track(npc, 137);
        pending.Tick();
        await Assert.That(npc.Ai.HomePosition.Z).IsEqualTo(11f).Within(0.01f);
        await Assert.That(npc.Ai.IdlePosition.Z).IsEqualTo(11f).Within(0.01f);
        npc.Transform.Local.SetPosition(spawn.X, spawn.Y, spawn.Z);
        pending.Track(npc, 137);
        npc.IsVisible = false;
        pending.Tick();
        await Assert.That(npc.Transform.World.Position.Z).IsEqualTo(12f);
    }

    private sealed class WaitingFiles(Dictionary<string, byte[]> files, ManualResetEventSlim gate) : IGeometryFileSource
    {
        public byte[]? TryReadBytes(string path) { gate.Wait(TimeSpan.FromSeconds(10)); return files.GetValueOrDefault(path); }
    }
    [Test]
    public async Task BrushOrTerrain_WhenTerrainIsFarBelow_UsesNearbyNavigationFloor()
    {
        var template = Template();
        template.CellX = template.CellY = 1;
        var cell = new WorldCell(0, 0, template);
        var samples = new ushort[WorldManager.CELL_HMAP_RESOLUTION, WorldManager.CELL_HMAP_RESOLUTION];
        samples[50, 50] = ushort.MaxValue;
        typeof(WorldCell).GetProperty(nameof(WorldCell.HeightMap), BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(cell, samples);
        typeof(WorldCell).GetProperty(nameof(WorldCell.HeightCoefficient))!.SetValue(cell, ushort.MaxValue / 100f);
        typeof(WorldCell).GetProperty(nameof(WorldCell.HasHeightMap))!.SetValue(cell, true);
        typeof(WorldCell).GetProperty(nameof(WorldCell.Loaded))!.SetValue(cell, true);
        template.Cells[0, 0] = cell;
        var manager = new StaticGeometryManager(new MemoryFiles(new Dictionary<string, byte[]>()));
        await manager.EnsureZoneAsync(template, 137);
        var resolver = new NpcGroundResolver(manager);

        var height = resolver.BrushOrTerrain(template, 137, 100f, 100f, 105f, () => 104.9f, out _);

        await Assert.That(height).IsEqualTo(104.9f).Within(0.01f);
    }

    /// <summary>Терраса: карта высот вокруг (100, 100) стоит на 100 м, остальное «нет данных».</summary>
    private static WorldTemplate TemplateWithTerrainAt100()
    {
        var template = Template();
        template.CellX = template.CellY = 1;
        var cell = new WorldCell(0, 0, template);
        var samples = new ushort[WorldManager.CELL_HMAP_RESOLUTION, WorldManager.CELL_HMAP_RESOLUTION];
        samples[50, 50] = ushort.MaxValue; // 100 m with the coefficient below
        typeof(WorldCell).GetProperty(nameof(WorldCell.HeightMap), BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(cell, samples);
        typeof(WorldCell).GetProperty(nameof(WorldCell.HeightCoefficient))!.SetValue(cell, ushort.MaxValue / 100f);
        typeof(WorldCell).GetProperty(nameof(WorldCell.HasHeightMap))!.SetValue(cell, true);
        typeof(WorldCell).GetProperty(nameof(WorldCell.Loaded))!.SetValue(cell, true);
        template.Cells[0, 0] = cell;
        return template;
    }

    [Test]
    public async Task Ground_UnderTheHeightmap_KeepsTheUnitsOwnHeight()
    {
        // Пещера/зал под холмом: юнит в 12 м под картой высот. Рельеф над ним — не опора (их GroundResolver
        // .UndergroundFloor), иначе НПЦ поднимается на поверхность холма сквозь камень.
        var template = TemplateWithTerrainAt100();
        var geometry = new StaticGeometryManager(new MemoryFiles(new Dictionary<string, byte[]>()));
        await geometry.EnsureZoneAsync(template, 137);
        var resolver = new NpcGroundResolver(geometry);

        var height = resolver.Ground(template, 137, 100f, 100f, 88f);

        await Assert.That(height).IsEqualTo(88f).Within(0.01f);
    }

    [Test]
    public async Task Ground_UnderTheHeightmap_TakesTheFloorBeneathTheUnit()
    {
        // Тот же зал, но под юнитом пол кисти (верх 87 м): подземный юнит встаёт на него, а не на рельеф.
        var template = TemplateWithTerrainAt100();
        var geometry = NewManager(centerZ: 86f);
        await geometry.EnsureZoneAsync(template, 137);
        var resolver = new NpcGroundResolver(geometry);

        var height = resolver.Ground(template, 137, 100f, 100f, 88f);

        await Assert.That(height).IsEqualTo(87f).Within(0.01f);
    }

    [Test]
    public async Task Ground_UsesDestinationZoneGeometryAtZoneBoundary()
    {
        var template = Template();
        template.OceanLevel = 0f;
        template.ZoneKeyByRegions = new uint[3, 3];
        template.ZoneKeyByRegions[1, 1] = 138;
        template.XmlWorldZones[138] = template.XmlWorldZones[137];
        template.XmlWorldZones[137] = new XmlWorldZone { Id = 137, Cells = new ConcurrentDictionary<(int, int), XmlWorldCell>() };
        var geometry = NewManager();
        await geometry.EnsureZoneAsync(template, 137);
        await geometry.EnsureZoneAsync(template, 138);
        var resolver = new NpcGroundResolver(geometry);

        var height = resolver.Ground(template, 137, 100f, 100f, 12f);

        await Assert.That(height).IsEqualTo(11f).Within(0.01f);
    }

    [Test]
    public async Task ClipWalk_StopsAtWallInNeighborZone()
    {
        var template = Template();
        template.ZoneKeyByRegions = new uint[3, 3];
        template.ZoneKeyByRegions[1, 1] = 137;
        template.ZoneKeyByRegions[2, 1] = 138;
        template.XmlWorldZones[138] = template.XmlWorldZones[137];
        template.XmlWorldZones[137] = new XmlWorldZone { Id = 137, Cells = new ConcurrentDictionary<(int, int), XmlWorldCell>() };
        var geometry = NewManager(centerX: 129f);
        await geometry.EnsureZoneAsync(template, 137);
        await geometry.EnsureZoneAsync(template, 138);
        var resolver = new NpcGroundResolver(geometry);

        var at = resolver.ClipWalk(template, 137, new Vector3(127f, 100f, 9f), new Vector3(129f, 100f, 9f), 0.45f, new WalkerCollision.Probe(), out var blocked);

        await Assert.That(blocked).IsTrue();
        await Assert.That(at.X).IsLessThan(128f);
    }
    private sealed class MemoryFiles(Dictionary<string, byte[]> files) : IGeometryFileSource
    {
        public byte[]? TryReadBytes(string path) => files.GetValueOrDefault(path);
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

    /// <summary>One brush: a 2 m box centred on (100, 100), its top at 11 m.</summary>
    private static StaticGeometryManager NewManager(float centerZ = 10f, float centerX = 100f)
    {
        var files = new Dictionary<string, byte[]>
        {
            [CellObjectDat] = AAEmu.UnitTests.Geometry.ObjectDat.WithBoxBrushes(BoxAsset, [new Vector3(centerX, 100f, centerZ)]),
            [BoxAsset] = AAEmu.UnitTests.Geometry.CgfFixture.WithBoxProxy()
        };
        return new StaticGeometryManager(new MemoryFiles(files));
    }

    [Test]
    public async Task WaterStanceAt_OnABrushHalfAMetreUnderWater_SettlesOnTheKnownFloor()
    {
        var geometry = NewManager(centerZ: 98.5f);
        var template = Template();
        await geometry.EnsureZoneAsync(template, 137);
        var resolver = new NpcGroundResolver(geometry);

        var stance = resolver.WaterStanceAt(template, 137, 100f, 100f, 99.5f);

        await Assert.That(stance).IsEqualTo(WaterStance.Settle);
    }

    [Test]
    public async Task WaterStanceAt_AboveABrushInShallowWater_Swims()
    {
        var geometry = NewManager(centerZ: 98f);
        var template = Template();
        await geometry.EnsureZoneAsync(template, 137);
        var resolver = new NpcGroundResolver(geometry);

        var stance = resolver.WaterStanceAt(template, 137, 100f, 100f, 99.5f);

        await Assert.That(stance).IsEqualTo(WaterStance.Swim);
    }

    [Test]
    public async Task WaterStanceAt_WithoutAFloorInShallowWater_Swims()
    {
        var geometry = new StaticGeometryManager(new MemoryFiles(new Dictionary<string, byte[]>()));
        var template = Template();
        await geometry.EnsureZoneAsync(template, 137);
        var resolver = new NpcGroundResolver(geometry);

        var stance = resolver.WaterStanceAt(template, 137, 100f, 100f, 99.5f);

        await Assert.That(stance).IsEqualTo(WaterStance.Swim);
    }

    [Test]
    public async Task SettledZ_FromASpawnerSlightlyAboveTheFloor_SeatsTheNpcOnTheBrush()
    {
        var geometry = NewManager();
        var template = Template();
        await geometry.EnsureZoneAsync(template, 137);
        var resolver = new NpcGroundResolver(geometry);

        // Spawners are often a metre or so above the floor they mean.
        var z = resolver.SettledZ(template, 137, 100f, 100f, 12f);

        await Assert.That(z).IsEqualTo(11f).Within(0.01f);
    }

    [Test]
    public async Task SettledZ_WithTheWorldHeightBelowTheSpawnPoint_StillSeatsOnTheBrush()
    {
        var geometry = NewManager();
        var template = Template();
        await geometry.EnsureZoneAsync(template, 137);
        var resolver = new NpcGroundResolver(geometry);

        // The world lowered the unit to 10.5 before the settle: the brush at 11 must still be found, not skipped because
        // it is below the already lowered reference.
        var z = resolver.SettledZ(template, 137, 100f, 100f, 12f, worldZ: 10.5f);

        await Assert.That(z).IsEqualTo(11f).Within(0.01f);
    }

    [Test]
    public async Task SettledZ_WithoutAnyFloor_KeepsTheSpawnHeight()
    {
        // No verified support: lowering the NPC could place it inside an unloaded floor.
        var geometry = new StaticGeometryManager(new MemoryFiles(new Dictionary<string, byte[]>()));
        var template = Template();
        await geometry.EnsureZoneAsync(template, 137);
        var resolver = new NpcGroundResolver(geometry);

        var z = resolver.SettledZ(template, 137, 100f, 100f, 12f);

        await Assert.That(z).IsEqualTo(12f).Within(0.01f);
    }

    [Test]
    public async Task SettledZ_AcrossACliff_KeepsTheSpawnersHeight()
    {
        var geometry = NewManager();
        var template = Template();
        await geometry.EnsureZoneAsync(template, 137);
        var resolver = new NpcGroundResolver(geometry);

        // 100 m above everything: no floor within the near band, so the unit is not teleported down.
        var z = resolver.SettledZ(template, 137, 100f, 100f, 111f);

        await Assert.That(z).IsEqualTo(111f).Within(0.01f);
    }
}
