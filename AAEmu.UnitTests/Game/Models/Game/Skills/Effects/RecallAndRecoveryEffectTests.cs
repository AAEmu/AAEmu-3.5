using System.Numerics;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

[NotInParallel]
public sealed class RecallAndRecoveryEffectTests
{
    [Test]
    public async Task ResurrectionEffectUsesTheCharactersFactionBindPoint()
    {
        var character = new Character(null)
        {
            ReturnDistrictId = 32,
            Faction = new SystemFaction { Id = FactionsEnum.Pirate, MotherId = FactionsEnum.NuiaAlliance }
        };
        var portal = new Portal { Id = 31, ZoneId = 7 };
        var manager = Mock.Of<IPortalManager>();
        manager.GetDistrictReturnPoint(32, FactionsEnum.Pirate).Returns(0u);
        manager.GetDistrictReturnPoint(32, FactionsEnum.NuiaAlliance).Returns(31u);
        manager.GetRespawnById(31).Returns(portal);

        await Assert.That(MoveToRezPointEffect.ResolveReturnPoint(character, manager.Object)).IsEqualTo(portal);
    }

    [Test]
    public async Task HearthEmberRestoresItsBindingWithoutWritingItToItemPackets()
    {
        var template = new MemoryEmberTemplate { Id = 53790 };
        var house = new House { Id = 91, PlaceDate = new DateTime(2026, 11, 22, 4, 0, 0, DateTimeKind.Utc) };
        var ember = new MemoryEmber(1, template, 1);
        var point = new Vector3(24f, 30f, 16.5f);
        ember.BindToHouse(house, point, 0.75f);
        var persistence = new PacketStream();
        ember.WriteBinding(persistence);
        persistence.Rollback();

        var loaded = new MemoryEmber(1, template, 1);
        loaded.ReadBinding(persistence);
        var itemDetails = new PacketStream();
        loaded.WriteDetails(itemDetails);

        await Assert.That(loaded.MatchesHouse(house)).IsTrue();
        await Assert.That(loaded.ReturnPosition).IsEqualTo(point);
        await Assert.That(loaded.ReturnRotation).IsEqualTo(0.75f);
        await Assert.That(itemDetails.Count).IsEqualTo(0);
        house.PlaceDate = house.PlaceDate.AddMinutes(1);
        await Assert.That(loaded.MatchesHouse(house)).IsFalse();
    }

    [Test]
    public async Task HouseOnlyRecallRejectsDistrictAndUnownedPortalIds()
    {
        var character = new Character(null) { Id = 42 };
        character.Portals = new CharacterPortals(character);
        var own = new Portal { Id = 1, Owner = 42 };
        var district = new Portal { Id = 2 };
        character.Portals.PrivatePortals[1] = own;
        character.Portals.DistrictPortals[2] = district;
        character.Portals.PrivatePortals[3] = new Portal { Id = 3, Owner = 50 };
        var effect = new MoveToLocationEffect { OwnHouseOnly = true };

        await Assert.That(effect.ResolveBookDestination(character, new SkillObjectUnk2 { Id = 1 })).IsEqualTo(own);
        await Assert.That(effect.ResolveBookDestination(character, new SkillObjectUnk2 { Id = 2 })).IsNull();
        await Assert.That(effect.ResolveBookDestination(character, new SkillObjectUnk2 { Id = 3 })).IsNull();
        await Assert.That(effect.ResolveBookDestination(character, new SkillObjectUnk2 { Id = 4 })).IsNull();
    }

    [Test]
    public async Task RecoverExpReturnsOnlyTheTrackedExperienceOnce()
    {
        var singleton = typeof(Singleton<ExperienceManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = singleton.GetValue(null);
        var experience = new ExperienceManager();
        var loader = Mock.Of<IExperienceLevelTemplateLoader>();
        loader.Load().Returns(new[]
        {
            new ExperienceLevelTemplate { Level = 1, TotalExp = 0 },
            new ExperienceLevelTemplate { Level = 2, TotalExp = 100 },
            new ExperienceLevelTemplate { Level = 3, TotalExp = 1_000 }
        });
        experience.Load(loader.Object, 3, 3);
        singleton.SetValue(null, experience);
        try
        {
            var character = new Character(null) { Level = 2, RecoverableExp = 35 };
            typeof(Character).GetProperty(nameof(Character.Experience))!.SetValue(character, 150);
            var effect = new RecoverExpEffect { Penaltied = true };

            effect.Apply(character, null, character, null, null, null, null, DateTime.UtcNow);
            effect.Apply(character, null, character, null, null, null, null, DateTime.UtcNow);

            await Assert.That(character.Experience).IsEqualTo(185);
            await Assert.That(character.RecoverableExp).IsEqualTo(0);
        }
        finally
        {
            singleton.SetValue(null, previous);
        }
    }

    [Test]
    public async Task PriestPrayerWithoutASetupPriestPreservesThePool()
    {
        var character = new Character(null) { Level = 2, RecoverableExp = 35 };
        var effect = new RecoverExpEffect { NeedPriest = true, NeedLaborPower = true };

        effect.Apply(character, null, character, null, null, null, null, DateTime.UtcNow);

        await Assert.That(character.Experience).IsEqualTo(0);
        await Assert.That(character.RecoverableExp).IsEqualTo(35);
    }

    [Test]
    public async Task ScrollWithoutRecoverableExperienceCancelsConsumption()
    {
        var character = new Character(null) { Level = 2, ConditionChance = true };
        var skill = new Skill(new SkillTemplate { Id = 26611 });
        var effect = new RecoverExpEffect { Penaltied = true };

        effect.Apply(character, new SkillItem(), character, null, null, new EffectSource(skill), null, DateTime.UtcNow);

        await Assert.That(skill.Cancelled).IsTrue();
        await Assert.That(character.ConditionChance).IsFalse();
    }
}
