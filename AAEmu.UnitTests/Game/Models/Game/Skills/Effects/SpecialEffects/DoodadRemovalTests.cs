using System.Collections.Concurrent;
using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

[NotInParallel]
public class DoodadRemovalTests
{
    private readonly List<(FieldInfo Field, object Previous)> _singletons = [];
    private WorldInstance _world;
    private WorldInstance _otherWorld;
    private Npc _leviathan;

    [Before(Test)]
    public void Setup()
    {
        var worlds = new WorldManager(null, null, null, null, null);
        Install(worlds);
        var sectors = WorldManager.SECTORS_PER_CELL;
        var template = new WorldTemplate { CellX = 4, CellY = 1, ZoneKeyByRegions = new uint[4 * sectors, sectors] };
        _world = new WorldInstance(template, 0, true, 1) { Regions = new Region[4 * sectors, sectors] };
        _otherWorld = new WorldInstance(template, 0, true, 2) { Regions = new Region[4 * sectors, sectors] };
        SetField(worlds, "_worlds", new ConcurrentDictionary<uint, WorldInstance> { [1] = _world, [2] = _otherWorld });
        _leviathan = new Npc { ObjId = 10, Hp = 1 };
        Place(_world, _leviathan, 63);

        var skills = new SkillManager(null, null);
        Install(skills);
        SetField(skills, "_effects", new Dictionary<string, Dictionary<uint, EffectTemplate>>
        {
            [nameof(SpecialEffect)] = new()
            {
                [21851] = new SpecialEffect { SpecialEffectTypeId = SpecialType.RemoveDoodadGroup, Value1 = 38, Value2 = 50000 },
                [25435] = new SpecialEffect { SpecialEffectTypeId = SpecialType.RemoveAllDoodad, Value1 = 50000 }
            }
        });
    }

    [After(Test)]
    public void TearDown()
    {
        foreach (var (field, previous) in _singletons)
            field.SetValue(null, previous);
    }

    [Test]
    public async Task TheStockPlotRemovesLooseGroup38PacksAcrossRegionsWithinFiftyMeters()
    {
        var pack = DoodadAt(_world, 11, 112, group: 38);
        var otherGroup = DoodadAt(_world, 12, 112, group: 24);
        var outside = DoodadAt(_world, 13, 114, group: 38);
        var otherInstance = DoodadAt(_otherWorld, 11, 112, group: 38);
        var housePack = DoodadAt(_world, 14, 82, group: 38);
        housePack.OwnerType = DoodadOwnerType.Housing;
        housePack.IsPersistent = true;
        var attachedPack = DoodadAt(_world, 15, 82, group: 38);
        attachedPack.ParentObjId = 400;

        ApplyLeviathanPlotEffect(21851);
        ApplyLeviathanPlotEffect(21851);

        await Assert.That(_world.GetDoodad(pack.ObjId)).IsNull();
        await Assert.That(_world.GetDoodad(otherGroup.ObjId)).IsSameReferenceAs(otherGroup);
        await Assert.That(_world.GetDoodad(outside.ObjId)).IsSameReferenceAs(outside);
        await Assert.That(_world.GetDoodad(housePack.ObjId)).IsSameReferenceAs(housePack);
        await Assert.That(_world.GetDoodad(attachedPack.ObjId)).IsSameReferenceAs(attachedPack);
        await Assert.That(_otherWorld.GetDoodad(otherInstance.ObjId)).IsSameReferenceAs(otherInstance);
    }

    [Test]
    public async Task TheStockAreaEffectRemovesTemporaryCollisionObstaclesAndSparesFixtures()
    {
        var barrier = DoodadAt(_world, 11, 74, despawnOnCollision: true);
        barrier.OwnerObjId = 22;
        var staticFixture = DoodadAt(_world, 12, 74, despawnOnCollision: true);
        staticFixture.OwnerObjId = 22;
        staticFixture.Spawner = new DoodadSpawner();
        var unrelatedDoodad = DoodadAt(_world, 13, 74);
        unrelatedDoodad.OwnerObjId = 22;
        var attachedObstacle = DoodadAt(_world, 14, 74, despawnOnCollision: true);
        attachedObstacle.OwnerObjId = 22;
        attachedObstacle.ParentObjId = 120;
        var playerProperty = DoodadAt(_world, 15, 74, despawnOnCollision: true);
        playerProperty.OwnerObjId = 22;
        playerProperty.IsPersistent = true;
        var distant = DoodadAt(_world, 16, 114, despawnOnCollision: true);
        distant.OwnerObjId = 22;
        var noCreator = DoodadAt(_world, 17, 74, despawnOnCollision: true);

        ApplyLeviathanPlotEffect(25435);

        await Assert.That(_world.GetDoodad(barrier.ObjId)).IsNull();
        await Assert.That(_world.GetDoodad(staticFixture.ObjId)).IsSameReferenceAs(staticFixture);
        await Assert.That(_world.GetDoodad(unrelatedDoodad.ObjId)).IsSameReferenceAs(unrelatedDoodad);
        await Assert.That(_world.GetDoodad(attachedObstacle.ObjId)).IsSameReferenceAs(attachedObstacle);
        await Assert.That(_world.GetDoodad(playerProperty.ObjId)).IsSameReferenceAs(playerProperty);
        await Assert.That(_world.GetDoodad(distant.ObjId)).IsSameReferenceAs(distant);
        await Assert.That(_world.GetDoodad(noCreator.ObjId)).IsSameReferenceAs(noCreator);
    }

