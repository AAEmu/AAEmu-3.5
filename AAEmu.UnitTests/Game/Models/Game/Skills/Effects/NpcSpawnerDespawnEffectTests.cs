using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Tasks.World;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

[NotInParallel]
public class NpcSpawnerDespawnEffectTests
{
    private const uint SirotheCleanupSkill = 22958;
    private const uint FireballSpawner = 119533;
    private const uint FireballTemplate = 13616;

    private readonly FieldInfo _worldManagerInstance = typeof(Singleton<WorldManager>)
        .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
    private object _previousWorldManager;
    private WorldInstance _instance;
    private WorldInstance _otherInstance;

    [Before(Test)]
    public void Setup()
    {
        var worlds = new WorldManager(null, null, null, null, null);
        var template = new WorldTemplate();
        _instance = CreateInstance(template, 1);
        _otherInstance = CreateInstance(template, 2);
        typeof(WorldManager).GetField("_worlds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(worlds, new ConcurrentDictionary<uint, WorldInstance> { [1] = _instance, [2] = _otherInstance });
        _previousWorldManager = _worldManagerInstance.GetValue(null);
        _worldManagerInstance.SetValue(null, worlds);
    }

    [After(Test)]
    public void TearDown() => _worldManagerInstance.SetValue(null, _previousWorldManager);

    [Test]
    public async Task SirotheCleanupQueuesEveryOwnedPlacementInItsOwnInstanceOnce()
    {
        var caster = AddNpc(_instance, 9, templateId: 12962);
        var first = AddNpc(_instance, 10, AddPlacement(_instance, FireballSpawner));
        var second = AddNpc(_instance, 11, AddPlacement(_instance, FireballSpawner));
        var unrelatedEvent = AddNpc(_instance, 12, AddPlacement(_instance, FireballSpawner + 1));
        var unrelatedStatic = AddNpc(_instance, 13, AddPlacement(_instance, FireballSpawner, eventPlacement: false));
        var foreign = AddNpc(_otherInstance, 10, AddPlacement(_otherInstance, FireballSpawner));
        first.Spawner.SpawnedNpcs[first.Spawner.SpawnerId].Add(foreign);
        first.Spawner.SpawnedNpcs[first.Spawner.SpawnerId].Add(unrelatedEvent);
        first.Spawner.Template.Npcs[0].MemberId = 200;

        ApplyCleanup(caster, foreign);
        var firstDeadline = first.Despawn;
        ApplyCleanup(caster, foreign);

        var pending = QueueOf(_instance, "Despawns");
        await Assert.That(pending.Count).IsEqualTo(2);
        await Assert.That(pending.Contains(first) && pending.Contains(second)).IsTrue();
        await Assert.That(pending.Contains(caster) || pending.Contains(unrelatedEvent) || pending.Contains(unrelatedStatic)).IsFalse();
        await Assert.That(first.Despawn).IsEqualTo(firstDeadline);
        await Assert.That(first.Spawner.Template.Npcs[0].MemberId).IsEqualTo(200u);
        await Assert.That(first.Spawner.RespawnTime).IsEqualTo(15);
        await Assert.That(QueueOf(_instance, "Respawns").Count).IsEqualTo(0);
        await Assert.That(QueueOf(_otherInstance, "Despawns").Count).IsEqualTo(0);
    }

    [Test]
    public async Task ExpiredLifetimeTaskCannotDespawnAnActorAgainAfterItsObjectIdIsReused()
    {
        var caster = AddNpc(_instance, 9, templateId: 12962);
        var placement = AddPlacement(_instance, FireballSpawner);
        placement.RespawnTime = 0;
        placement.DespawnTime = 0;
        var npc = AddNpc(_instance, 10, placement);
        var lifetime = new NpcSpawnerDoDespawnTask(npc);

        ApplyCleanup(caster, npc);

        // The reaper removes the NPC from the world index, then drops its queue entry.
        _instance.RemoveObject(npc);
        typeof(SpawnManager).GetMethod("RemoveDespawn", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(_instance.SpawnManager, [npc]);
        var replacement = AddNpc(_instance, npc.ObjId);
        lifetime.Execute();

        await Assert.That(QueueOf(_instance, "Despawns").Count).IsEqualTo(0);
        await Assert.That(_instance.GetNpc(replacement.ObjId)).IsSameReferenceAs(replacement);
    }

    private static void ApplyCleanup(Npc caster, Npc target)
    {
        var effect = new NpcSpawnerDespawnEffect { Id = 136, SpawnerId = FireballSpawner };
        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), target, new SkillCastUnitTarget(target.ObjId),
            new CastSkill(SirotheCleanupSkill, 1), null, null, DateTime.UtcNow);
    }

    private static WorldInstance CreateInstance(WorldTemplate template, uint id)
    {
        var world = new WorldInstance(template, 0, true, id);
        world.SpawnManager = new SpawnManager(world);
        return world;
    }

    private static NpcSpawner AddPlacement(WorldInstance world, uint spawnerId, bool eventPlacement = true)
    {
        var spawner = new NpcSpawner
        {
            Id = spawnerId,
            UnitId = FireballTemplate,
            NpcSpawnerIds = [spawnerId],
            Position = new WorldSpawnPosition()
        };
        if (!eventPlacement)
        {
            spawner.Template = new NpcSpawnerTemplate(spawnerId, FireballTemplate);
            spawner.SpawnableNpcs = spawner.Template.Npcs;
        }
        world.SpawnManager.AddNpcSpawner(spawner);
        return spawner;
    }

    private static Npc AddNpc(WorldInstance world, uint objId, NpcSpawner spawner = null, uint templateId = FireballTemplate)
    {
        var npc = new Npc { ObjId = objId, TemplateId = templateId, Hp = 1, ParentWorld = world, Spawner = spawner };
        world.AddObject(npc);
        if (spawner != null)
            spawner.SpawnedNpcs.GetOrAdd(spawner.SpawnerId, static _ => []).Add(npc);
        return npc;
    }

    private static HashSet<GameObject> QueueOf(WorldInstance world, string name) =>
        (HashSet<GameObject>)typeof(SpawnManager).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(world.SpawnManager)!;
}
