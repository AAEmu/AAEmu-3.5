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
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

[NotInParallel]
public class RevertItemLookTests
{
    private const uint RevertSkillId = 27476;
    private const uint RevertReagentId = 35117;
    private const uint OriginalDye = 26769;
    private const uint CustomDye = 28421;
    private const uint GliderImage = 0x12345678;
    private readonly List<(FieldInfo Field, object Previous)> _singletons = [];
    private readonly Dictionary<ulong, Item> _items = [];
    private readonly Dictionary<uint, ItemTemplate> _templates = [];
    private Character _owner;
    private ItemContainer _bag;

    [Before(Test)]
    public void Setup()
    {
        var manager = new ItemManager(null, Mock.Of<IItemIdManager>().Object, null, null, null, null);
        SetField(manager, "_allItems", _items);
        SetField(manager, "_removedItems", new List<ulong>());
        SetField(manager, "_templates", _templates);
        Install(manager);

        var skills = new SkillManager(null, null);
        SetField(skills, "_skillReagents", new Dictionary<uint, SkillReagent>());
        SetField(skills, "_skillProducts", new Dictionary<uint, SkillProduct>());
        Install(skills);

        _owner = new Character(new UnitCustomModelParams()) { Id = 29, ObjId = 44, ConditionChance = true };
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
    public async Task ItemSkillRestoresTheOriginalLookAndConsumesOneCatalyst()
    {
        var reagent = MakeReagent();
        var gear = MakeGear();
        var used = 0;
        _owner.Events.OnItemUse += (_, args) =>
        {
            if (args.ItemId == RevertReagentId)
                used++;
        };

        var skill = Cast(reagent, gear);

        await Assert.That(skill.Cancelled).IsFalse();
        await Assert.That(gear.ImageItemTemplateId).IsEqualTo(0u);
        await Assert.That(gear.DyeItemId).IsEqualTo(OriginalDye);
        await Assert.That(gear.UccId).IsEqualTo(0ul);
        await Assert.That(gear.ItemFlags).IsEqualTo(ItemFlag.SoulBound);
        await Assert.That(gear.GemIds[4]).IsEqualTo(123u);
        await Assert.That(gear.Durability).IsEqualTo((byte)23);
        await Assert.That(gear.IsDirty).IsTrue();
        await Assert.That(reagent.Count).IsEqualTo(1);
        await Assert.That(used).IsEqualTo(1);
    }

    [Test]
    [Arguments("foreign target")]
    [Arguments("foreign catalyst")]
    [Arguments("target equipped")]
    [Arguments("moved catalyst")]
    [Arguments("secured target")]
    [Arguments("original appearance")]
    [Arguments("no image")]
    public async Task InvalidTargetOrCatalystDoesNotConsumeOrChangeTheItem(string reason)
    {
        var reagent = MakeReagent();
        var gear = MakeGear();
        switch (reason)
        {
            case "foreign target": gear.OwnerId++; break;
            case "foreign catalyst": reagent.OwnerId++; break;
            case "target equipped": gear.SlotType = SlotType.Equipment; break;
            case "moved catalyst": reagent.HoldingContainer = null; break;
            case "secured target": gear.SetFlag(ItemFlag.Secure); break;
            case "original appearance": gear.ImageItemTemplateId = gear.TemplateId; break;
            case "no image": gear.ImageItemTemplateId = 0; break;
        }
        var before = gear.ImageItemTemplateId;

        var skill = Cast(reagent, gear);

        await Assert.That(skill.Cancelled).IsTrue();
        await Assert.That(gear.ImageItemTemplateId).IsEqualTo(before);
        await Assert.That(gear.DyeItemId).IsEqualTo(CustomDye);
        await Assert.That(gear.UccId).IsEqualTo(30001ul);
        await Assert.That(reagent.Count).IsEqualTo(2);
    }

    [Test]
    public async Task DuplicateConcurrentRestorationConsumesOnlyOneCatalyst()
    {
        var reagent = MakeReagent();
        var gear = MakeGear();
        Skill first = null;
        Skill second = null;
        Parallel.Invoke(() => first = Cast(reagent, gear), () => second = Cast(reagent, gear));

        await Assert.That(first!.Cancelled != second!.Cancelled).IsTrue();
        await Assert.That(reagent.Count).IsEqualTo(1);
        await Assert.That(gear.ImageItemTemplateId).IsEqualTo(0u);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task LegacyGliderWithoutDetailsWritesTheOriginalAppearance(bool nullDetails)
    {
        var glider = MakeBackpack(BackpackType.Glider);
        ItemManager.ReadUserItemDetails(glider, nullDetails ? DBNull.Value : Array.Empty<byte>());

        var body = ItemManager.WriteUserItemDetails(glider);
        var update = ReadDetailUpdate(glider);
        await Assert.That(update.ReadByte()).IsEqualTo((byte)9);
        await Assert.That(update.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(update.LeftBytes).IsEqualTo(0);
        await Assert.That(body.GetBytes().SequenceEqual(new byte[] { 0, 0, 0, 0 })).IsTrue();
    }

    [Test]
    public async Task ImportedGliderAppearanceSurvivesSavingAndTheFullItemPacket()
    {
        var glider = MakeBackpack(BackpackType.Glider);
        byte[] persisted = [0x78, 0x56, 0x34, 0x12];
        ItemManager.ReadUserItemDetails(glider, persisted);
        glider.LifespanMins = 42;

        var body = ItemManager.WriteUserItemDetails(glider);
        var fullItem = glider.Write(new PacketStream());
        var reader = new PacketStream(fullItem.GetBytes());
        reader.ReadBytes(18);
        var restored = new Backpack();
        restored.Read(new PacketStream(fullItem.GetBytes()));

        await Assert.That(body.GetBytes().SequenceEqual(persisted)).IsTrue();
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)9);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(GliderImage);
        await Assert.That(restored.ImageItemTemplateId).IsEqualTo(GliderImage);
        await Assert.That(restored.LifespanMins).IsEqualTo(42);
    }

    [Test]
    public async Task UnknownGliderDetailsArePreservedInStorageAndOmittedFromClientUpdates()
    {
        var glider = MakeBackpack(BackpackType.Glider);
        byte[] unrecognized = [0x09, 0x78, 0x56, 0x34, 0x12];
        ItemManager.ReadUserItemDetails(glider, unrecognized);

        var stored = ItemManager.WriteUserItemDetails(glider);
        var update = ReadDetailUpdate(glider);

        await Assert.That(stored.GetBytes().SequenceEqual(unrecognized)).IsTrue();
        await Assert.That(update.ReadByte()).IsEqualTo((byte)9);
        await Assert.That(update.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(update.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task GliderReversionInTheBagClearsAppearanceAndConsumesOnlyOneCatalyst()
    {
        var reagent = MakeReagent();
        var glider = MakeBackpack(BackpackType.Glider, GliderImage);
        glider.SetFlag(ItemFlag.SoulBound);
        glider.UccId = 30001;
        glider.IsDirty = false;
        Skill first = null;
        Skill second = null;

        Parallel.Invoke(() => first = Cast(reagent, glider), () => second = Cast(reagent, glider));

        var update = ReadDetailUpdate(glider);
        await Assert.That(first!.Cancelled != second!.Cancelled).IsTrue();
        await Assert.That(reagent.Count).IsEqualTo(1);
        await Assert.That(glider.IsDirty).IsTrue();
        await Assert.That(glider.UccId).IsEqualTo(0ul);
        await Assert.That(glider.ItemFlags).IsEqualTo(ItemFlag.SoulBound);
        await Assert.That(update.ReadByte()).IsEqualTo((byte)9);
        await Assert.That(update.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(update.LeftBytes).IsEqualTo(0);
    }

    [Test]
    [Arguments("foreign")]
    [Arguments("equipped")]
    [Arguments("secured")]
    [Arguments("missing catalyst")]
    [Arguments("trade pack")]
    public async Task RejectedGliderReversionKeepsAppearanceAndCatalyst(string reason)
    {
        var reagent = MakeReagent();
        var glider = MakeBackpack(reason == "trade pack" ? BackpackType.TradePack : BackpackType.Glider, GliderImage);
        switch (reason)
        {
            case "foreign": glider.OwnerId++; break;
            case "equipped": glider.SlotType = SlotType.Equipment; break;
            case "secured": glider.SetFlag(ItemFlag.Secure); break;
            case "missing catalyst": _bag.Items.Remove(reagent); break;
        }

        var skill = Cast(reagent, glider);

        await Assert.That(skill.Cancelled).IsTrue();
        await Assert.That(reagent.Count).IsEqualTo(2);
        await Assert.That(glider.ImageItemTemplateId).IsEqualTo(GliderImage);
    }

    [Test]
    public async Task TradePackRetainsItsOriginalDetailFormat()
    {
        var pack = MakeBackpack(BackpackType.TradePack);
        var update = ReadDetailUpdate(pack);

        await Assert.That(update.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(update.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ThirdEquipmentDetailFieldPreservesLegacyValuesAndTheRestoredDefaultDye()
    {
        var gear = MakeGear();
        var oldBlob = new PacketStream().Write((byte)23).Write((short)0).Write(0L).Write((ushort)0).Write((ushort)0);
        oldBlob.WritePisc(208, 0, CustomDye, 0).WritePisc(123, 0, 0, 0).WritePisc(0, 0, 0, 0)
            .WritePisc(0, 0, 0, 0).WritePisc(0, 0);
        gear.ReadDetails(new PacketStream(oldBlob.GetBytes()));
        var legacyDye = gear.DyeItemId;
        var reserializedLegacyBlob = new PacketStream();
        gear.WriteDetails(reserializedLegacyBlob);
        Cast(MakeReagent(), gear);

        var packet = new PacketStream(new SCItemDetailUpdatedPacket(gear).Write(new PacketStream()).GetBytes());
        packet.ReadBytes(11);
        var restored = new EquipItem();
        restored.ReadDetails(packet);

        await Assert.That(legacyDye).IsEqualTo(CustomDye);
        await Assert.That(reserializedLegacyBlob.GetBytes().SequenceEqual(oldBlob.GetBytes())).IsTrue();
        await Assert.That(restored.DyeItemId).IsEqualTo(OriginalDye);
        await Assert.That(restored.ImageItemTemplateId).IsEqualTo(0u);
        await Assert.That(restored.GemIds[4]).IsEqualTo(123u);
    }

    [Test]
    public async Task ClientUccRemovalUsesNativeOpcodeAndFieldWidths()
    {
        var packet = new SCItemUccDataChangedPacket(0, 0x12345678, 0x123456789ABCDEF0);
        var reader = new PacketStream(packet.Write(new PacketStream()).GetBytes());
        await Assert.That(packet.TypeId).IsEqualTo((ushort)0x12C);
        await Assert.That(reader.ReadUInt64()).IsEqualTo(0ul);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(0x12345678u);
        await Assert.That(reader.ReadUInt64()).IsEqualTo(0x123456789ABCDEF0ul);
        await Assert.That(reader.LeftBytes).IsEqualTo(0);
    }

    private Item MakeReagent() => Add(new Item(0, 1,
        new ItemTemplate { Id = RevertReagentId, UseSkillId = RevertSkillId, UseSkillAsReagent = true }, 2));

    private Backpack MakeBackpack(BackpackType type, uint image = 0)
    {
        var item = new Backpack(2, new BackpackTemplate { Id = 16409, BackpackType = type }, 1) { ImageItemTemplateId = image };
        Add(item);
        return item;
    }

    private static PacketStream ReadDetailUpdate(Item item)
    {
        var reader = new PacketStream(new SCItemDetailUpdatedPacket(item).Write(new PacketStream()).GetBytes());
        reader.ReadBytes(10);
        return reader;
    }

    private EquipItem MakeGear()
    {
        var template = new ArmorTemplate { Id = 26871, DefaultDyeItemId = OriginalDye };
        var gear = new EquipItem(2, template, 1)
        {
            ImageItemTemplateId = 208, DyeItemId = CustomDye, Durability = 23, ItemFlags = ItemFlag.SoulBound, UccId = 30001
        };
        gear.GemIds[4] = 123;
        Add(gear);
        gear.IsDirty = false;
        return gear;
    }

    private Item Add(Item item)
    {
        item.OwnerId = _owner.Id;
        item.SlotType = SlotType.Inventory;
        item.Slot = _bag.Items.Count;
        item.HoldingContainer = _bag;
        _bag.Items.Add(item);
        _items.Add(item.Id, item);
        _templates[item.TemplateId] = item.Template;
        return item;
    }

    private Skill Cast(Item reagent, Item gear)
    {
        var template = new SkillTemplate
        {
            Id = RevertSkillId, TargetType = SkillTargetType.Item,
            Effects =
            [
                new SkillEffect
                {
                    ApplicationMethod = SkillEffectApplicationMethod.Target, Chance = 100, EndLevel = 99,
                    ConsumeItemCount = 1, Template = new SpecialEffect { SpecialEffectTypeId = SpecialType.RevertItemLook }
                }
            ]
        };
        var skill = new Skill(template);
        var source = new SkillItem(_owner.ObjId, reagent.Id, reagent.TemplateId);
        skill.ApplyEffects(_owner, source, _owner, new SkillCastItemTarget { Id = gear.Id, Type = SkillCastTargetType.Item }, null);
        return skill;
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