    [Test]
    public async Task UnverifiedAreaEffectModeLeavesNearbyObjectsInPlace()
    {
        var barrier = DoodadAt(_world, 11, 74, despawnOnCollision: true);
        barrier.OwnerObjId = 22;
        var skill = new Skill(new SkillTemplate { Id = 27749, TargetSelection = SkillTargetSelection.Source });
        var cast = new CastPlot(1655, 0, 17372, skill.Id);

        new RemoveAllDoodad().Execute(_leviathan, null, _leviathan, null, cast, skill, null, DateTime.UtcNow, 50000, 1, 0, 0);

        await Assert.That(_world.GetDoodad(barrier.ObjId)).IsSameReferenceAs(barrier);
    }

    [Test]
    public async Task GroupRemovalCentersTargetSelectedSkillsOnTheTargetedDoodad()
    {
        var sourceNeighbor = DoodadAt(_world, 11, 78, group: 38);
        var target = DoodadAt(_world, 12, 160, group: 38);
        var targetNeighbor = DoodadAt(_world, 13, 175, group: 38);
        var skill = new Skill(new SkillTemplate { TargetSelection = SkillTargetSelection.Target });

        new RemoveDoodadGroup().Execute(_leviathan, null, target, null, null, skill, null, DateTime.UtcNow, 38, 50000, 0, 0);

        await Assert.That(_world.GetDoodad(target.ObjId)).IsNull();
        await Assert.That(_world.GetDoodad(targetNeighbor.ObjId)).IsNull();
        await Assert.That(_world.GetDoodad(sourceNeighbor.ObjId)).IsSameReferenceAs(sourceNeighbor);
    }

    [Test]
    public async Task ExactRemovalUsesTheSiegeCannonImpactAndAuthoredTemplateWithinFifteenMeters()
    {
        var marker = new BaseUnit { ObjId = uint.MaxValue, ParentWorld = _world };
        marker.Transform.Local.SetPosition(280, 90, 10);
        marker.Region = _world.GetRegionByPos(marker.Transform.World.Position);
        var sourceNeighbor = DoodadAt(_world, 11, 65);
        var barrier = DoodadAt(_world, 12, 294);
        var outside = DoodadAt(_world, 13, 296);
        var differentBarrier = DoodadAt(_world, 14, 285, idOverride: 12230);
        var housing = DoodadAt(_world, 15, 284);
        housing.OwnerType = DoodadOwnerType.Housing;
        housing.IsPersistent = true;
        var otherWorldBarrier = DoodadAt(_otherWorld, 12, 294);

        ApplyExactTemplateRemoval(_leviathan, 13567, SkillTargetSelection.Target, 3060, 15000,
            SkillTargetType.ArtilleryPos, initialTarget: marker);

        await Assert.That(_world.GetDoodad(barrier.ObjId)).IsNull();
        await Assert.That(_world.GetDoodad(sourceNeighbor.ObjId)).IsSameReferenceAs(sourceNeighbor);
        await Assert.That(_world.GetDoodad(outside.ObjId)).IsSameReferenceAs(outside);
        await Assert.That(_world.GetDoodad(differentBarrier.ObjId)).IsSameReferenceAs(differentBarrier);
        await Assert.That(_world.GetDoodad(housing.ObjId)).IsSameReferenceAs(housing);
        await Assert.That(_otherWorld.GetDoodad(otherWorldBarrier.ObjId)).IsSameReferenceAs(otherWorldBarrier);
    }

    [Test]
    public async Task ExactRemovalCanRemoveAnAuthoredWorldSpawnAtOneMeter()
    {
        var obstacle = DoodadAt(_world, 11, 63.5f, group: 11, idOverride: 1556);
        obstacle.OwnerType = DoodadOwnerType.System;
        obstacle.Spawner = new DoodadSpawner();
        var outside = DoodadAt(_world, 12, 65, group: 11, idOverride: 1556);

        ApplyExactTemplateRemoval(_leviathan, 17302, SkillTargetSelection.Source, 1556, 1000);

        await Assert.That(_world.GetDoodad(obstacle.ObjId)).IsNull();
        await Assert.That(_world.GetDoodad(outside.ObjId)).IsSameReferenceAs(outside);
    }

