using System.Reflection;
using System.Runtime.CompilerServices;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

using TUnit.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

[NotInParallel]
public class RechargeItemBuffTests
{
    private const uint RechargeSkillId = 22560;
    private const uint ReagentTemplateId = 28242;
    private static readonly DateTime s_now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
    private readonly List<(FieldInfo Field, object Previous)> _singletons = [];
    private readonly Dictionary<ulong, Item> _items = [];
    private readonly Dictionary<uint, ItemTemplate> _templates = [];
    private ItemContainer _bag;
    private ItemContainer _equipment;
    private Character _owner;

    [Before(Test)]
    public void Setup()
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

        _owner = new Character(new UnitCustomModelParams()) { Id = 20, ObjId = 30, ConditionChance = true };
        _bag = new ItemContainer(0, SlotType.Inventory, false, null);
        _equipment = new ItemContainer(0, SlotType.Equipment, false, null);
        _owner.Inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        SetField(_owner.Inventory, "<Bag>k__BackingField", _bag);
        SetField(_owner.Inventory, "<Equipment>k__BackingField", _equipment);
        SetField(_owner.Inventory, "<_itemContainers>k__BackingField", new Dictionary<SlotType, ItemContainer>
        {
            [SlotType.Inventory] = _bag,
            [SlotType.Equipment] = _equipment
        });
    }

    [After(Test)]
    public void TearDown()
    {
        foreach (var (field, previous) in _singletons)
            field.SetValue(null, previous);
    }

    [Test]
    public async Task NormalItemSkillConsumesOneNamedReagentAndRefreshesTheStoredAndClientTimes()
    {
        var reagent = MakeReagent(count: 3);
        var target = MakeGear();
        var itemUses = 0;
        _owner.Events.OnItemUse += (_, args) =>
        {
            if (args.ItemId == ReagentTemplateId)
                itemUses++;
        };

        var skill = MakeSkill();
        var before = DateTime.UtcNow;
        skill.ApplyEffects(_owner, MakeSource(reagent), _owner, MakeTarget(target), null);

        await Assert.That(skill.Cancelled).IsFalse();
        await Assert.That(reagent.Count).IsEqualTo(2);
        await Assert.That(itemUses).IsEqualTo(1);
        await Assert.That(target.ChargeCount).IsEqualTo((short)7);
        await Assert.That(target.ChargeStartTime >= before).IsTrue();
        await Assert.That(target.ChargeTime).IsEqualTo(target.ChargeStartTime);
        await Assert.That(target.IsDirty).IsTrue();
    }

    [Test]
    [Arguments("equipped")]
    [Arguments("unrelated reagent")]
    [Arguments("wrong source skill")]
    [Arguments("foreign target")]
    [Arguments("no charge buff")]
    public async Task RejectedItemSkillLeavesBothItemsUntouched(string cause)
    {
        var reagent = MakeReagent(count: 3, skillId: cause == "wrong source skill" ? 90000u : RechargeSkillId);
        var target = MakeGear(
            cause == "equipped" ? _equipment : _bag,
            reagentTemplateId: cause == "unrelated reagent" ? 35027 : (int)ReagentTemplateId,
            buffId: cause == "no charge buff" ? 0u : 7724u);
        if (cause == "foreign target")
            target.OwnerId++;

        var skill = MakeSkill();
        skill.ApplyEffects(_owner, MakeSource(reagent), _owner, MakeTarget(target), null);

        await Assert.That(skill.Cancelled).IsTrue();
        await Assert.That(reagent.Count).IsEqualTo(3);
        await Assert.That(target.ChargeStartTime).IsEqualTo(DateTime.MinValue);
        await Assert.That(target.ChargeTime).IsEqualTo(DateTime.MinValue);
    }

    [Test]
    [Arguments(ItemBindType.BindOnUnpack)]
    [Arguments(ItemBindType.BindOnPickupPack)]
    public async Task PreviouslyUnpackedGearRefreshesItsEffectiveStart(ItemBindType bindType)
    {
        var reagent = MakeReagent(count: 2);
        var target = MakeGear(bindType: bindType);
        target.SetFlag(ItemFlag.Unpacked);
        target.UnpackTime = s_now.AddDays(-40);

        Execute(MakeSkill(), MakeSource(reagent), target);

        await Assert.That(target.UnpackTime).IsEqualTo(s_now);
        await Assert.That(target.ChargeTime).IsEqualTo(s_now);
        await Assert.That(reagent.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(35027u, 32337u)]
    [Arguments(45600u, 38921u)]
    public async Task UnrestrictedChargeBuffAcceptsAnotherAuthoredReagent(uint reagentTemplateId, uint skillId)
    {
        var reagent = MakeReagent(count: 2, skillId, reagentTemplateId);
        var target = MakeGear(reagentTemplateId: 0);
        var skill = MakeSkill(skillId);

        Execute(skill, MakeSource(reagent), target);

        await Assert.That(skill.Cancelled).IsFalse();
        await Assert.That(target.ChargeTime).IsEqualTo(s_now);
        await Assert.That(reagent.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TwoConcurrentCastsCannotBothUseTheSameLastReagent()
    {
        var reagent = MakeReagent(count: 1);
        var source = MakeSource(reagent);
        var firstSkill = MakeSkill();
        var secondSkill = MakeSkill();
        var first = MakeGear();
        var second = MakeGear();

        Parallel.Invoke(() => Execute(firstSkill, source, first), () => Execute(secondSkill, source, second));

        await Assert.That(firstSkill.Cancelled != secondSkill.Cancelled).IsTrue();
        await Assert.That((first.ChargeTime == s_now) ^ (second.ChargeTime == s_now)).IsTrue();
        await Assert.That(_bag.GetItemByItemId(reagent.Id)).IsNull();
    }

    [Test]
    public async Task NativeDetailPacketCarriesSlotAndTheRawEquipmentBlob()
    {
        var item = MakeGear();
        item.ChargeCount = 7;
        item.ChargeTime = s_now;
        var packet = new SCItemDetailUpdatedPacket(item);
        var writer = packet.Write(new PacketStream());
        var reader = new PacketStream(writer.GetBytes());

        await Assert.That(packet.TypeId).IsEqualTo((ushort)0x191);
        await Assert.That(reader.ReadUInt64()).IsEqualTo(item.Id);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)SlotType.Inventory);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)item.Slot);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)ItemDetailType.Equipment);
        await Assert.That(reader.ReadByte()).IsEqualTo(item.Durability);
        await Assert.That(reader.ReadInt16()).IsEqualTo((short)7);
        await Assert.That(reader.ReadInt64()).IsEqualTo(new DateTimeOffset(s_now).ToUnixTimeSeconds());
    }

    [Test]
    public async Task StoredDetailsPreserveTheNewChargeTimeAfterLoad()
    {
        var reagent = MakeReagent(count: 2);
        var equipment = MakeGear();
        Execute(MakeSkill(), MakeSource(reagent), equipment);
        var details = new PacketStream();
        equipment.WriteDetails(details);

        var reloaded = new EquipItem();
        reloaded.ReadDetails(new PacketStream(details.GetBytes()));

        await Assert.That(details.Count).IsEqualTo(38);
        await Assert.That(reloaded.ChargeCount).IsEqualTo((short)7);
        await Assert.That(reloaded.ChargeTime).IsEqualTo(s_now);
    }

    private static Skill MakeSkill(uint skillId = RechargeSkillId)
    {
        var template = new SkillTemplate { Id = skillId, TargetType = SkillTargetType.Item };
        template.Effects.Add(new SkillEffect
        {
            ApplicationMethod = SkillEffectApplicationMethod.Target,
            Chance = 100,
            EndLevel = 100,
            ConsumeItemCount = 1,
            Template = new SpecialEffect { SpecialEffectTypeId = SpecialType.RechargeItemBuff }
        });
        return new Skill(template);
    }

    private void Execute(Skill skill, SkillItem source, Item target) =>
        new RechargeItemBuff().Execute(_owner, source, _owner, MakeTarget(target), null, skill, null, s_now, 0, 0, 0, 0);

    private SkillItem MakeSource(Item item)
    {
        var bytes = new PacketStream();
        bytes.WriteBc(_owner.ObjId).Write(item.Id).Write(item.TemplateId).Write((byte)0).Write(0u);
        var source = new SkillItem { Type = SkillCasterType.Item };
        source.Read(new PacketStream(bytes.GetBytes()));
        return source;
    }

    private static SkillCastItemTarget MakeTarget(Item item) => new() { Id = item.Id, Type = SkillCastTargetType.Item };

    private Item MakeReagent(int count, uint skillId = RechargeSkillId, uint templateId = ReagentTemplateId)
    {
        var template = new ItemTemplate { Id = templateId, UseSkillId = skillId, UseSkillAsReagent = true };
        _templates[template.Id] = template;
        return Add(_bag, new Item(0, (ulong)_items.Count + 1, template, count));
    }

    private EquipItem MakeGear(ItemContainer container = null, ItemBindType bindType = ItemBindType.BindOnPickup,
        int reagentTemplateId = (int)ReagentTemplateId, uint buffId = 7724)
    {
        var template = new ArmorTemplate
        {
            Id = 33049,
            BindType = bindType,
            RechargeBuffId = buffId,
            RechargeRestrictItemId = reagentTemplateId,
            ChargeLifetime = 43200,
            ChargeCount = 7
        };
        return (EquipItem)Add(container ?? _bag, new EquipItem((ulong)_items.Count + 1, template, 1));
    }

    private Item Add(ItemContainer container, Item item)
    {
        item.OwnerId = _owner.Id;
        item.SlotType = container.ContainerType;
        item.Slot = container.Items.Count;
        item.HoldingContainer = container;
        container.Items.Add(item);
        _items.Add(item.Id, item);
        item.IsDirty = false;
        return item;
    }

    private static void SetField<T>(T instance, string name, object value) =>
        typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private void Install<T>(T singleton) where T : class
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _singletons.Add((field, field.GetValue(null)));
        field.SetValue(null, singleton);
    }
}
