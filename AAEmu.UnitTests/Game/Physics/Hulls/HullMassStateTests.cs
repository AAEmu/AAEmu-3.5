using AAEmu.Game.Physics.Hulls;

namespace AAEmu.UnitTests.Game.Physics.Hulls;

public class HullMassStateTests
{
    [Test]
    public async Task OverCapacity_RisesImmediatelyAndFallsAtPeakRate()
    {
        var state = new HullMassState();
        var overloaded = state.Update(0, 15000f, 5, 0.48f, 7, 300f, 0.5f);
        await Assert.That(overloaded).IsEqualTo(14700f).Within(0.01f);
        var afterLeaving = state.Update(1000, 15000f, 5, 0.48f, 5, 300f, 0.5f);
        await Assert.That(afterLeaving).IsEqualTo(7500f).Within(0.01f);
        var recovered = state.Update(2000, 15000f, 5, 0.48f, 5, 300f, 0.5f);
        await Assert.That(recovered).IsEqualTo(300f).Within(0.01f);
    }

    [Test]
    public async Task ZeroCapacity_DisablesOverloadButKeepsAttributeMass()
    {
        var state = new HullMassState();
        var mass = state.Update(0, 15000f, 0, 1f, 50, 200f, 1f);
        await Assert.That(mass).IsEqualTo(200f);
    }

    [Test]
    public async Task OverloadDecay_UsesElapsedMillisecondsAfterLongUptime()
    {
        const long now = 1_000_000_000_000;
        var state = new HullMassState();
        state.Update(now, 15000f, 1, 0.5f, 2, 0f, 1f);

        var mass = state.Update(now + 500, 15000f, 1, 0.5f, 1, 0f, 1f);

        await Assert.That(mass).IsEqualTo(3750f);
    }

    [Test]
    public async Task OverloadDecay_AccumulatesFractionsAcrossShortTicks()
    {
        var state = new HullMassState();
        state.Update(0, 1000f, 1, 0.001f, 2, 0f, 1f);
        for (var now = 15; now < 1000; now += 15)
            state.Update(now, 1000f, 1, 0.001f, 1, 0f, 1f);

        var mass = state.Update(1000, 1000f, 1, 0.001f, 1, 0f, 1f);

        await Assert.That(mass).IsEqualTo(0f);
    }
}
