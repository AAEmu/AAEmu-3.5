using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillModifiersTests
{
    [Test]
    public async Task FlatSkillModifierRunsBeforePercentageEvenWhenItWasAddedLater()
    {
        var cache = new SkillModifiers();
        var skill = new Skill(new SkillTemplate { Id = 100 });
        cache.AddModifier(new SkillModifier
        {
            SkillId = skill.Id, SkillAttribute = SkillAttribute.AreaRadius, UnitModifierType = UnitModifierType.Percent, Value = 50
        });
        cache.AddModifier(new SkillModifier
        {
            SkillId = skill.Id, SkillAttribute = SkillAttribute.AreaRadius, UnitModifierType = UnitModifierType.Value, Value = 2
        });

        var radius = cache.ApplyModifiers(skill, SkillAttribute.AreaRadius, 8);

        await Assert.That(radius).IsEqualTo(15d);
    }

    [Test]
    public async Task ChangingAReturnedListCannotRemoveTheLiveModifier()
    {
        var cache = new SkillModifiers();
        var skill = new Skill(new SkillTemplate { Id = 100 });
        cache.AddModifier(new SkillModifier
        {
            SkillId = skill.Id, SkillAttribute = SkillAttribute.AreaRadius, UnitModifierType = UnitModifierType.Value, Value = 2
        });

        cache.GetModifiersForSkillId(skill.Id).Clear();

        await Assert.That(cache.ApplyModifiers(skill, SkillAttribute.AreaRadius, 8)).IsEqualTo(10d);
    }
}
