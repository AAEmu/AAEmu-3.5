using System.Numerics;
using System.Text;

using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

public class ZoneAiPathsTests
{
    private const string NpcXml = """
        <Objects>
          <aiPath Type="AIPath" Name="AIPath_test_square" Pos="4,0,0.1" Rec._Speed="0.4" Rec._PathType="0" Closed="0">
           <Points>
            <Point Pos="0,0,0"/>
            <Point Pos="20,0,0"/>
            <Point Pos="20,-20,0"/>
            <Point Pos="0,-20,0"/>
           </Points>
          </aiPath>
          <aiPath Type="AIPath" Name="AIPath_unused" Pos="0,0,0" Rec._PathType="1"><Points><Point Pos="1,1,0"/></Points></aiPath>
        </Objects>
        """;

    private static ZoneAiPathIndex Paths(params string[] wanted) =>
        ZoneAiPathIndex.Parse(new MemoryStream(Encoding.UTF8.GetBytes(NpcXml)),
            wanted.Length == 0 ? null : wanted.ToHashSet());

    [Test]
    public async Task DesignerPaths_ParseWantedAndLaidAtSpawn()
    {
        var index = Paths("AIPath_test_square");
        await Assert.That(index.Count).IsEqualTo(1);
        var path = index.Get("aipath_test_square");
        await Assert.That(path).IsNotNull();
        await Assert.That(path!.Loops).IsTrue();
        await Assert.That(path.Speed).IsEqualTo(0.4f);
        await Assert.That(path.Points.Count).IsEqualTo(4);

        var laid = path.LaidAt(new Vector3(177, 183, 21));
        await Assert.That(laid[0]).IsEqualTo(new Vector3(181, 183, 21));
        await Assert.That(laid[2]).IsEqualTo(new Vector3(201, 163, 21));
        await Assert.That(path.LaidAt(new Vector3(177, 183, 21), pointNo: 2).Count).IsEqualTo(2);
        await Assert.That(Paths().Count).IsEqualTo(2);
    }
}