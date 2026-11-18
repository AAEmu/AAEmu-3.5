using System.Numerics;

using AAEmu.Game.Models.Game.AI;
using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Models.Game.AI.v2.Controls;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.UnitTests.Game.Models.Game.AI;

public class PathWalkerRulesTests
{
    [Test]
    public async Task ShouldEnterFollowPath_AllowsDesignerAiPathByDefault()
    {
        var npc = new Npc { Hp = 1 };
        npc.Ai = new DefaultAiCharacter { Owner = npc };
        npc.Spawner = new NpcSpawner { FollowPath = "AIPath_179_moang_29" };
        npc.Ai.PathHandler.AiPathPoints.Add(new AiPathPoint { Position = new Vector3(1, 2, 3) });

        await Assert.That(PathWalkerRules.ShouldEnterFollowPath(npc)).IsTrue();
    }
    [Test]
    public async Task JsonPatrolSupersededWhenClientZoneIncludesPathZone()
    {
        await Assert.That(PathWalkerRules.JsonPatrolSupersededByClient("AIPath_179_moang_29",
            [129u, 179u], clientSpawnEnabled: true)).IsTrue();
        await Assert.That(PathWalkerRules.JsonPatrolSupersededByClient("AIPath_179_moang_29",
            [129u], clientSpawnEnabled: true)).IsFalse();
        await Assert.That(PathWalkerRules.JsonPatrolSupersededByClient("NuiForestkeeperArthur",
            [179u], clientSpawnEnabled: true)).IsFalse();
        await Assert.That(PathWalkerRules.JsonPatrolSupersededByClient("AIPath_179_moang_29",
            [179u], clientSpawnEnabled: false)).IsFalse();
        await Assert.That(PathWalkerRules.JsonPatrolSupersededByClient("AIPath_179_moang_29",
            [], clientSpawnEnabled: true)).IsFalse();
    }
}