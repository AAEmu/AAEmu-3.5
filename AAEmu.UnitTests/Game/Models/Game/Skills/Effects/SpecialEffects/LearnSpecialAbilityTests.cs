using System.Reflection;
using System.Runtime.CompilerServices;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

[NotInParallel]
public class LearnSpecialAbilityTests
{
    private const uint LearnSkillId = 33995;
    private const uint CatalystTemplateId = 42267;
    private readonly List<(FieldInfo Field, object Previous)> _singletons = [];
    private Character _owner;
    private ItemContainer _bag;
    private Dictionary<ulong, Item> _items;

    [Before(Test)]
    public void Setup()
    {
        var levels = Mock.Of<IExperienceLevelTemplateLoader>();
        levels.Load().Returns(new[]
        {
            new ExperienceLevelTemplate { Level = 1, TotalExp = 0 },
            new ExperienceLevelTemplate { Level = 2, TotalExp = 100 },
            new ExperienceLevelTemplate { Level = 3, TotalExp = 300 }
        });
        var experience = new ExperienceManager();
        experience.Load(levels.Object, 3, 3);
        Install(experience);

        _items = [];
        var items = new ItemManager(null, Mock.Of<IItemIdManager>().Object, null, null, null, null);
        SetField(items, "_allItems", _items);
        SetField(items, "_removedItems", new List<ulong>());
        Install(items);

        var skills = new SkillManager(null, null);
        SetField(skills, "_skillReagents", new Dictionary<uint, SkillReagent>());
        SetField(skills, "_skillProducts", new Dictionary<uint, SkillProduct>());
        Install(skills);

        _owner = new TestCharacter()
        {
            Id = 31, ObjId = 77, Race = Race.Dwarf, Level = 2,
            Ability1 = AbilityType.Fight, Ability2 = AbilityType.Magic, Ability3 = AbilityType.Wild
        };
        typeof(Character).GetProperty(nameof(Character.Experience))!.SetValue(_owner, 130);
        _owner.Abilities = new CharacterAbilities(_owner);
        _owner.Skills = new CharacterSkills(_owner, new SkillAuthorityGameData(), experience);
        _owner.Inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        _bag = new ItemContainer(0, SlotType.Inventory, false, null);
        SetField(_owner.Inventory, "<Bag>k__BackingField", _bag);
        SetField(_owner.Inventory, "<_itemContainers>k__BackingField", new Dictionary<SlotType, ItemContainer>
        {
            [SlotType.Inventory] = _bag
        });
    }

    [After(Test)]
    public void TearDown()
    {
        foreach (var (field, previous) in _singletons)
            field.SetValue(null, previous);
    }

    [Test]
    [Arguments(AbilityType.Trooper, AbilityType.Predator)]
    [Arguments(AbilityType.Predator, AbilityType.Trooper)]
    public async Task NormalSkillUsesTheWireChoiceAndConsumesItsItemOnlyOnce(AbilityType chosen, AbilityType other)
    {
        var gem = GiveCatalyst(2);
        var first = Learn((uint)chosen);
        var entry = _owner.Abilities.Abilities[chosen];
        await Assert.That(first.Cancelled).IsFalse();
        await Assert.That(entry.Exp).IsEqualTo(100);
        await Assert.That(_owner.Abilities.Abilities[other].Exp).IsEqualTo(0);
        await Assert.That(gem.Count).IsEqualTo(1);
        await Assert.That(_owner.Ability3).IsEqualTo(AbilityType.Wild);

        entry.Exp = 150;
        var repeat = Learn((uint)chosen);
        await Assert.That(repeat.Cancelled).IsTrue();
        await Assert.That(entry.Exp).IsEqualTo(150);
        await Assert.That(gem.Count).IsEqualTo(1);
    }

