using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class ClientSpawnerRegistrationTests
{
    [Test]
    public async Task PrebuiltClientSpawner_RegistersInActiveSpawnerMap_NotEventOnly()
    {
        var template = new WorldTemplate { Name = "main_world", Id = 1 };
        var world = new WorldInstance(template, 0, true, 1);
        var spawnManager = new SpawnManager(world);
        var npcLine = new NpcSpawnerNpc(13257, 11999);
        var spawnerTemplate = new NpcSpawnerTemplate(13257) { Npcs = [npcLine] };
        var spawner = new NpcSpawner
        {
            SpawnerId = 13257,
            UnitId = 11999,
            Template = spawnerTemplate,
            NpcSpawnerIds = [13257],
            FollowPath = "AIPath_179_moang_29",
            PlacementSpawnLocal = new System.Numerics.Vector3(1, 2, 3),
            PlacementAnchorLocal = new System.Numerics.Vector3(4, 5, 6)
        };
        spawner.InitializeSpawnableNpcs(spawnerTemplate);

        spawnManager.AddNpcSpawner(spawner);

        var registered = spawnManager.GetAllSpawners().Values.SelectMany(list => list)
            .Any(s => s.SpawnerId == 13257 && s.FollowPath == "AIPath_179_moang_29");
        await Assert.That(registered).IsTrue();
    }
}