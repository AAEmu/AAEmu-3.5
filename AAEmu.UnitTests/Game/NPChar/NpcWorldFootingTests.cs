using System.Numerics;
using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.UnitTests.Game.NPChar;

public sealed class NpcWorldFootingTests
{
    [Test]
    public async Task TryGetStreamSpawnHeight_WhenAnnouncedNearSpawn_ReturnsZ()
    {
        var npc = new Npc { Hp = 1 };
        npc.Ai = new DefaultAiCharacter { Owner = npc };
        npc.WorldStreamPosition = new Vector3(100f, 200f, 50.5f);
        var z = NpcWorldFooting.TryGetStreamSpawnHeight(npc, new Vector3(100.2f, 200.1f, 50f));
        await Assert.That(z).IsEqualTo(50.5f);
    }

    [Test]
    public async Task TryGetStreamSpawnHeight_WhenTooFar_ReturnsNull()
    {
        var npc = new Npc { Hp = 1 };
        npc.WorldStreamPosition = new Vector3(0f, 0f, 100f);
        await Assert.That(NpcWorldFooting.TryGetStreamSpawnHeight(npc, new Vector3(5f, 0f, 100f))).IsNull();
    }
}