    [Test]
    public async Task MissingOrLostCatalystLeavesTheRequestedAbilityLocked()
    {
        var gem = GiveCatalyst(1);
        _bag.Items.Remove(gem);
        var request = Learn((uint)AbilityType.Predator);
        await Assert.That(request.Cancelled).IsTrue();
        await Assert.That(_owner.Abilities.Abilities[AbilityType.Predator].Exp).IsEqualTo(0);
    }

    [Test]
    [Arguments(1u)]
    [Arguments(267u)]
    [Arguments(uint.MaxValue)]
    public async Task InvalidAbilityNeverConsumesTheCatalyst(uint invalid)
    {
        var gem = GiveCatalyst(1);
        var request = Learn(invalid);
        await Assert.That(request.Cancelled).IsTrue();
        await Assert.That(gem.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DifferentConcurrentUnlocksCannotSpendTheSameLastCatalyst()
    {
        var gem = GiveCatalyst(1);
        Skill predator = null;
        Skill trooper = null;
        Parallel.Invoke(() => predator = Learn((uint)AbilityType.Predator), () => trooper = Learn((uint)AbilityType.Trooper));

        await Assert.That(predator!.Cancelled != trooper!.Cancelled).IsTrue();
        var total = _owner.Abilities.Abilities[AbilityType.Predator].Exp + _owner.Abilities.Abilities[AbilityType.Trooper].Exp;
        await Assert.That(total).IsEqualTo(100);
        await Assert.That(_bag.GetItemByItemId(gem.Id)).IsNull();
    }

    [Test]
    public async Task TagEighteenRoundTripsThroughStartedAndFiredPackets()
    {
        var request = new PacketStream([0x92, 0x0C, 0x00, 0x00, 0x00]);
        var skillObject = SkillObject.ReadFrom(request, request.ReadByte());
        await Assert.That(skillObject is SkillObjectSpecialAbility { Ability: 12 }).IsTrue();
        await Assert.That(request.LeftBytes).IsEqualTo(0);

        var skill = new Skill(MakeTemplate());
        var caster = new SkillCasterUnit(_owner.ObjId);
        var target = new SkillCastUnitTarget(_owner.ObjId);
        var started = new SCSkillStartedPacket(LearnSkillId, 0, caster, target, skill, skillObject).Write(new PacketStream());
        var fired = new SCSkillFiredPacket(LearnSkillId, 0, caster, target, skill, skillObject).Write(new PacketStream());
        await Assert.That(started.GetBytes()[14..19]).IsEquivalentTo(new byte[] { 0x92, 12, 0, 0, 0 });
        await Assert.That(fired.GetBytes()[10..15]).IsEquivalentTo(new byte[] { 0x92, 12, 0, 0, 0 });
    }

    [Test]
    public async Task LearnedSpecialAbilityCannotReplaceAnOrdinaryTree()
    {
        var current = _owner.Ability2;
        _owner.Abilities.Abilities[AbilityType.Trooper].Exp = 100;
        _owner.Abilities.Swap(current, AbilityType.Trooper);

        await Assert.That(_owner.Ability2).IsEqualTo(current);
        await Assert.That(_owner.Abilities.Abilities[AbilityType.Trooper].Order).IsEqualTo(byte.MaxValue);
    }

    [Test]
    public async Task SelectingTheThirdOrdinaryTreeDoesNotUnlockEitherSpecialAbility()
    {
        _owner.Ability3 = AbilityType.None;
        _owner.Abilities.Swap(AbilityType.None, AbilityType.Wild);
        await Assert.That(_owner.Ability3).IsEqualTo(AbilityType.Wild);
        await Assert.That(_owner.Abilities.Abilities[AbilityType.Predator].Exp).IsEqualTo(0);
        await Assert.That(_owner.Abilities.Abilities[AbilityType.Trooper].Exp).IsEqualTo(0);
    }

    [Test]
    public async Task DefaultTransformationRequiresTheMatchingRaceAndLearnedSpecialAbility()
    {
        var authority = new SkillAuthority(new SkillAuthorityGameData());
        var troop = new SkillTemplate { Id = 33985, AbilityId = AbilityType.Trooper, AbilityLevel = 2, CharRaceId = (int)Race.Dwarf };
        var predator = new SkillTemplate { Id = 33984, AbilityId = AbilityType.Predator, AbilityLevel = 2, CharRaceId = (int)Race.Warborn };
        var source = new SkillCasterUnit(_owner.ObjId);
        var target = new SkillCastUnitTarget(_owner.ObjId);

        await Assert.That(authority.Check(_owner, source, target, troop, isDefaultSkill: true)).IsEqualTo(SkillResult.InvalidSkill);
        _owner.Abilities.Abilities[AbilityType.Predator].Exp = 100;
        _owner.Abilities.Abilities[AbilityType.Trooper].Exp = 100;
        await Assert.That(authority.Check(_owner, source, target, troop, isDefaultSkill: true)).IsEqualTo(SkillResult.Success);
        await Assert.That(authority.Check(_owner, source, target, predator, isDefaultSkill: true)).IsEqualTo(SkillResult.InvalidSkill);

        troop.AbilityLevel = 3;
        await Assert.That(authority.Check(_owner, source, target, troop, isDefaultSkill: true)).IsEqualTo(SkillResult.InvalidSkill);
    }

    [Test]
    public async Task FormSkillsAndPassivesCannotBecomePermanentSkills()
    {
        var template = new SkillTemplate { Id = 34491, AbilityId = AbilityType.Trooper, AbilityLevel = 30 };
        var passive = new PassiveBuffTemplate { Id = 293, AbilityId = AbilityType.Trooper, Level = 40 };
        SetField(SkillManager.Instance, "_skills", new Dictionary<uint, SkillTemplate> { [template.Id] = template });
        SetField(SkillManager.Instance, "_passiveBuffs", new Dictionary<uint, PassiveBuffTemplate> { [passive.Id] = passive });
        _owner.Ability3 = AbilityType.Trooper;
        _owner.Abilities.Abilities[AbilityType.Trooper].Exp = 100;

        _owner.Skills.AddSkill(template.Id);
        _owner.Skills.AddSkill(template, 1, false);
        _owner.Skills.AddBuff(passive.Id);

        await Assert.That(_owner.Skills.IsLearned(template.Id)).IsFalse();
        await Assert.That(_owner.Skills.PassiveBuffs.ContainsKey(passive.Id)).IsFalse();
    }

    [Test]
    public async Task FormGrantsOnlyPassivesAtTheUnlockedAbilityLevel()
    {
        PrepareForms();
        var form = EnterForm(20682);

        await Assert.That(_owner.Buffs.CheckBuff(18363)).IsTrue();
        await Assert.That(_owner.Buffs.CheckBuff(18364)).IsFalse();
        await Assert.That(_owner.Skills.PassiveBuffs.Count).IsEqualTo(0);

        form.Exit();
        await Assert.That(_owner.Buffs.CheckBuff(18363)).IsFalse();
    }

    [Test]
    public async Task PassiveRemainsWhileAnotherAuthoredFormStillGrantsIt()
    {
        PrepareForms();
        var first = EnterForm(20682);
        var second = EnterForm(17212);

        first.Exit();
        await Assert.That(_owner.Buffs.CheckBuff(18363)).IsTrue();
        second.Exit();
        await Assert.That(_owner.Buffs.CheckBuff(18363)).IsFalse();
    }

    [Test]
    public async Task ActivationPacketCarriesTheAbilityId()
    {
        var packet = new SCSpecialAbilityActivedPacket((byte)AbilityType.Trooper);
        var reader = new PacketStream(packet.Write(new PacketStream()).GetBytes());
        await Assert.That(packet.TypeId).IsEqualTo((ushort)0x179);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)AbilityType.Trooper);
        await Assert.That(reader.LeftBytes).IsEqualTo(0);
    }

