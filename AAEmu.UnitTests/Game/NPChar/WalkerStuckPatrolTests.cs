using System.Reflection;

using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Physics.Static;

namespace AAEmu.UnitTests.Game.NPChar;

public sealed class WalkerStuckPatrolTests
{
    [Test]
    public async Task MovementAnimSpeed_ZeroWhenNoProgress()
    {
        await Assert.That(Npc.MovementAnimSpeedQuant(0f, blockedByWall: false)).IsEqualTo(0f);
        await Assert.That(Npc.MovementAnimSpeedQuant(0.01f, blockedByWall: true)).IsEqualTo(0f);
        await Assert.That(Npc.MovementAnimSpeedQuant(0.05f, blockedByWall: false)).IsEqualTo(4000f);
    }
    [Test]
    public async Task PatrolStuck_WhenProgressStallsAfterTail()
    {
        var npc = new Npc { Hp = 1 };
        npc.Ai = new DefaultAiCharacter { Owner = npc };
        npc.IsInPatrol = true;

        npc.RecordWalkerStep(blockedByWall: false, horizontalMeters: 0f, attemptedMeters: 0.5f);
        await Assert.That(npc.AppliesWalkerCollision()).IsFalse();
        await Assert.That(npc.IsWalkerStuck()).IsFalse();

        typeof(Npc).GetField("_walkerBlockedSinceUtc", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(npc, DateTime.UtcNow.AddMilliseconds(-(WalkerCollision.StuckEndMs + 100)));

        await Assert.That(npc.IsWalkerStuck()).IsTrue();
    }

    [Test]
    public async Task PlannedReturnStuck_WhenProgressStallsAfterTail()
    {
        var npc = new Npc { Hp = 1 };
        npc.Ai = new DefaultAiCharacter { Owner = npc };
        npc.ReturningOnPlannedRoute = true;

        npc.RecordWalkerStep(blockedByWall: false, horizontalMeters: 0f, attemptedMeters: 0.5f);
        await Assert.That(npc.AppliesWalkerCollision()).IsFalse();

        typeof(Npc).GetField("_walkerBlockedSinceUtc", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(npc, DateTime.UtcNow.AddMilliseconds(-(WalkerCollision.StuckEndMs + 100)));

        await Assert.That(npc.IsWalkerStuck()).IsTrue();
    }

    [Test]
    public async Task Chase_DoesNotUseStuckTailWhenClipOff()
    {
        var npc = new Npc { Hp = 1 };
        npc.Ai = new DefaultAiCharacter { Owner = npc };
        npc.Ai.CombatApproachMoving = true;

        npc.RecordWalkerStep(blockedByWall: true, horizontalMeters: 0f, attemptedMeters: 0.5f);
        typeof(Npc).GetField("_walkerBlockedSinceUtc", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(npc, DateTime.UtcNow.AddMilliseconds(-(WalkerCollision.StuckEndMs + 100)));

        await Assert.That(npc.IsWalkerStuck()).IsFalse();
    }
}