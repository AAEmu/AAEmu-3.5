using System.Numerics;

using AAEmu.Geometry;
using AAEmu.Geometry.Abstractions;

namespace AAEmu.UnitTests.Geometry;

/// <summary>
/// The queries the phase P2–P6 work depends on: floor under a point, wall between two points, brush shapes for the
/// physics host. Built on a synthetic one-cell scene so no client data is needed.
/// </summary>
public class StaticCollisionQueryTests
{
    private sealed class MemoryFiles(Dictionary<string, byte[]> files) : IGeometryFileSource
    {
        public byte[]? TryReadBytes(string path) => files.GetValueOrDefault(path);
    }

    /// <summary>A zone with one asset placed twice: a 2 m box at (100, 100) and one at (104, 100).</summary>
    private static StaticCollision TwoBoxes()
    {
        // Placements are read from the cell's object.dat; the asset list and one brush per placement are enough.
        var data = ObjectDat.WithBoxBrushes(asset: "game/objects/test_box.cgf", [
            new Vector3(100f, 100f, 10f),
            new Vector3(104f, 100f, 10f)
        ]);
        var files = new MemoryFiles(new()
        {
            ["game/worlds/main_world/cells/000_000/client/object.dat"] = data,
            ["game/objects/test_box.cgf"] = CgfFixture.WithBoxProxy()
        });
        return StaticCollision.Load(files, [new StaticCollisionCell("main_world", 0, 0, 0f)]);
    }

    [Test]
    public async Task Load_PlacesEveryBrush()
    {
        var collision = TwoBoxes();
        await Assert.That(collision.Instances).IsEqualTo(2);
        await Assert.That(collision.Placements).IsEqualTo(2);
    }

    [Test]
    public async Task TryFloor_FindsTheBoxTop()
    {
        var collision = TwoBoxes();

        // The fixture box is 2 m across, centred on the placement, so its top is at 11 m.
        var found = collision.TryFloor(100f, 100f, above: 50f, below: 0f, out var z);

        await Assert.That(found).IsTrue();
        await Assert.That(z).IsEqualTo(11f).Within(0.01f);
    }

    [Test]
    public async Task TryFloor_FindsNothingOutsideTheBoxes()
    {
        var collision = TwoBoxes();
        await Assert.That(collision.TryFloor(200f, 200f, above: 50f, below: 0f, out _)).IsFalse();
    }

    [Test]
    public async Task Raycast_SeesAWallButFloorQueryDoesNot()
    {
        var collision = TwoBoxes();
        // A horizontal ray at z = 10 passes through both boxes.
        var from = new Vector3(90f, 100f, 10f);
        var to = new Vector3(115f, 100f, 10f);

        await Assert.That(collision.Raycast(from, to, out _, out _, minUpNormal: 0f)).IsTrue();
        // Floor-only filtering keeps upward faces, so a horizontal ray must miss.
        await Assert.That(collision.Raycast(from, to, out _, out _, minUpNormal: StaticCollision.FloorMinUpNormal)).IsFalse();
    }

    [Test]
    public async Task StaticShapes_GiveOneBoxPerBrush()
    {
        var collision = TwoBoxes();
        var shapes = collision.StaticShapes().ToList();

        await Assert.That(shapes.Count).IsEqualTo(2);
        // Geometry stays in the game's Z-up frame.
        await Assert.That(shapes[0].Origin.Z).IsEqualTo(10f).Within(0.01f);
        foreach (var shape in shapes)
        {
            await Assert.That(shape.HalfExtents.X).IsEqualTo(1f).Within(0.01f);
            await Assert.That(shape.HalfExtents.Y).IsEqualTo(1f).Within(0.01f);
            await Assert.That(shape.HalfExtents.Z).IsEqualTo(1f).Within(0.01f);
        }
    }

    [Test]
    public async Task StaticShapesWithin_TakesTheBrushesUnderTheFootprintAndItsEdge()
    {
        var collision = TwoBoxes();

        // Height 10 does not affect a footprint centred on game coordinates (100, 100).
        await Assert.That(collision.StaticShapesWithin(100f, 100f, 2f).Count()).IsEqualTo(1);
        // Reaching 103, the second box's edge (103) is inside as well.
        await Assert.That(collision.StaticShapesWithin(100f, 100f, 4f).Count()).IsEqualTo(2);
        await Assert.That(collision.StaticShapesWithin(100f, 10f, 4f).Count()).IsEqualTo(0);
        // Far from both boxes.
        await Assert.That(collision.StaticShapesWithin(200f, 200f, 10f).Count()).IsEqualTo(0);
    }

    [Test]
    public async Task AnyBoxOverlaps_AnswersForANearbyBoxOnly()
    {
        var collision = TwoBoxes();

        await Assert.That(collision.AnyBoxOverlaps(new Vector3(99f, 99f, 9f), new Vector3(101f, 101f, 11f))).IsTrue();
        await Assert.That(collision.AnyBoxOverlaps(new Vector3(300f, 300f, 9f), new Vector3(302f, 302f, 11f))).IsFalse();
    }
}
