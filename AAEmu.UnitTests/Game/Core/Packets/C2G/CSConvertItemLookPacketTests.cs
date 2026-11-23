using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;

using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Features;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;

using GameFeature = AAEmu.Game.Models.Game.Features.Feature;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

[NotInParallel]
public class CSConvertItemLookPacketTests
{
    private const uint PowderId = 29880;
    private const uint ConverterObjectId = 0x123456;
    private const uint GliderCategory = 118;
    private const uint WingCategory = 175;
    private const uint ActorModelId = 25;
    private const GameFeature InBagConvertFeature = (GameFeature)150;
    private readonly List<(FieldInfo Field, object Previous)> _singletons = [];
    private readonly Dictionary<ulong, Item> _items = [];
    private readonly Dictionary<uint, ItemTemplate> _templates = [];
    private readonly ItemLookConvert _gliderConvert = new() { Id = 27, RequiredItemId = PowderId, RequiredItemCount = 15 };
    private readonly ItemLookConvert _wingConvert = new() { Id = 28, RequiredItemId = PowderId, RequiredItemCount = 15 };
    private FeatureSet _previousFeatures;
    private Character _owner;
    private TrackingBag _bag;
    private ItemContainer _bank;
    private GameConnection _connection;
    private WorldInstance _world;

    [Before(Test)]
    public void SetUp()
    {
        var manager = new ItemManager(null, Mock.Of<IItemIdManager>().Object, null, null, null, null);
        SetField(manager, "_allItems", _items);
        SetField(manager, "_removedItems", new List<ulong>());
        SetField(manager, "_templates", _templates);
        SetField(manager, "_itemLookConverts", new Dictionary<uint, ItemLookConvert> { [27] = _gliderConvert, [28] = _wingConvert });
        SetField(manager, "_gliderItemLookConverts", new Dictionary<int, uint> { [(int)GliderCategory] = 27, [(int)WingCategory] = 28 });
        Install(manager);

        var models = new ModelManager();
        SetField(models, "_modelTypes", new Dictionary<uint, ModelType>
        {
            [ActorModelId] = new() { SubType = "Actor", SubId = ActorModelId }
        });
        SetField(models, "_models", new Dictionary<string, Dictionary<uint, Model>>
        {
            ["Actor"] = new() { [ActorModelId] = new ActorModel { Radius = 0.5f } }
        });
        Install(models);

        _previousFeatures = FeaturesManager.Fsets;
        SetInBagFeature(true);
        _owner = new Character(null) { Id = 37, ObjId = 45 };
        _owner.Inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        typeof(Inventory).GetField(nameof(Inventory.Owner))!.SetValue(_owner.Inventory, _owner);
        _bag = new TrackingBag { ContainerSize = 20 };
        _bank = new ItemContainer(0, SlotType.Bank, false, null) { ContainerSize = 20 };
        SetField(_owner.Inventory, "<Bag>k__BackingField", _bag);
        SetField(_owner.Inventory, "<Warehouse>k__BackingField", _bank);
        SetField(_owner.Inventory, "<Equipment>k__BackingField", _owner.Equipment);
        SetField(_owner.Inventory, "<_itemContainers>k__BackingField", new Dictionary<SlotType, ItemContainer>
        {
            [SlotType.Inventory] = _bag,
            [SlotType.Bank] = _bank,
            [SlotType.Equipment] = _owner.Equipment
        });
        _connection = new GameConnection(null) { ActiveChar = _owner };
        _world = new WorldInstance(new WorldTemplate(), 0, true, _owner.Transform.InstanceId);
        _owner.ParentWorld = _world;
    }

    [After(Test)]
    public void TearDown()
    {
        FeaturesProperty.SetValue(null, _previousFeatures);
        foreach (var (field, previous) in _singletons)
            field.SetValue(null, previous);
    }

    [Test]
    [Arguments(GliderCategory)]
    [Arguments(WingCategory)]
    public async Task ConversionConsumesAllFifteenAlembicsAndAppliesTheDonorLook(uint category)
    {
        var target = Glider(30001, category, 3, true);
        var donor = Glider(30002, category, 2);
        var firstStack = Powder(7);
        var secondStack = Powder(10);

        var packet = ReadConvert(target.Id, donor.Id);

        await Assert.That(packet.LeftBytes).IsEqualTo(0);
        await Assert.That(target.ImageItemTemplateId).IsEqualTo(donor.TemplateId);
        await Assert.That(_bag.Items).DoesNotContain(donor);
        await Assert.That(_bag.Items).DoesNotContain(firstStack);
        await Assert.That(secondStack.Count).IsEqualTo(2);
    }