    [Test]
    public async Task ExactRemovalFromTheStockBoardingBuffUsesItsOwnerAndCrossesManyRegions()
    {
        var owner = new Npc { ObjId = 100, Hp = 1 };
        Place(_world, owner, 200);
        var inRangeFromOwner = DoodadAt(_world, 11, 2690);
        var outOfRange = DoodadAt(_world, 12, 2703);
        var otherTemplate = DoodadAt(_world, 13, 2690, idOverride: 12230);
        var template = new BuffTemplate { Id = 21368 };
        var source = new SkillCasterUnit(_leviathan.ObjId);
        var buff = new Buff(owner, _leviathan, source, template, null, DateTime.UtcNow);
        var effect = new SpecialEffect { SpecialEffectTypeId = SpecialType.RemoveDoodad, Value1 = 3060, Value2 = 2500000 };

        effect.Apply(_leviathan, source, owner, new SkillCastUnitTarget(owner.ObjId), new CastBuff(buff),
            new EffectSource(template), null, DateTime.UtcNow);

        await Assert.That(_world.GetDoodad(inRangeFromOwner.ObjId)).IsNull();
        await Assert.That(_world.GetDoodad(outOfRange.ObjId)).IsSameReferenceAs(outOfRange);
        await Assert.That(_world.GetDoodad(otherTemplate.ObjId)).IsSameReferenceAs(otherTemplate);
    }

    [Test]
    public async Task ZeroRadiusInTheStockPullDoesNotDeleteNearTheCaster()
    {
        var doodad = DoodadAt(_world, 11, 64, group: 24, idOverride: 3248);
        var target = new Npc { ObjId = 100, Hp = 1 };
        Place(_world, target, 68);

        ApplyExactTemplateRemoval(target, 15027, SkillTargetSelection.Target, 3248, 0);

        await Assert.That(_world.GetDoodad(doodad.ObjId)).IsSameReferenceAs(doodad);
    }

    [Test]
    public async Task PositionalRemovalWithoutAnActualImpactMarkerDoesNotDeleteNearAFallbackUnit()
    {
        var doodad = DoodadAt(_world, 11, 65);

        ApplyExactTemplateRemoval(_leviathan, 13567, SkillTargetSelection.Target, 3060, 15000, SkillTargetType.ArtilleryPos);

        await Assert.That(_world.GetDoodad(doodad.ObjId)).IsSameReferenceAs(doodad);
    }

    private void ApplyExactTemplateRemoval(BaseUnit target, uint skillId, SkillTargetSelection selection, int templateId,
        int radiusMillimeters, SkillTargetType type = SkillTargetType.Self, BaseUnit initialTarget = null)
    {
        var skill = new Skill(new SkillTemplate { Id = skillId, TargetSelection = selection, TargetType = type })
        {
            InitialTarget = initialTarget
        };
        var effect = new SpecialEffect { SpecialEffectTypeId = SpecialType.RemoveDoodad, Value1 = templateId, Value2 = radiusMillimeters };
        effect.Apply(_leviathan, new SkillCasterUnit(_leviathan.ObjId), target, new SkillCastUnitTarget(target.ObjId),
            new CastSkill(skill.Id, 0), new EffectSource(skill), null, DateTime.UtcNow);
    }

    private void ApplyLeviathanPlotEffect(uint effectId)
    {
        var skill = new Skill(new SkillTemplate { Id = 27749, TargetSelection = SkillTargetSelection.Source });
        var state = new PlotState(_leviathan, new SkillCasterUnit(_leviathan.ObjId), _leviathan, new SkillCastUnitTarget(_leviathan.ObjId), null, skill);
        var evt = new PlotEventTemplate { Id = 17372, PlotId = 1655, SourceUpdateMethodId = 3, TargetUpdateMethodId = 4 };
        var context = new PlotTargetInfo(state);
        context.UpdateTargetInfo(evt, state);
        var effect = new PlotEventEffect
        {
            ActualId = effectId, ActualType = nameof(SpecialEffect), EventId = evt.Id,
            SourceId = PlotEffectSource.Source, TargetId = PlotEffectTarget.Target
        };
        byte flag = 0;
        effect.ApplyEffect(state, context, evt, ref flag);
    }

    private static Doodad DoodadAt(WorldInstance world, uint id, float x, uint group = 13,
        bool despawnOnCollision = false, uint idOverride = 0)
    {
        var templateId = idOverride > 0 ? idOverride : group switch { 38 => 3054u, 13 => 3060u, _ => 2310u };
        var doodad = new Doodad
        {
            ObjId = id, TemplateId = templateId, OwnerType = DoodadOwnerType.Character,
            Template = new DoodadTemplate { Id = templateId, GroupId = group, DespawnOnCollision = despawnOnCollision }
        };
        Place(world, doodad, x);
        return doodad;
    }

    private static void Place(WorldInstance world, BaseUnit unit, float x)
    {
        unit.ParentWorld = world;
        unit.IsVisible = true;
        unit.Transform.Local.SetPosition(x, 90, 10);
        unit.Region = world.GetRegionByPos(unit.Transform.World.Position);
        unit.Region.AddObject(unit);
        world.AddObject(unit);
    }

    private void Install<T>(T instance) where T : class
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _singletons.Add((field, field.GetValue(null)));
        field.SetValue(null, instance);
    }

    private static void SetField<T>(T instance, string name, object value) =>
        typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
}
