using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

[NotInParallel]
public class PortedActorSpecialEffectsTests
{
    private readonly FieldInfo _worldManagerField = typeof(Singleton<WorldManager>)
        .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
    private object _previousWorldManager;

    [Before(Test)]
    public void Setup()
    {
        _previousWorldManager = _worldManagerField.GetValue(null);
        _worldManagerField.SetValue(null, new WorldManager(null, null, null, null, null));
    }

    [After(Test)]
    public void TearDown() => _worldManagerField.SetValue(null, _previousWorldManager);

    [Test]
    public async Task NpcDespawnQueuesOnlyTheEffectiveSourceOnce()
    {
        var world = CreateWorld();
        var caster = new Npc { ObjId = 10, ParentWorld = world };
        var target = new Npc { ObjId = 11, ParentWorld = world };
        world.AddObject(caster);
        world.AddObject(target);
        var action = new NpcDespawn();

        Execute(action, caster, target);
        Execute(action, caster, target);
        Execute(action, new TestCharacter { ObjId = 20, ParentWorld = world }, target);

        var pending = (HashSet<GameObject>)typeof(SpawnManager).GetMethod("GetDespawnsReady",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world.SpawnManager, null)!;
        await Assert.That(pending.Count).IsEqualTo(1);
        await Assert.That(pending.Contains(caster)).IsTrue();
        await Assert.That(pending.Contains(target)).IsFalse();
    }

    [Test]
    public async Task KillWithoutCorpseQueuesTheMatchingTargetAndNeighborInsteadOfTheCaster()
    {
        const uint removedTemplate = 9436;
        var world = CreateWorld();
        var caster = PlaceNpc(world, 10, 1, 5);
        var target = PlaceNpc(world, 11, removedTemplate, 6);
        var neighbor = PlaceNpc(world, 12, removedTemplate, 8);
        var distant = PlaceNpc(world, 13, removedTemplate, 30);
        var otherTemplate = PlaceNpc(world, 14, 99, 8);
        var effect = new KillNpcWithoutCorpseEffect { NpcId = removedTemplate, Radius = 6 };

        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), target, null, null, null, null, DateTime.UtcNow);
        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), target, null, null, null, null, DateTime.UtcNow);

        var pending = GetQueuedDespawns(world);
        await Assert.That(pending.Count).IsEqualTo(2);
        await Assert.That(pending.Contains(target)).IsTrue();
        await Assert.That(pending.Contains(neighbor)).IsTrue();
        await Assert.That(pending.Contains(caster)).IsFalse();
        await Assert.That(pending.Contains(distant)).IsFalse();
        await Assert.That(pending.Contains(otherTemplate)).IsFalse();
    }

    [Test]
    public async Task CorpseFreeSelfVanishRequiresTheAuthoredNpcTemplate()
    {
        var world = CreateWorld();
        var caster = PlaceNpc(world, 10, 7711, 5);
        var other = PlaceNpc(world, 11, 99, 6);
        var effect = new KillNpcWithoutCorpseEffect { NpcId = 7711, Vanish = true };

        effect.Apply(other, new SkillCasterUnit(other.ObjId), null, null, null, null, null, DateTime.UtcNow);
        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), null, null, null, null, null, DateTime.UtcNow);

        var pending = GetQueuedDespawns(world);
        await Assert.That(pending.Count).IsEqualTo(1);
        await Assert.That(pending.Contains(caster)).IsTrue();
    }

    [Test]
    public async Task MoveToGroundUsesThePlotMarkersHorizontalPositionAndTerrainHeight()
    {
        var world = CreateWorld(terrainHeight: 12);
        var caster = new TestCharacter { ObjId = 11, ParentWorld = world };
        caster.Transform.Local.SetPosition(2, 3, 40);
        var marker = new BaseUnit { ObjId = uint.MaxValue, ParentWorld = world };
        marker.Transform.Local.SetPosition(6, 9, 25);

        Execute(new MoveToGround(), caster, marker);

        await Assert.That(caster.Transform.World.Position).IsEqualTo(new Vector3(6, 9, 12));
        await Assert.That(marker.Transform.World.Position).IsEqualTo(new Vector3(6, 9, 25));
    }

    [Test]
    public async Task MoveToGroundRespectsAPlotMarkerWhenNoTerrainSampleExists()
    {
        var world = CreateWorld();
        var caster = new TestCharacter { ObjId = 11, ParentWorld = world };
        caster.Transform.Local.SetPosition(2, 3, 40);
        var marker = new BaseUnit { ObjId = uint.MaxValue, ParentWorld = world };
        marker.Transform.Local.SetPosition(6, 9, 25);

        Execute(new MoveToGround(), caster, null);
        await Assert.That(caster.Transform.World.Position).IsEqualTo(new Vector3(2, 3, 40));
        Execute(new MoveToGround(), caster, marker);
        await Assert.That(caster.Transform.World.Position).IsEqualTo(new Vector3(6, 9, 25));
    }

    private static WorldInstance CreateWorld(int? terrainHeight = null)
    {
        var sectors = WorldManager.SECTORS_PER_CELL;
        var template = new WorldTemplate { CellX = 1, CellY = 1, ZoneKeyByRegions = new uint[sectors, sectors] };
        var cell = new WorldCell(0, 0, template);
        typeof(WorldCell).GetProperty(nameof(WorldCell.Loaded))!.SetValue(cell, true);
        template.Cells[0, 0] = cell;
        if (terrainHeight.HasValue)
        {
            var samples = new ushort[WorldManager.CELL_HMAP_RESOLUTION, WorldManager.CELL_HMAP_RESOLUTION];
            for (var x = 0; x < 8; x++)
            for (var y = 0; y < 8; y++)
                samples[x, y] = (ushort)terrainHeight.Value;
            typeof(WorldCell).GetProperty(nameof(WorldCell.HeightMap), BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(cell, samples);
            typeof(WorldCell).GetProperty(nameof(WorldCell.HeightCoefficient))!.SetValue(cell, 1d);
            typeof(WorldCell).GetProperty(nameof(WorldCell.HasHeightMap))!.SetValue(cell, true);
        }

        var world = new WorldInstance(template, 0, true, 1) { Regions = new Region[sectors, sectors] };
        world.SpawnManager = new SpawnManager(world);
        typeof(WorldManager).GetField("_worlds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(WorldManager.Instance, new ConcurrentDictionary<uint, WorldInstance> { [world.Id] = world });
        return world;
    }

    private static Npc PlaceNpc(WorldInstance world, uint objId, uint template, float x)
    {
        var npc = new Npc { ObjId = objId, TemplateId = template, ParentWorld = world, Hp = 1 };
        npc.Transform.Local.SetPosition(x, 5, 10);
        npc.Region = world.GetRegionByPos(npc.Transform.World.Position);
        npc.Region.AddObject(npc);
        world.AddObject(npc);
        return npc;
    }

    private static HashSet<GameObject> GetQueuedDespawns(WorldInstance world) =>
        (HashSet<GameObject>)typeof(SpawnManager).GetProperty("Despawns", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(world.SpawnManager)!;

    private static void Execute(SpecialEffectAction action, BaseUnit caster, BaseUnit target) =>
        action.Execute(caster, null, target, null, null, null, null, DateTime.UtcNow, 0, 0, 0, 0);

    private sealed class TestCharacter() : Character(new UnitCustomModelParams())
    {
        public override void SetPosition(float x, float y, float z, float rotationX, float rotationY, float rotationZ) =>
            Transform.Local.SetPosition(x, y, z, rotationX, rotationY, rotationZ);
    }
}