    [Test]
    [Arguments(false, 0f, false)]
    [Arguments(true, 2.75f, true)]
    [Arguments(true, 3.25f, true)]
    [Arguments(true, 3.5f, false)]
    [Arguments(true, 3.75f, false)]
    public async Task NpcConversionRequiresTheAuthoredServiceAndThreeMetresToItsShape(bool converter, float x, bool allowed)
    {
        SetInBagFeature(false);
        AddNpc(converter, x);
        var target = Glider(30001, GliderCategory, 3, true);
        var donor = Glider(30002, GliderCategory, 2);
        var powder = Powder(16);

        var packet = ReadConvert(target.Id, donor.Id, ConverterObjectId);

        await Assert.That(packet.LeftBytes).IsEqualTo(0);
        await Assert.That(target.ImageItemTemplateId).IsEqualTo(allowed ? donor.TemplateId : 0u);
        await Assert.That(_bag.Items.Contains(donor)).IsEqualTo(!allowed);
        await Assert.That(powder.Count).IsEqualTo(allowed ? 1 : 16);
    }

    [Test]
    [Arguments(0u, false)]
    [Arguments(ConverterObjectId, true)]
    public async Task MissingNpcOrInBagPermissionLeavesAnOtherwiseValidArmourConversionIntact(uint npcId, bool inBagFeature)
    {
        SetInBagFeature(inBagFeature);
        var target = Armor(30001, 3, true);
        var donor = Armor(30002, 2);
        var powder = Powder(16);

        ReadConvert(target.Id, donor.Id, npcId);

        await Assert.That(target.ImageItemTemplateId).IsEqualTo(0u);
        await Assert.That(_bag.Items).Contains(donor);
        await Assert.That(powder.Count).IsEqualTo(16);
    }

    [Test]
    [Arguments("category")]
    [Arguments("gender")]
    [Arguments("stat disabled")]
    [Arguments("skinized flag missing")]
    [Arguments("foreign donor")]
    [Arguments("secure target")]
    [Arguments("short powder")]
    public async Task InvalidPairOrInsufficientPowderChangesNeitherGliderAndSpendsNothing(string reason)
    {
        var target = Glider(30001, GliderCategory, 3, reason != "stat disabled");
        var donor = Glider(30002, reason == "category" ? WingCategory : GliderCategory,
            reason == "skinized flag missing" ? 3u : 2u, gender: reason == "gender" ? (byte)2 : (byte)0);
        if (reason == "foreign donor")
            donor.OwnerId++;
        if (reason == "secure target")
            target.SetFlag(ItemFlag.Secure);
        var powderCount = reason == "short powder" ? 14 : 16;
        var powder = Powder(powderCount);

        ReadConvert(target.Id, donor.Id);

        await Assert.That(target.ImageItemTemplateId).IsEqualTo(0u);
        await Assert.That(_bag.Items).Contains(donor);
        await Assert.That(powder.Count).IsEqualTo(powderCount);
    }

    [Test]
    public async Task ConcurrentConversionsCannotReuseTheSameDonor()
    {
        var first = Glider(30001, GliderCategory, 3, true);
        var second = Glider(30002, GliderCategory, 3, true);
        var donor = Glider(30003, GliderCategory, 3);
        donor.SetFlag(ItemFlag.Skinized);
        var powder = Powder(31);

        Parallel.Invoke(() => ReadConvert(first.Id, donor.Id), () => ReadConvert(second.Id, donor.Id));

        await Assert.That((first.ImageItemTemplateId == donor.TemplateId) != (second.ImageItemTemplateId == donor.TemplateId)).IsTrue();
        await Assert.That(_bag.Items).DoesNotContain(donor);
        await Assert.That(powder.Count).IsEqualTo(16);
    }

