using System.Numerics;
using AAEmu.Game.Core.Managers.Bots.Navigation;
using OceanNavWater = AAEmu.UnitTests.Game.Core.Managers.Bots.Navigation.OceanNavWater;

namespace AAEmu.UnitTests.Game.Core.Managers.Bots.Navigation;

public class NavBlockTests
{
    [Test]
    public async Task Grid_KeepsEveryTriangleAndPutsItsBorderOnTheFourSides()
    {
        var block = new GridMesh(3, 4, 8).Block();

        await Assert.That(block.TriangleCount).IsEqualTo(128);
        await Assert.That(block.WaypointCount).IsEqualTo(0);
        for (var side = NavRules.West; side <= NavRules.North; side++)
        {
            await Assert.That(block.Seams[side].Length).IsEqualTo(8);
            await Assert.That(block.Seams[side].Sum(e => e.Hi - e.Lo)).IsEqualTo(256f).Within(0.001f);
            // No fringe in the file: nothing is said about crossing.
            await Assert.That(block.Seams[side].All(e => float.IsNaN(e.OutRadius) && float.IsNaN(e.InRadius))).IsTrue();
        }
    }

    [Test]
    public async Task Fringe_IsDroppedButWhatItSaysAboutTheSideIsKept()
    {
        var plain = new GridMesh(0, 0, 4).Block();
        var withFringe = new GridMesh(0, 0, 4) { EastFringeRadius = 0.3f }.Block();

        await Assert.That(withFringe.TriangleCount).IsEqualTo(plain.TriangleCount);
        await Assert.That(withFringe.LinkCount).IsEqualTo(plain.LinkCount);
        await Assert.That(withFringe.Seams[NavRules.East].Length).IsEqualTo(4);
        await Assert.That(withFringe.Seams[NavRules.East].All(e => e.OutRadius == 0.3f && e.InRadius == 0.3f)).IsTrue();
        await Assert.That(withFringe.Seams[NavRules.West].All(e => float.IsNaN(e.OutRadius))).IsTrue();
    }

    [Test]
    public async Task Locate_FindsTheTriangleUnderAPoint()
    {
        var mesh = new GridMesh(1, 1, 4);
        var block = mesh.Block();
        var inA = mesh.Corner(1, 1) + new Vector3(50f, 10f, 0f); // below the diagonal of cell (1, 1)
        var inB = mesh.Corner(1, 1) + new Vector3(10f, 50f, 0f);

        var a = block.Locate(inA.X, inA.Y);
        var b = block.Locate(inB.X, inB.Y);

        await Assert.That(a).IsGreaterThanOrEqualTo(0);
        await Assert.That(b).IsGreaterThanOrEqualTo(0);
        await Assert.That(a).IsNotEqualTo(b);
        await Assert.That(block.InsideDistance(a, inA.X, inA.Y)).IsGreaterThan(0f);
        await Assert.That(block.Locate(100f, 100f)).IsEqualTo(-1); // another block's ground
    }

    [Test]
    public async Task Nearest_SkipsWhatTheCallerRefusesAndStaysWithinReach()
    {
        var mesh = new GridMesh(0, 0, 16); // 16 m cells
        mesh.Blocked.Add((5, 5));
        var block = mesh.Block();
        var inside = mesh.CellCentre(5, 5);

        var t = block.Nearest(inside.X, inside.Y, 20f, n => !block.IsForbidden(n), out var point, out var distance);
        var tooFar = block.Nearest(inside.X, inside.Y, 4f, n => !block.IsForbidden(n), out _, out _);

        await Assert.That(block.IsForbidden(block.Locate(inside.X, inside.Y))).IsTrue();
        await Assert.That(t).IsGreaterThanOrEqualTo(0);
        await Assert.That(block.IsForbidden(t)).IsFalse();
        await Assert.That(distance).IsEqualTo(8f).Within(0.02f);
        await Assert.That(block.Locate(point.X, point.Y)).IsEqualTo(t);
        await Assert.That(tooFar).IsEqualTo(-1);
    }

    [Test]
    public async Task BadNodes_AreSkipped()
    {
        var data = new GridMesh(0, 0, 2).Data();
        var first = data.Nodes[0];
        data.Nodes.Add(first with { Position = first.Position + Vector3.One }); // duplicate id
        data.Nodes.Add(new NavRawNode(999, NavRules.TriangleNodeType, 0, 0, 1, 9999, Vector3.Zero)); // corner out of range
        data.Nodes.Add(new NavRawNode(998, 7, 0, 0, 1, 2, Vector3.Zero)); // a node type that is not walked

        var block = NavBlock.Build(0, 0, data, 0.1f);

        await Assert.That(block.TriangleCount).IsEqualTo(8);
        await Assert.That(block.Centre(0)).IsEqualTo(first.Position);
    }

    [Test]
    public async Task StructureGrids_AreKeptOnlyWhenAskedFor()
    {
        var mesh = new GridMesh(0, 0, 4);
        var centre = mesh.CellCentre(1, 1);
        mesh.ExtraNodes.Add(new NavRawNode(5000, NavRules.WaypointNodeType, 3, -1, -1, -1, centre with { Z = 152f }));
        mesh.ExtraNodes.Add(new NavRawNode(5001, NavRules.WaypointNodeType, 1, -1, -1, -1, centre + new Vector3(1.7f, 0f, 2f)));
        mesh.ExtraLinks.Add(new NavRawLink(mesh.NodeId(1, 1, 0), 5000, 2f, centre, 0, 0));
        mesh.ExtraLinks.Add(new NavRawLink(5000, mesh.NodeId(1, 1, 0), 2f, centre, 0, 0));
        mesh.ExtraLinks.Add(new NavRawLink(5000, 5001, 321f, centre, 0, 0));

        var without = mesh.Block();
        var with = mesh.Block(grids: true);

        await Assert.That(without.WaypointCount).IsEqualTo(0);
        await Assert.That(without.LinkCount).IsEqualTo(new GridMesh(0, 0, 4).Block().LinkCount);
        await Assert.That(with.WaypointCount).IsEqualTo(2);
        await Assert.That(with.IsWaypoint(with.TriangleCount)).IsTrue();
        await Assert.That(with.LinkCount).IsEqualTo(without.LinkCount + 3);
        await Assert.That(with.LinksOf(with.TriangleCount).Count()).IsEqualTo(2);
        await Assert.That(with.LinksOf(with.TriangleCount).All(l => l.Edge == NavBlock.NoEdge)).IsTrue();
        await Assert.That(with.NearestWaypoint(centre.X, centre.Y, 151f, 2f, 2.5f, out _)).IsEqualTo(with.TriangleCount);
        await Assert.That(with.NearestWaypoint(centre.X, centre.Y, 140f, 2f, 2.5f, out _)).IsEqualTo(-1);
    }

    [Test]
    public async Task Water_IsMeasuredAtEveryNodeAndLink()
    {
        var mesh = new GridMesh(0, 0, 4) { Height = 90f };
        var block = mesh.Block(water: OceanNavWater.Instance.For(NavTestWorlds.World()));

        await Assert.That(Enumerable.Range(0, block.NodeCount).All(n => Math.Abs(block.DepthOf(n) - 10f) < 0.001f)).IsTrue();
        await Assert.That(block.LinkDepth.All(d => Math.Abs(d - 10f) < 0.001f)).IsTrue();
    }

}