    private Item GiveCatalyst(int count)
    {
        var item = new Item(0, 1, new ItemTemplate { Id = CatalystTemplateId }, count)
        {
            SlotType = SlotType.Inventory, Slot = 0, HoldingContainer = _bag, OwnerId = _owner.Id
        };
        _bag.Items.Add(item);
        _items.Add(item.Id, item);
        return item;
    }

    private void PrepareForms()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE buff_passive_buffs (id INTEGER, buff_id INTEGER, passive_buff_id INTEGER);
            INSERT INTO buff_passive_buffs VALUES (5, 17212, 315), (21, 20682, 315), (22, 20682, 316);
            """;
        command.ExecuteNonQuery();
        var data = new FormPassiveGameData();
        data.Load(connection, connection);
        Install(data);

        var levels = new ExperienceLevelTemplate[55];
        for (var index = 0; index < levels.Length; index++)
            levels[index] = new ExperienceLevelTemplate { Level = (byte)(index + 1), TotalExp = 100 * index };
        var loader = Mock.Of<IExperienceLevelTemplateLoader>();
        loader.Load().Returns(levels);
        ExperienceManager.Instance.Load(loader.Object, 55, 55);
        _owner.Race = Race.Warborn;
        _owner.Level = 55;
        _owner.Abilities.Abilities[AbilityType.Predator].Exp = levels[39].TotalExp;

        var skills = SkillManager.Instance;
        SetField(skills, "_passiveBuffs", new Dictionary<uint, PassiveBuffTemplate>
        {
            [315] = new() { Id = 315, BuffId = 18363, AbilityId = AbilityType.Predator, Level = 40 },
            [316] = new() { Id = 316, BuffId = 18364, AbilityId = AbilityType.Predator, Level = 45 }
        });
        SetField(skills, "_buffs", new Dictionary<uint, BuffTemplate>
        {
            [18363] = new() { Id = 18363 }, [18364] = new() { Id = 18364 },
            [17212] = new() { Id = 17212 }, [20682] = new() { Id = 20682 }
        });
        SetField(skills, "_buffTags", new Dictionary<uint, List<uint>>());
        SetField(skills, "_buffTriggers", new Dictionary<uint, List<BuffTriggerTemplate>>());
        SetField(skills, "_skillModifiers", new Dictionary<uint, List<SkillModifier>>());
        SetField(skills, "_combatBuffs", new Dictionary<uint, List<CombatBuffTemplate>>());
        var buffs = new BuffGameData();
        SetField(buffs, "_buffModifiers", new Dictionary<uint, List<BuffModifier>>());
        Install(buffs);
    }

    private Buff EnterForm(uint id)
    {
        var form = new Buff(_owner, _owner, new SkillCasterUnit(_owner.ObjId), SkillManager.Instance.GetBuffTemplate(id), null, DateTime.UtcNow);
        _owner.Buffs.AddBuff(form);
        return form;
    }

    private Skill Learn(uint ability)
    {
        var skill = new Skill(MakeTemplate());
        skill.ApplyEffects(_owner, new SkillCasterUnit(_owner.ObjId), _owner, new SkillCastUnitTarget(_owner.ObjId),
            new SkillObjectSpecialAbility { Ability = ability });
        return skill;
    }

    private static SkillTemplate MakeTemplate() => new()
    {
        Id = LearnSkillId,
        TargetType = SkillTargetType.Self,
        Effects =
        [
            new SkillEffect
            {
                ApplicationMethod = SkillEffectApplicationMethod.Target, Chance = 100, EndLevel = 99,
                ConsumeSourceItem = true, ConsumeItemId = CatalystTemplateId, ConsumeItemCount = 1,
                Template = new SpecialEffect { SpecialEffectTypeId = SpecialType.LearnSpecialAbility }
            }
        ]
    };

    private static void SetField<T>(T instance, string name, object value) =>
        typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private void Install<T>(T singleton) where T : class
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _singletons.Add((field, field.GetValue(null)));
        field.SetValue(null, singleton);
    }

    private sealed class TestCharacter : Character
    {
        public TestCharacter() : base(new UnitCustomModelParams())
        {
        }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
        }
    }
}
