using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Services;

namespace AAEmu.UnitTests.Game.Services;

public class SolzreedSpawnerFileTests
{
    private static string DataRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "AAEmu.Game", "Data");
                if (Directory.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("AAEmu.Game/Data not found above test base directory.");
        }
    }
    [Test]
    public async Task Zone129_EditorNpcSpawners_ParsesHundredsOfBlocks()
    {
        var path = Path.Combine(DataRoot, "Worlds", "main_world", "level_design", "zone", "129", "editor", NpcSpawnersFileRules.FileName);
        await Assert.That(File.Exists(path)).IsTrue();

        var placements = NpcSpawnersFileRules.Parse(File.ReadAllText(path), out _);
        await Assert.That(placements.Count).IsGreaterThan(250);

        // spawnerId 484 — first block in retail fixture; Solzreed origin 9×1024, 14×1024.
        var anchor = placements.First(p => p.SpawnerId == 484).Anchor.Position;
        await Assert.That(anchor.X + 9 * 1024f).IsEqualTo(10363.92f).Within(0.1f);
        await Assert.That(anchor.Y + 14 * 1024f).IsEqualTo(16001.42f).Within(0.1f);
    }

    [Test]
    public async Task Zone129_PlacementPathNames_ResolveInNpcXml()
    {
        var spawnerFile = Path.Combine(DataRoot, "Worlds", "main_world", "level_design", "zone", "129", "editor",
            NpcSpawnersFileRules.FileName);
        var placements = NpcSpawnersFileRules.Parse(File.ReadAllText(spawnerFile), out _);
        var wanted = placements.SelectMany(p => p.Paths).Select(p => p.PathName)
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        await Assert.That(wanted.Count).IsGreaterThan(0);

        var index = ZoneAiPathIndex.Load("main_world", 129, wanted);
        await Assert.That(index.Count).IsEqualTo(wanted.Count);
        foreach (var name in wanted)
            await Assert.That(index.Get(name)).IsNotNull();
    }

    [Test]
    public async Task Zone179_PlacementPathNames_ResolveInNpcXml()
    {
        var spawnerFile = Path.Combine(DataRoot, "Worlds", "main_world", "level_design", "zone", "179", "editor",
            NpcSpawnersFileRules.FileName);
        await Assert.That(File.Exists(spawnerFile)).IsTrue();

        var placements = NpcSpawnersFileRules.Parse(File.ReadAllText(spawnerFile), out _);
        var wanted = placements.SelectMany(p => p.Paths).Select(p => p.PathName)
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        await Assert.That(wanted.Contains("AIPath_179_moang_29")).IsTrue();

        var index = ZoneAiPathIndex.Load("main_world", 179, wanted);
        await Assert.That(index.Get("AIPath_179_moang_29")).IsNotNull();
        await Assert.That(index.Count).IsEqualTo(wanted.Count);
    }
}