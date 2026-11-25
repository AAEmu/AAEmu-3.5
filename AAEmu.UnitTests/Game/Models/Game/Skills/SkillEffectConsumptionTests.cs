using System.Reflection;
using System.Runtime.CompilerServices;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Loots;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

[NotInParallel]
public class SkillEffectConsumptionTests
{
    private readonly List<(FieldInfo Field, object Previous)> _singletons = [];
    private readonly Dictionary<ulong, Item> _items = [];
    private readonly Dictionary<uint, ItemTemplate> _templates = [];
    private readonly ItemContainer _bag = new(0, SlotType.Inventory, false, null);
    private readonly Character _caster = new(new UnitCustomModelParams()) { Id = 29, ObjId = 44 };

    [Before(Test)]
    public void SetUp()
    {
        var items = new ItemManager(null, Mock.Of<IItemIdManager>().Object, null, null, null, null);
        SetField(items, "_allItems", _items);
        SetField(items, "_templates", _templates);
        SetField(items, "_removedItems", new List<ulong>());
        Install(items);

        var skills = new SkillManager(null, null);
        SetField(skills, "_skillReagents", new Dictionary<uint, SkillReagent>());
        SetField(skills, "_skillProducts", new Dictionary<uint, SkillProduct>());
        Install(skills);

        _caster.Inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        SetField(_caster.Inventory, "<Bag>k__BackingField", _bag);
        SetField(_caster.Inventory, "<_itemContainers>k__BackingField", new Dictionary<SlotType, ItemContainer>
        {
            [SlotType.Inventory] = _bag
        });
    }

    [After(Test)]
    public void RestoreManagers()
    {
        foreach (var (field, previous) in _singletons)
            field.SetValue(null, previous);
    }

    [Test]
    public async Task LootEffectThatConsumesItsSourceUsesOneItemFromTheStack()
    {
        _caster.ConditionChance = true;
        var source = AddItem(204, 5);
        var loot = new LootGameData();
        SetField(loot, "_lootPacks", new Dictionary<uint, LootPack>
        {
            [9000843] = new() { Loots = [new Loot()], LootsByGroupNo = [] }
        });
        Install(loot);
        var openPack = new GainLootPackItemEffect { LootPackId = 9000843, ConsumeSourceItem = true };
        var uses = 0;
        _caster.Events.OnItemUse += (_, args) =>
        {
            if (args.ItemId == source.TemplateId)
                uses++;
        };

        Cast(new SkillItem(_caster.ObjId, source.Id, source.TemplateId), MakeEffect(openPack, consumeCount: 1, consumesSource: true));

        await Assert.That(_bag.GetItemByItemId(source.Id)).IsSameReferenceAs(source);
        await Assert.That(source.Count).IsEqualTo(4);
        await Assert.That(uses).IsEqualTo(1);
    }

    [Test]
    public async Task LaterEffectDoesNotHideSourceItemCost()
    {
        var source = AddItem(201, 12);
        var effect = new RecordedEffect();
        var paying = MakeEffect(effect, consumeCount: 8, consumesSource: true);
        var extraPaying = MakeEffect(effect, consumeCount: 2, consumesSource: true);
        var secondary = MakeEffect(effect);

        Cast(new SkillItem(_caster.ObjId, source.Id, source.TemplateId), paying, extraPaying, secondary);

        await Assert.That(effect.Applications).IsEqualTo(3);
        await Assert.That(source.Count).IsEqualTo(4);
    }

    [Test]
    [Arguments(8, 2, 3)]
    [Arguments(4, 0, 4)]
    public async Task AllSelectedEffectsContributeToTheTemplateCost(int initialCount, int applications, int countLeft)
    {
        var reagent = AddItem(202, initialCount);
        var effect = new RecordedEffect();
        var first = MakeEffect(effect, reagent.TemplateId, 3);
        var second = MakeEffect(effect, reagent.TemplateId, 2);

        var skill = Cast(new SkillCasterUnit(_caster.ObjId), first, second);

        await Assert.That(effect.Applications).IsEqualTo(applications);
        await Assert.That(reagent.Count).IsEqualTo(countLeft);
        await Assert.That(skill.Cancelled).IsEqualTo(applications == 0);
    }

    [Test]
    public async Task WeightsSelectOneCostEvenWhenAnUnweightedEffectRunsLast()
    {
        var reagent = AddItem(203, 7);
        var effect = new RecordedEffect();
        var first = MakeEffect(effect, reagent.TemplateId, 3);
        var second = MakeEffect(effect, reagent.TemplateId, 3);
        first.Weight = 1;
        second.Weight = 1;

        Cast(new SkillCasterUnit(_caster.ObjId), first, second, MakeEffect(effect));

        await Assert.That(effect.Applications).IsEqualTo(2);
        await Assert.That(reagent.Count).IsEqualTo(4);
    }

    private Skill Cast(SkillCaster source, params SkillEffect[] effects)
    {
        var skill = new Skill(new SkillTemplate { Id = 23136, Effects = [..effects] });
        skill.ApplyEffects(_caster, source, _caster, new SkillCastUnitTarget(_caster.ObjId), null);
        return skill;
    }

    private static SkillEffect MakeEffect(EffectTemplate effect, uint reagent = 0, int consumeCount = 0, bool consumesSource = false) =>
        new()
        {
            ApplicationMethod = SkillEffectApplicationMethod.Target, Chance = 100, EndLevel = 99, Template = effect,
            ConsumeItemId = reagent, ConsumeItemCount = consumeCount, ConsumeSourceItem = consumesSource
        };

    private Item AddItem(uint templateId, int count)
    {
        var template = new ItemTemplate { Id = templateId };
        var item = new Item(0, templateId, template, count)
        {
            OwnerId = _caster.Id, SlotType = SlotType.Inventory, Slot = _bag.Items.Count, HoldingContainer = _bag
        };
        _bag.Items.Add(item);
        _items.Add(item.Id, item);
        _templates.Add(templateId, template);
        return item;
    }

    private static void SetField<T>(T instance, string name, object value) =>
        typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private void Install<T>(T singleton) where T : Singleton<T>
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _singletons.Add((field, field.GetValue(null)));
        field.SetValue(null, singleton);
    }

    private sealed class RecordedEffect : EffectTemplate
    {
        public int Applications { get; private set; }
        public override bool OnActionTime => false;

        public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
            CompressedGamePackets packetBuilder = null) => Applications++;
    }
}
