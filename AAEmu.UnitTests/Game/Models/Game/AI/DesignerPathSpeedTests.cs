using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.UnitTests.Game.Models.Game.AI;

public class DesignerPathSpeedTests
{
    [Test]
    public async Task DesignerPathSpeed_IsMultiplierOnBaseMoveSpeed()
    {
        var npc = new Npc { Hp = 1 };
        npc.Ai = new DefaultAiCharacter { Owner = npc };
        const float baseSpeed = 4.6f;

        var leg = (float)npc.Ai.GetRealMovementSpeed(baseSpeed * 0.4f);
        var wrong = (float)npc.Ai.GetRealMovementSpeed(0.4f);
        await Assert.That(leg).IsGreaterThan(wrong * 3f);
        await Assert.That(leg).IsEqualTo(1.84f).Within(0.01f);
    }
}