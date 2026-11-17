using AAEmu.Geometry;

namespace AAEmu.UnitTests.Geometry;

public class StaticCollisionOptionsTests
{
    private static StaticCollisionOptions From(params (string Name, string Value)[] variables)
    {
        var map = variables.ToDictionary(v => v.Name, v => (string?)v.Value, StringComparer.Ordinal);
        return StaticCollisionOptions.FromEnvironment(name => map.GetValueOrDefault(name));
    }

    [Test]
    public async Task Defaults_KeepProxyWithRenderFallback()
    {
        var options = StaticCollisionOptions.FromEnvironment(_ => null);

        // Proxy-only silently dropped 44 of 549 assets in zone 129; the fallback is what keeps them colliding.
        await Assert.That(options.PhysicsSource).IsEqualTo(GeometryPhysicsSource.ProxyThenRender);
        await Assert.That(options.UndergroundFloorsEnabled).IsTrue();
        await Assert.That(options.InlandWaterEnabled).IsTrue();
    }

    [Test]
    public async Task PhysicsSource_ReadsAllThreeModes()
    {
        await Assert.That(From(("AAEMU_GEOMETRY_PHYSICS_SOURCE", "proxy")).PhysicsSource).IsEqualTo(GeometryPhysicsSource.Proxy);
        await Assert.That(From(("AAEMU_GEOMETRY_PHYSICS_SOURCE", "render")).PhysicsSource).IsEqualTo(GeometryPhysicsSource.Render);
        await Assert.That(From(("AAEMU_GEOMETRY_PHYSICS_SOURCE", "proxy-then-render")).PhysicsSource).IsEqualTo(GeometryPhysicsSource.ProxyThenRender);
    }

    [Test]
    public async Task PhysicsSource_IgnoresCaseAndSurroundingSpace()
    {
        await Assert.That(From(("AAEMU_GEOMETRY_PHYSICS_SOURCE", "  RENDER ")).PhysicsSource).IsEqualTo(GeometryPhysicsSource.Render);
    }

    [Test]
    public async Task PhysicsSource_UnknownValueFallsBackToTheDefault()
    {
        await Assert.That(From(("AAEMU_GEOMETRY_PHYSICS_SOURCE", "nonsense")).PhysicsSource).IsEqualTo(GeometryPhysicsSource.ProxyThenRender);
    }

    [Test]
    public async Task KillSwitches_AreOffOnlyForZero()
    {
        var off = From(("AAEMU_ZONE_UNDERGROUND_FLOORS", "0"), ("AAEMU_ZONE_INLAND_WATER", "0"));
        await Assert.That(off.UndergroundFloorsEnabled).IsFalse();
        await Assert.That(off.InlandWaterEnabled).IsFalse();
        await Assert.That(off.SeaWaterEnabled).IsTrue();

        await Assert.That(From(("AAEMU_ZONE_UNDERGROUND_FLOORS", "1")).UndergroundFloorsEnabled).IsTrue();
    }
}
