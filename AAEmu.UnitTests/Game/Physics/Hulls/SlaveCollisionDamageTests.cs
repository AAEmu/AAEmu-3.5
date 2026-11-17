using System.Globalization;

using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.World.Zones;

using Jace;

namespace AAEmu.UnitTests.Game.Physics.Hulls;

public class SlaveCollisionDamageTests
{
    [Test]
    [Arguments(0.16, 49)]
    [Arguments(0.5, 152)]
    public async Task ClipperBottomHit_UsesTheThreePointFiveFormula(double impact, int expected)
    {
        // Row 3: impact mass 17400, bottom gain 25. AAC 3.5 formula 28 has coefficient 0.000002.
        var raw = SlaveCollisionDamage.RawDamage(impact, 17400d, 25d, 1d, 1d);

        await Assert.That(SlaveCollisionDamage.RoundedDamage(raw)).IsEqualTo(expected);
    }

    [Test]
    public async Task RawDamage_MatchesTheAuthoredFormulaExpression()
    {
        var engine = new CalculationEngine(new JaceOptions { CultureInfo = CultureInfo.InvariantCulture });
        var expression = engine.Build("(0.000002 * (impact * (mass ^ 1.6))) * part_gain * equip_gain / armor_gain");
        var authored = expression(new Dictionary<string, double>
        {
            ["impact"] = 0.5d,
            ["mass"] = 45000d,
            ["part_gain"] = 0.5d,
            ["equip_gain"] = 0.01d,
            ["armor_gain"] = 30d
        });

        var raw = SlaveCollisionDamage.RawDamage(0.5d, 45000d, 0.5d, 0.01d, 30d);

        await Assert.That(raw).IsEqualTo(0.004645032657000043d).Within(0.000000001d);
        await Assert.That(raw).IsEqualTo(authored).Within(0.000000001d);
        await Assert.That(SlaveCollisionDamage.RoundedDamage(raw)).IsEqualTo(1);
    }

    [Test]
    public async Task StruckFace_UsesItsAuthoredGain()
    {
        var desc = new SlaveCollisionDamageDesc
        {
            FrontGain = 0.5f,
            SideGain = 1f,
            RearGain = 0.8f,
            BottomGain = 25f
        };

        await Assert.That(desc.GainFor(0)).IsEqualTo(0.5f);
        await Assert.That(desc.GainFor(1)).IsEqualTo(1f);
        await Assert.That(desc.GainFor(2)).IsEqualTo(0.8f);
        await Assert.That(desc.GainFor(3)).IsEqualTo(25f);
        // AAC 3.5 has no top gain; unspecified faces keep the side gain.
        await Assert.That(desc.GainFor(4)).IsEqualTo(1f);
    }

    [Test]
    [Arguments(0d)]
    [Arguments(-1d)]
    [Arguments(double.NaN)]
    [Arguments(double.PositiveInfinity)]
    public async Task InvalidImpact_DealsNoDamage(double impact)
    {
        var raw = SlaveCollisionDamage.RawDamage(impact, 45000d, 1d, 1d, 1d);

        await Assert.That(SlaveCollisionDamage.RoundedDamage(raw)).IsEqualTo(0);
    }

    [Test]
    public async Task ZeroEquipmentGain_DealsNoDamage()
    {
        var raw = SlaveCollisionDamage.RawDamage(1d, 45000d, 1d, 0d, 1d);

        await Assert.That(SlaveCollisionDamage.RoundedDamage(raw)).IsEqualTo(0);
    }

    [Test]
    public async Task ExcessiveDamage_IsCappedBeforeIntegerConversion()
    {
        await Assert.That(SlaveCollisionDamage.RoundedDamage(double.MaxValue)).IsEqualTo(int.MaxValue);
        await Assert.That(SlaveCollisionDamage.RoundedDamage(double.PositiveInfinity)).IsEqualTo(int.MaxValue);
        await Assert.That(SlaveCollisionDamage.RoundedDamage(double.NaN)).IsEqualTo(0);
    }

    [Test]
    public async Task DisabledZoneGroup_PreventsCollisionDamage()
    {
        await Assert.That(SlaveCollisionDamage.AllowsDamage(new ZoneGroup { EnablePhysicsCollisionDamage = false })).IsFalse();
        await Assert.That(SlaveCollisionDamage.AllowsDamage(new ZoneGroup { EnablePhysicsCollisionDamage = true })).IsTrue();
        await Assert.That(SlaveCollisionDamage.AllowsDamage(null)).IsTrue();
    }
}