    [Test]
    public async Task ExistingArmourConversionPreservesDyeAndCrescentStones()
    {
        var target = Armor(30001, 3, true);
        target.DyeItemId = 30123;
        target.GemIds[4] = 30124;
        var donor = Armor(30002, 2);
        var powder = Powder(16);

        var packet = ReadConvert(target.Id, donor.Id);

        await Assert.That(packet.LeftBytes).IsEqualTo(0);
        await Assert.That(target.ImageItemTemplateId).IsEqualTo(donor.TemplateId);
        await Assert.That(target.DyeItemId).IsEqualTo(30123u);
        await Assert.That(target.GemIds[4]).IsEqualTo(30124u);
        await Assert.That(_bag.Items).DoesNotContain(donor);
        await Assert.That(powder.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TruncatedNpcFieldCannotFallBackToInBagConversion()
    {
        var target = Armor(30001, 3, true);
        var donor = Armor(30002, 2);
        var powder = Powder(16);
        var wire = new PacketStream().Write(target.Id).Write(donor.Id).Write((ushort)0x3456);
        wire.Rollback();

        new CSConvertItemLookPacket { Connection = _connection }.Read(wire);

        await Assert.That(target.ImageItemTemplateId).IsEqualTo(0u);
        await Assert.That(_bag.Items).Contains(donor);
        await Assert.That(powder.Count).IsEqualTo(16);
    }

    [Test]
    public async Task FirstClientNotificationSeesTheDonorRemovedAndTheFullConversionCommitted()
    {
        var target = Glider(30001, GliderCategory, 3, true);
        var donor = Glider(30002, GliderCategory, 2);
        var powder = Powder(16);
        var notified = false;
        var committedAtFirstSend = false;
        var encryption = new EncryptionManager();
        Install(encryption);
        encryption.Load();
        _owner.Connection = _connection = new GameConnection(new CapturingSession(() =>
        {
            if (notified)
                return;

            notified = true;
            committedAtFirstSend = target.ImageItemTemplateId == donor.TemplateId && !_bag.Items.Contains(donor) &&
                                  ItemManager.Instance.GetItemByItemId(donor.Id) == null && powder.Count == 1;
        })) { ActiveChar = _owner };

        ReadConvert(target.Id, donor.Id);

        await Assert.That(notified).IsTrue();
        await Assert.That(committedAtFirstSend).IsTrue();
    }

    [Test]
    public async Task RemovingATimedDonorCancelsItsExpirationAndNotifiesTheBagAfterCommit()
    {
        var target = Glider(30001, GliderCategory, 3, true);
        var donor = Glider(30002, GliderCategory, 2);
        donor.ExpirationTime = DateTime.UtcNow.AddMinutes(1);
        donor.ExpirationOnlineMinutesLeft = 1;
        var powder = Powder(16);
        var notifications = 0;
        (bool Committed, byte Slot) departure = default;
        _bag.ItemLeft = (item, previousSlot) =>
        {
            if (item != donor)
                return;

            notifications++;
            departure = (item.HoldingContainer == null && item.ExpirationTime == DateTime.MinValue &&
                         item.ExpirationOnlineMinutesLeft == 0 && target.ImageItemTemplateId == donor.TemplateId &&
                         powder.Count == 1, previousSlot);
        };

        ReadConvert(target.Id, donor.Id);

        await Assert.That(notifications).IsEqualTo(1);
        await Assert.That(departure.Committed).IsTrue();
        await Assert.That(departure.Slot).IsEqualTo((byte)1);
    }

    [Test]
    public async Task ApplyingThenRevertingAnAppearanceDoesNotRestoreObsoleteImportedDetails()
    {
        var target = Glider(30001, GliderCategory, 3, true);
        var donor = Glider(30002, GliderCategory, 2);
        Powder(15);
        ItemManager.ReadUserItemDetails(target, new byte[] { 9, 1, 2, 3, 4 });
        const uint revertSkill = 27476;
        var catalyst = new Item(87, new ItemTemplate { Id = 35117, UseSkillId = revertSkill, UseSkillAsReagent = true }, 2);
        AddToBag(catalyst);

        ReadConvert(target.Id, donor.Id);
        var result = _bag.RevertItemLook(_owner.Id, catalyst.Id, revertSkill, target.Id, out _, out _, out _);
        var persisted = new PacketStream(ItemManager.WriteUserItemDetails(target).GetBytes());

        await Assert.That(result).IsEqualTo(ErrorMessageType.NoErrorMessage);
        await Assert.That(catalyst.Count).IsEqualTo(1);
        await Assert.That(persisted.LeftBytes).IsEqualTo(sizeof(uint));
        await Assert.That(persisted.ReadUInt32()).IsEqualTo(0u);
    }

    [Test]
    public async Task MovingTheDonorToTheWarehouseCannotInterleaveWithAConversion()
    {
        var target = Glider(30001, GliderCategory, 3, true);
        using var planning = new ManualResetEventSlim();
        using var commit = new ManualResetEventSlim();
        var donor = new PausingGlider(new BackpackTemplate
        {
            Id = 30002, ImplId = ItemImplEnum.Backpack, MaxCount = 1, BackpackType = BackpackType.Glider,
            CategoryId = (int)GliderCategory, SkinKindId = 2
        }, planning, commit);
        AddToBag(donor);
        var powder = Powder(16);
        var timeout = TimeSpan.FromSeconds(5);
        var convertTask = Task.Run(() => ReadConvert(target.Id, donor.Id));
        Task moveTask = Task.CompletedTask;
        bool movedDuringConversion;

        try
        {
            await Assert.That(planning.Wait(timeout)).IsTrue();
            var moveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            moveTask = Task.Run(() =>
            {
                moveStarted.SetResult();
                _owner.Inventory.SplitOrMoveItem(ItemTaskType.SwapItems, donor.Id, SlotType.Inventory, (byte)donor.Slot,
                    0, SlotType.Bank, 0);
            });
            await moveStarted.Task.WaitAsync(timeout);
            movedDuringConversion = await Task.WhenAny(moveTask, Task.Delay(100)) == moveTask;
        }
        finally
        {
            commit.Set();
        }

        await Task.WhenAll(convertTask, moveTask).WaitAsync(timeout);
        await Assert.That(movedDuringConversion).IsFalse();
        await Assert.That(target.ImageItemTemplateId).IsEqualTo(donor.TemplateId);
        await Assert.That(_bank.Items).DoesNotContain(donor);
        await Assert.That(powder.Count).IsEqualTo(1);
    }

    private static PropertyInfo FeaturesProperty => typeof(FeaturesManager).GetProperty(nameof(FeaturesManager.Fsets))!;

    private static void SetInBagFeature(bool enabled)
    {
        var features = new FeatureSet();
        features.Set(InBagConvertFeature, enabled);
        FeaturesProperty.SetValue(null, features);
    }

    private void AddNpc(bool converter, float x)
    {
        var npc = new Npc
        {
            ObjId = ConverterObjectId, ParentWorld = _world, Hp = 100, IsVisible = true, ModelId = ActorModelId,
            Template = new NpcTemplate { Id = 895, LookConverter = converter, Scale = 1f }
        };
        npc.Transform.Local.SetPosition(x, 0f, 0f);
        _world.AddObject(npc);
    }

    private Backpack Glider(uint id, uint category, uint skinKind, bool useAsStat = false, byte gender = 0)
    {
        var template = new BackpackTemplate
        {
            Id = id, ImplId = ItemImplEnum.Backpack, MaxCount = 1, BackpackType = BackpackType.Glider,
            CategoryId = (int)category, CharGender = gender, UseAsStat = useAsStat, SkinKindId = skinKind
        };
        var item = new Backpack(id, template, 1);
        AddToBag(item);
        return item;
    }

    private EquipItem Armor(uint id, int skinKind, bool useAsStat = false)
    {
        var template = new ArmorTemplate
        {
            Id = id, ImplId = ItemImplEnum.Armor, MaxCount = 1, AssetId = 1, UseAsStat = useAsStat,
            SkinKindId = skinKind, ItemLookConvert = _gliderConvert
        };
        var item = new EquipItem(id, template, 1);
        AddToBag(item);
        return item;
    }

    private Item Powder(int count)
    {
        var item = new Item(0, 0x10000u + (uint)_items.Count, new ItemTemplate { Id = PowderId, MaxCount = 100 }, count);
        AddToBag(item);
        return item;
    }

    private void AddToBag(Item item)
    {
        item.OwnerId = _owner.Id;
        item.SlotType = SlotType.Inventory;
        item.Slot = _bag.Items.Count;
        item.HoldingContainer = _bag;
        _bag.Items.Add(item);
        _items.Add(item.Id, item);
        _templates[item.TemplateId] = item.Template;
    }

    private PacketStream ReadConvert(ulong target, ulong donor, uint npcId = 0)
    {
        // Native CS 0x0DB, two little-endian uint64 values and one three-byte object id.
        var wire = new PacketStream().Write(target).Write(donor)
            .Write((byte)npcId).Write((byte)(npcId >> 8)).Write((byte)(npcId >> 16));
        wire.Rollback();
        new CSConvertItemLookPacket { Connection = _connection }.Read(wire);
        return wire;
    }

    private static void SetField<T>(T owner, string name, object value) =>
        typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);

    private void Install<T>(T singleton) where T : class
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _singletons.Add((field, field.GetValue(null)));
        field.SetValue(null, singleton);
    }

    private sealed class PausingGlider(BackpackTemplate template, ManualResetEventSlim planning, ManualResetEventSlim commit)
        : Backpack(template.Id, template, 1)
    {
        public override bool CanDestroy()
        {
            planning.Set();
            return commit.Wait(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class TrackingBag() : ItemContainer(0, SlotType.Inventory, false, null)
    {
        public Action<Item, byte> ItemLeft { get; set; }

        public override void OnLeaveContainer(Item item, ItemContainer newContainer, byte previousSlot) => ItemLeft?.Invoke(item, previousSlot);
    }

    private sealed class CapturingSession(Action onSend) : ISession
    {
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId => 37;
        public Socket Socket => throw new NotSupportedException();

        public void SendPacket(byte[] packet) => onSend();

        public void AddAttribute(string name, object attribute) => throw new NotSupportedException();
        public object GetAttribute(string name) => throw new NotSupportedException();
        public void ClearAttribute(string name) => throw new NotSupportedException();
        public void Close() => throw new NotSupportedException();
    }
}
