using AAEmu.Geometry;

namespace AAEmu.UnitTests.Geometry;

/// <summary>
/// Plan P3: the water stance thresholds must match the ones fitted on the native traces, so a missed rule shows up
/// here rather than in a mismatch against the dedicate.
/// </summary>
public class StanceRulesTests
{
    [Test]
    public async Task Thresholds_MatchTheNativeFit()
    {
        // Native fit: swim from 0.3 m under the surface, settle on the bed, swim on the bed from 1.25 m down.
        float[] thresholds = [StanceRules.MinDepthMetres, StanceRules.OffBedMetres, StanceRules.BedDepthMetres];
        await Assert.That(thresholds[0]).IsEqualTo(0.3f);
        await Assert.That(thresholds[1]).IsEqualTo(0.3f);
        await Assert.That(thresholds[2]).IsEqualTo(1.25f);
    }

    [Test]
    public async Task AboveTheSurface_LeavesTheStanceAlone()
    {
        await Assert.That(StanceRules.Resolve(depthUnderSurface: -1f, heightAboveFloor: 10f)).IsEqualTo(WaterStance.Unchanged);
        await Assert.That(StanceRules.Resolve(depthUnderSurface: 0.1f, heightAboveFloor: 10f)).IsEqualTo(WaterStance.Unchanged);
    }

    [Test]
    public async Task DeepInTheColumn_Swims()
    {
        // Between the two thresholds, well off the bed.
        await Assert.That(StanceRules.Resolve(depthUnderSurface: 0.6f, heightAboveFloor: 5f)).IsEqualTo(WaterStance.Swim);
        // At or past the bed depth: swimming even sitting on the bed (the native's small fish).
        await Assert.That(StanceRules.Resolve(depthUnderSurface: 1.25f, heightAboveFloor: 0f)).IsEqualTo(WaterStance.Swim);
        await Assert.That(StanceRules.Resolve(depthUnderSurface: 3f, heightAboveFloor: 0f)).IsEqualTo(WaterStance.Swim);
    }

    [Test]
    public async Task OnTheBedInShallowWater_Settles()
    {
        await Assert.That(StanceRules.Resolve(depthUnderSurface: 0.5f, heightAboveFloor: 0.1f)).IsEqualTo(WaterStance.Settle);
    }

    [Test]
    public async Task JustOffTheBedInShallowWater_Swims()
    {
        await Assert.That(StanceRules.Resolve(depthUnderSurface: 0.5f, heightAboveFloor: 0.4f)).IsEqualTo(WaterStance.Swim);
    }

    [Test]
    public async Task UnknownFloor_DoesNotSettle()
    {
        await Assert.That(StanceRules.Resolve(depthUnderSurface: 0.5f, heightAboveFloor: 0f, floorKnown: false))
            .IsEqualTo(WaterStance.Swim);
    }

    [Test]
    public async Task SurfaceShorthand_UsesTheSameRules()
    {
        // Surface at 100, unit at 99.4 (0.6 under it), floor at 95.
        await Assert.That(StanceRules.Resolve(surfaceZ: 100f, unitZ: 99.4f, floorZ: 95f)).IsEqualTo(WaterStance.Swim);
        // Unit sitting 0.1 above the bed, 0.5 under the surface.
        await Assert.That(StanceRules.Resolve(surfaceZ: 100f, unitZ: 99.5f, floorZ: 99.4f)).IsEqualTo(WaterStance.Settle);
    }
}
