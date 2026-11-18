using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Physics.Static;

namespace AAEmu.UnitTests.Game.NPChar;

public sealed class WalkerCollisionScopeTests
{
    [Test]
    public async Task AppliesWalkerCollision_EnabledDuringChase_DisabledOnPatrol()
    {
        if (!WalkerCollision.Enabled)
            return;

        var npc = new Npc { Hp = 1 };
        npc.Ai = new DefaultAiCharacter { Owner = npc };

        npc.Ai.CombatApproachMoving = true;
        npc.IsInPatrol = false;
        await Assert.That(npc.AppliesWalkerCollision()).IsTrue();

        npc.IsInPatrol = true;
        await Assert.That(npc.AppliesWalkerCollision()).IsFalse();
        await Assert.That(npc.IsWalkerStuck()).IsFalse();
    }

    [Test]
    public async Task AppliesWalkerCollision_DisabledOnPlannedReturnHome()
    {
        if (!WalkerCollision.Enabled)
            return;

        var npc = new Npc { Hp = 1 };
        npc.Ai = new DefaultAiCharacter { Owner = npc };
        npc.ReturningOnPlannedRoute = true;
        npc.IsInPatrol = false;
        await Assert.That(npc.AppliesWalkerCollision()).IsFalse();
    }

    [Test]
    public async Task AppliesWalkerCollision_EnabledForRoamWhenGlobalOn()
    {
        if (!WalkerCollision.Enabled)
            return;

        var npc = new Npc { Hp = 1 };
        npc.Ai = new DefaultAiCharacter { Owner = npc };
        npc.IsInPatrol = false;
        npc.Ai.CombatApproachMoving = false;
        await Assert.That(npc.AppliesWalkerCollision()).IsTrue();
    }
}