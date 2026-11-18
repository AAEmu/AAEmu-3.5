using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Services;

namespace AAEmu.UnitTests.Game.Services;

public class ZoneNpcSpawnOrchestratorTests
{
    [Test]
    public async Task PopulateAtLoad_QueuesOneSlotPerMaxPopulation_SkipsInactive()
    {
        var template = new WorldTemplate { Name = "main_world", Id = 1 };
        var world = new WorldInstance(template, 0, true, 1);
        var orchestrator = new ZoneNpcSpawnOrchestrator(world);

        var active = BuildSpawner(spawnerId: 100, maxPopulation: 3, activationState: true);
        var inactive = BuildSpawner(spawnerId: 200, maxPopulation: 5, activationState: false);

        orchestrator.RegisterPlacement(active);
        orchestrator.RegisterPlacement(inactive);
        orchestrator.PopulateAtLoad();

        await Assert.That(orchestrator.PlacementCount).IsEqualTo(2);
        await Assert.That(orchestrator.QueuedCount).IsEqualTo(3);
        await Assert.That(active.ZonePopulationManaged).IsTrue();
        await Assert.That(orchestrator.OwnsPlacement(100)).IsTrue();
    }

    [Test]
    public async Task EnqueueMinPopulationRespawn_QueuesOneSlot_WhenLiveBelowMin_AndCorpseStillListed()
    {
        var template = new WorldTemplate { Name = "main_world", Id = 1 };
        var world = new WorldInstance(template, 0, true, 1);
        var orchestrator = new ZoneNpcSpawnOrchestrator(world);
        var spawner = BuildSpawner(spawnerId: 400, maxPopulation: 4, minPopulation: 4, activationState: true);
        orchestrator.RegisterPlacement(spawner);
        orchestrator.PopulateAtLoad();
        await Assert.That(orchestrator.QueuedCount).IsEqualTo(4);

        spawner.SpawnedNpcs[400] =
        [
            new Npc { TemplateId = 11999, Hp = 0 },
            new Npc { TemplateId = 11999, Hp = 100 },
            new Npc { TemplateId = 11999, Hp = 100 },
            new Npc { TemplateId = 11999, Hp = 100 },
        ];

        orchestrator.EnqueueMinPopulationRespawn(spawner);
        await Assert.That(orchestrator.QueuedCount).IsEqualTo(5);
    }

    [Test]
    public async Task EnqueueRespawn_AddsSlotWhenBelowMaxPopulation()
    {
        var template = new WorldTemplate { Name = "main_world", Id = 1 };
        var world = new WorldInstance(template, 0, true, 1);
        var orchestrator = new ZoneNpcSpawnOrchestrator(world);
        var spawner = BuildSpawner(spawnerId: 300, maxPopulation: 2, activationState: true);
        orchestrator.RegisterPlacement(spawner);
        orchestrator.PopulateAtLoad();
        await Assert.That(orchestrator.QueuedCount).IsEqualTo(2);

        lock (spawner.SpawnedNpcs)
        {
            spawner.SpawnedNpcs[300] = [new Npc { TemplateId = 11999 }];
        }

        orchestrator.EnqueueRespawn(spawner);
        await Assert.That(orchestrator.QueuedCount).IsGreaterThan(2);
    }

    [Test]
    public async Task EnqueueRespawn_DelayedSlot_WaitsUntilNotBeforeUtc()
    {
        var template = new WorldTemplate { Name = "main_world", Id = 1 };
        var world = new WorldInstance(template, 0, true, 1);
        var orchestrator = new ZoneNpcSpawnOrchestrator(world);
        var spawner = BuildSpawner(spawnerId: 500, maxPopulation: 2, activationState: true);
        spawner.SpawnedNpcs[500] = [];
        orchestrator.RegisterPlacement(spawner);
        orchestrator.PopulateAtLoad();
        var initialQueued = orchestrator.QueuedCount;

        orchestrator.EnqueueRespawn(spawner, TimeSpan.FromHours(1));
        await Assert.That(orchestrator.QueuedCount).IsGreaterThan(initialQueued);

        var ticks = 0;
        while (ticks++ < 5)
            orchestrator.Tick();

        await Assert.That(orchestrator.QueuedCount).IsGreaterThan(initialQueued);
    }

    private static NpcSpawner BuildSpawner(uint spawnerId, int maxPopulation, bool activationState, int minPopulation = 1)
    {
        var npcLine = new NpcSpawnerNpc(spawnerId, 11999);
        var spawnerTemplate = new NpcSpawnerTemplate(spawnerId)
        {
            MaxPopulation = (uint)maxPopulation,
            MinPopulation = (uint)minPopulation,
            ActivationState = activationState,
            Npcs = [npcLine]
        };
        var spawner = new NpcSpawner
        {
            SpawnerId = spawnerId,
            UnitId = 11999,
            Template = spawnerTemplate,
            NpcSpawnerIds = [spawnerId]
        };
        spawner.InitializeSpawnableNpcs(spawnerTemplate);
        return spawner;
    }
}