using System.Numerics;
using System.Reflection;

using AAEmu.Commons.IO;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.AI;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Services;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class AiPathsManagerDesignerTests
{
    private static string GameProjectDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "AAEmu.Game");
                if (Directory.Exists(Path.Combine(candidate, "Data", "Worlds", "main_world")))
                    return candidate;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("AAEmu.Game with main_world data not found.");
        }
    }

    private static void PointFileManagerAtGameData()
    {
        var field = typeof(FileManager).GetField("_appPath", BindingFlags.NonPublic | BindingFlags.Static);
        var gameDir = GameProjectDir;
        if (!gameDir.EndsWith(Path.DirectorySeparatorChar))
            gameDir += Path.DirectorySeparatorChar;
        field!.SetValue(null, gameDir);
    }

    private static string SpawnerFile => Path.Combine(GameProjectDir, "Data", "Worlds", "main_world",
        "level_design", "zone", "129", "editor", NpcSpawnersFileRules.FileName);

    private static (string pathName, int pointNo, Vector3 spawnLocal) SampleSolzreedPlacement()
    {
        var placements = NpcSpawnersFileRules.Parse(File.ReadAllText(SpawnerFile), out _);
        var placement = placements.First(p => p.Paths.Count > 0);
        var path = placement.Paths[0];
        var local = placement.Points[0].Position;
        var anchor = PathWalkerRules.AnchorPlacementPoint(placement.Points, local)?.Position
            ?? placement.Anchor.Position;
        var spawnLocal = new Vector3(anchor.X, anchor.Y, local.Z);
        return (path.PathName, path.PointNo, spawnLocal);
    }

    [Test]
    public async Task TryLoadDesignerPathPoints_SearchZones_FindsPathInZone129()
    {
        PointFileManagerAtGameData();
        var (pathName, pointNo, spawnLocal) = SampleSolzreedPlacement();

        var ok = AiPathsManager.Instance.TryLoadDesignerPathPoints("main_world", 1, pathName, spawnLocal, pointNo,
            out var points, out var speed, out var loops, out var resolvedZone, [129u]);

        await Assert.That(ok).IsTrue();
        await Assert.That(resolvedZone).IsEqualTo(129u);
        await Assert.That(points.Count).IsGreaterThan(0);
        await Assert.That(speed).IsGreaterThan(0f);
        await Assert.That(loops).IsTrue();
    }

    [Test]
    public async Task TryLoadDesignerPathPoints_ArthurZone179_Moang29()
    {
        PointFileManagerAtGameData();
        var spawnLocal = new Vector3(1211.53f, 1496.13f, 136.535f);
        var ok = AiPathsManager.Instance.TryLoadDesignerPathPoints("main_world", 179, "AIPath_179_moang_29",
            spawnLocal, 0, out var points, out var speed, out _, out var resolvedZone);

        await Assert.That(ok).IsTrue();
        await Assert.That(resolvedZone).IsEqualTo(179u);
        await Assert.That(points.Count).IsGreaterThan(15);
        await Assert.That(speed).IsEqualTo(0.4f);
        var index = ZoneAiPathIndex.Load("main_world", 179, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AIPath_179_moang_29" });
        await Assert.That(index.Get("AIPath_179_moang_29")!.Loops).IsTrue();
    }

    [Test]
    public async Task TryLoadDesignerPathPoints_NameEmbedZone_ResolvesWithoutSearch()
    {
        PointFileManagerAtGameData();
        var placements = NpcSpawnersFileRules.Parse(File.ReadAllText(SpawnerFile), out _);
        var placement = placements.First(p => p.Paths.Any(path =>
            path.PathName.StartsWith("AIPath_129_", StringComparison.OrdinalIgnoreCase)));
        var path = placement.Paths.First(p =>
            p.PathName.StartsWith("AIPath_129_", StringComparison.OrdinalIgnoreCase));
        var local = placement.Points[0].Position;
        var anchor = PathWalkerRules.AnchorPlacementPoint(placement.Points, local)?.Position
            ?? placement.Anchor.Position;
        var spawnLocal = new Vector3(anchor.X, anchor.Y, local.Z);

        var ok = AiPathsManager.Instance.TryLoadDesignerPathPoints("main_world", 1, path.PathName,
            spawnLocal, path.PointNo, out var points, out _, out _, out var resolvedZone);

        await Assert.That(ok).IsTrue();
        await Assert.That(resolvedZone).IsEqualTo(129u);
        await Assert.That(points.Count).IsGreaterThan(0);
    }
}