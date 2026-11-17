using System.Reflection;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

public class WorldTerrainHeightTests
{
    [Test]
    public async Task GetHeightMapDataInCell_OnAHighPlateau_UsesTheCellsQuantizationRange()
    {
        var template = HighPlateau();

        var height = template.Cells[0, 0].GetHeightMapDataInCell(0, 0);

        await Assert.That(height).IsEqualTo(1100f).Within(0.01f);
    }

    [Test]
    public async Task HighPlateau_HasTheSameHeightThroughTheCellAndTemplateApis()
    {
        var template = HighPlateau();

        var cellHeight = template.Cells[0, 0].GetHeight(1, 1);
        var hasRawHeight = template.TryGetRawHeightMapHeight(1, 1, out var rawHeight);
        var hasHeight = template.TryGetHeight(1f, 1f, out var height);

        await Assert.That(cellHeight).IsEqualTo(1100f).Within(0.01f);
        await Assert.That(hasRawHeight).IsTrue();
        await Assert.That(rawHeight).IsEqualTo(1100f).Within(0.01f);
        await Assert.That(hasHeight).IsTrue();
        await Assert.That(height).IsEqualTo(1100f).Within(0.01f);
    }

    [Test]
    public async Task GetHeightMapDataInCell_AtTheResolutionBoundary_ReturnsNoHeight()
    {
        var cell = HighPlateau().Cells[0, 0];

        await Assert.That(cell.GetHeightMapDataInCell(WorldManager.CELL_HMAP_RESOLUTION, 0)).IsEqualTo(0f);
        await Assert.That(cell.GetHeightMapDataInCell(0, WorldManager.CELL_HMAP_RESOLUTION)).IsEqualTo(0f);
    }

    private static WorldTemplate HighPlateau()
    {
        var template = new WorldTemplate
        {
            CellX = 1,
            CellY = 1,
            HeightMaxCoefficient = ushort.MaxValue / 1024d
        };
        var cell = new WorldCell(0, 0, template);
        var samples = new ushort[WorldManager.CELL_HMAP_RESOLUTION, WorldManager.CELL_HMAP_RESOLUTION];
        samples[0, 0] = ushort.MaxValue;
        samples[1, 0] = ushort.MaxValue;
        samples[0, 1] = ushort.MaxValue;
        samples[1, 1] = ushort.MaxValue;
        // A loaded 1100 m plateau uses the entire ushort range instead of the world's 1024 m range.
        typeof(WorldCell).GetProperty(nameof(WorldCell.HeightMap), BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(cell, samples);
        typeof(WorldCell).GetProperty(nameof(WorldCell.HeightCoefficient))!.SetValue(cell, ushort.MaxValue / 1100d);
        typeof(WorldCell).GetProperty(nameof(WorldCell.HasHeightMap))!.SetValue(cell, true);
        typeof(WorldCell).GetProperty(nameof(WorldCell.Loaded))!.SetValue(cell, true);
        template.Cells[0, 0] = cell;
        return template;
    }
}
