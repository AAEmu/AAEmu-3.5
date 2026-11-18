using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Services;

public class SolzreedArthurSpawnerTests
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

            throw new DirectoryNotFoundException("AAEmu.Game/Data not found.");
        }
    }

  [Test]
    public async Task Zone179_ClientSpawner13257_UsesMoang29DesignerPath()
    {
        var path = Path.Combine(DataRoot, "Worlds", "main_world", "level_design", "zone", "179", "editor",
            NpcSpawnersFileRules.FileName);
        var placements = NpcSpawnersFileRules.Parse(File.ReadAllText(path), out _);
        var arthur = placements.FirstOrDefault(p => p.SpawnerType == 13257);
        await Assert.That(arthur).IsNotNull();
        await Assert.That(arthur!.Paths.Count).IsGreaterThan(0);
        await Assert.That(arthur.Paths[0].PathName).IsEqualTo("AIPath_179_moang_29");
        await Assert.That(arthur.Paths[0].PointNo).IsEqualTo(0);
        var pt = arthur.Points[0].Position;
        await Assert.That(pt.X + 14 * 1024f).IsEqualTo(15547.53f).Within(0.05f);
        await Assert.That(pt.Y + 13 * 1024f).IsEqualTo(14808.13f).Within(0.05f);
    }
}