using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Models.Game.AI.v2.Controls;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Route;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

public class SimulationPathSpeedTests
{
    [Test]
    public async Task SyncFromAiPath_CopiesDesignerSpeedMultiplier()
    {
        var npc = new Npc { Hp = 1 };
        npc.Ai = new DefaultAiCharacter { Owner = npc };
        npc.Simulation = new Simulation(npc);
        npc.Ai.PathHandler.AiPathSpeed = 0.4f;
        npc.Ai.PathHandler.AiPathPoints.Add(new AiPathPoint { Position = new System.Numerics.Vector3(1, 2, 3) });

        npc.Simulation.SyncFromAiPath(npc.Ai, "AIPath_179_moang_29");
        await Assert.That(npc.Simulation.PathSpeedMultiplier).IsEqualTo(0.4f);
    }

}