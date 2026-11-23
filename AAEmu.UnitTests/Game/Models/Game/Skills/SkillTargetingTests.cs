using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillTargetingTests
{
    [Test]
    public async Task AreaConeUsesTheFullAuthoredSweep()
    {
        var caster = PlaceUnit(1, 100, 100);
        var forward = PlaceUnit(2, 100, 105);
        var outside = PlaceUnit(3, 104, 105);
        var behind = PlaceUnit(4, 100, 96);
        var skill = new Skill(new SkillTemplate
        {
            TargetSelection = SkillTargetSelection.Source,
            TargetAreaAngle = 60
        });

        var targets = skill.SelectAreaTargets(caster, caster, [forward, outside, behind]);

        await Assert.That(targets).IsEquivalentTo(new[] { caster, forward });
    }

    [Test]
    public async Task AreaAroundTargetIncludesTheAimedUnit()
    {
        var caster = PlaceUnit(1, 100, 100);
        var aimed = PlaceUnit(2, 100, 105);
        var neighbor = PlaceUnit(3, 101, 105);
        var skill = new Skill(new SkillTemplate { TargetSelection = SkillTargetSelection.Target });

        var targets = skill.SelectAreaTargets(caster, aimed, [neighbor]);

        await Assert.That(targets).IsEquivalentTo(new[] { aimed, neighbor });
    }

    [Test]
    public async Task GroundMarkerCannotOccupyAnAreaHitSlot()
    {
        var caster = PlaceUnit(1, 100, 100);
        var marker = PlaceUnit(uint.MaxValue, 100, 105);
        var neighbor = PlaceUnit(3, 101, 105);
        var skill = new Skill(new SkillTemplate { TargetSelection = SkillTargetSelection.Location });

        var targets = skill.SelectAreaTargets(caster, marker, [neighbor]);

        await Assert.That(targets).HasSingleItem();
        await Assert.That(targets[0]).IsSameReferenceAs(neighbor);
    }

    [Test]
    public async Task SelfTargetIgnoresMinimumRangeWhileOtherUnitsStillNeedIt()
    {
        var caster = PlaceUnit(1, 100, 100);
        var nearby = PlaceUnit(2, 100, 101);

        var self = Skill.CheckTargetRange(caster, caster, 0, 5, 10);
        var other = Skill.CheckTargetRange(caster, nearby, 1, 5, 10);

        await Assert.That(self).IsNull();
        await Assert.That(other).IsEqualTo(SkillResult.TooCloseRange);
    }

    [Test]
    public async Task AreaRadiusIncludesCasterModifiers()
    {
        var skill = new Skill(new SkillTemplate { TargetAreaRadius = 8 });
        var caster = new AreaModifierUnit();

        var radius = skill.GetAreaRadius(caster);

        await Assert.That(radius).IsEqualTo(10f);
    }

    private sealed class AreaModifierUnit : BaseUnit
    {
        public override double ApplySkillModifiers(Skill skill, SkillAttribute attribute, double baseValue) =>
            attribute == SkillAttribute.AreaRadius ? baseValue + 2 : baseValue;
    }

    private static BaseUnit PlaceUnit(uint id, float x, float y)
    {
        var unit = new BaseUnit { ObjId = id };
        unit.Transform.Local.SetPosition(x, y, 100);
        return unit;
    }
}
