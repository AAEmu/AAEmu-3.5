using System.Reflection;
using System.Runtime.CompilerServices;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.UnitTests.Utils;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Items.Containers;

[NotInParallel]
public class InventoryTests
{
    [Before(Test)]
    public void Setup()
    {
        ResetItemManager();
    }

    [After(Test)]
    public void Cleanup()
    {
        ResetSingleton<ItemManager>();
    }

    [Test]
    public async Task InventoryAddsItem()
    {
        var mockCharacter = new CharacterMock();
        var container = new ItemContainer(mockCharacter.Id, SlotType.Inventory, false, mockCharacter);
        var item = InventoryTestUtils.MockItem(1, 1);

        await Assert.That(container.AddOrMoveExistingItem(ItemTaskType.Gm, item, 1)).IsTrue();

        Item foundItem = null;
        foreach (var containerItem in container.Items)
        {
            if (containerItem.TemplateId == 1)
            {
                foundItem = containerItem;
                break;
            }
        }

        await Assert.That(foundItem).IsNotNull();
    }

    [Test]
    public async Task ExactConsumptionDoesNotPartiallyConsumeAnInsufficientStack()
    {
        var container = new ItemContainer(0, SlotType.Inventory, false, null);
        var item = InventoryTestUtils.MockItem(1, 15659);
        item.Count = 49;
        PutItemInContainer(container, item, SlotType.Inventory, 0);

        var success = container.TryConsumeExact(ItemTaskType.Invalid, item.TemplateId, 50);

        await Assert.That(success).IsFalse();
        await Assert.That(item.Count).IsEqualTo(49);
    }

    [Test]
    public async Task ConcurrentExactConsumptionOnlyChargesOneCallerWhenTheStackCoversOneRequest()
    {
        var container = new ItemContainer(0, SlotType.Inventory, false, null);
        var item = InventoryTestUtils.MockItem(1, 15659);
        item.Count = 51;
        PutItemInContainer(container, item, SlotType.Inventory, 0);

        var outcomes = await Task.WhenAll(
            Task.Run(() => container.TryConsumeExact(ItemTaskType.Invalid, item.TemplateId, 50)),
            Task.Run(() => container.TryConsumeExact(ItemTaskType.Invalid, item.TemplateId, 50)));

        await Assert.That(outcomes[0] ^ outcomes[1]).IsTrue();
        await Assert.That(item.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SplitOrMoveItemEx_SplitAcrossContainers_AssignsTargetOwner()
    {
        var sourceContainer = new ItemContainer(111, SlotType.Bank, false, null);
        var targetContainer = new ItemContainer(222, SlotType.Inventory, false, null);
        var inventory = CreateInventory(sourceContainer, targetContainer);
        var template = SetTemplate(5001, 100);
        var fromItem = new ItemMock(1001, template, 10);

        PutItemInContainer(sourceContainer, fromItem, SlotType.Bank, 2);
        ItemManager.Instance.AddItem(fromItem);

        var result = inventory.SplitOrMoveItemEx(
            ItemTaskType.Invalid,
            sourceContainer,
            targetContainer,
            fromItem.Id,
            SlotType.Bank,
            2,
            0,
            SlotType.Inventory,
            4,
            3);

        await Assert.That(result).IsTrue();
        await Assert.That(fromItem.Count).IsEqualTo(7);
        await Assert.That(targetContainer.Items.Count).IsEqualTo(1);

        var splitItem = targetContainer.Items[0];
        await Assert.That(splitItem.OwnerId).IsEqualTo((ulong)targetContainer.OwnerId);
        await Assert.That(splitItem.HoldingContainer).IsSameReferenceAs(targetContainer);
        await Assert.That(splitItem.SlotType).IsEqualTo(SlotType.Inventory);
        await Assert.That(splitItem.Slot).IsEqualTo(4);
    }

    [Test]
    public async Task SplitOrMoveItem_StaleTargetItemId_UsesAuthoritativeTargetSlot()
    {
        var sourceContainer = new ItemContainer(111, SlotType.Inventory, false, null);
        var targetContainer = new ItemContainer(111, SlotType.Bank, false, null);
        var inventory = CreateInventory(sourceContainer, targetContainer);

        var fromItem = InventoryTestUtils.MockItem(1001, 5001);
        var targetItem = InventoryTestUtils.MockItem(1002, 5002);

        PutItemInContainer(sourceContainer, fromItem, SlotType.Inventory, 4);
        PutItemInContainer(targetContainer, targetItem, SlotType.Bank, 6);
        ItemManager.Instance.AddItem(fromItem);
        ItemManager.Instance.AddItem(targetItem);

        var result = inventory.SplitOrMoveItem(
            ItemTaskType.Invalid,
            fromItem.Id,
            SlotType.Inventory,
            4,
            999999,
            SlotType.Bank,
            6);

        await Assert.That(result).IsTrue();
        await Assert.That(sourceContainer.Items).Contains(targetItem);
        await Assert.That(targetContainer.Items).Contains(fromItem);
        await Assert.That(fromItem.SlotType).IsEqualTo(SlotType.Bank);
        await Assert.That(fromItem.Slot).IsEqualTo(6);
        await Assert.That(targetItem.SlotType).IsEqualTo(SlotType.Inventory);
        await Assert.That(targetItem.Slot).IsEqualTo(4);
    }

    private static Inventory CreateInventory(params ItemContainer[] containers)
    {
        var inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        var equipment = new ItemContainer(0, SlotType.Equipment, false, null);
        var containerMap = new Dictionary<SlotType, ItemContainer>
        {
            [SlotType.Equipment] = equipment
        };

        foreach (var container in containers)
            containerMap[container.ContainerType] = container;

        typeof(Inventory)
            .GetField("<Equipment>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(inventory, equipment);
        typeof(Inventory)
            .GetProperty("_itemContainers", BindingFlags.Instance | BindingFlags.Public)
            ?.SetValue(inventory, containerMap);

        return inventory;
    }

    private static ItemTemplate SetTemplate(uint templateId, int maxCount)
    {
        var template = new ItemTemplate
        {
            Id = templateId,
            BindType = ItemBindType.Normal,
            MaxCount = maxCount
        };

        typeof(ItemManager)
            .GetField("_templates", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(ItemManager.Instance, new Dictionary<uint, ItemTemplate>
            {
                [template.Id] = template
            });

        return template;
    }

    private static void PutItemInContainer(ItemContainer container, Item item, SlotType slotType, int slot)
    {
        item.SlotType = slotType;
        item.Slot = slot;
        item.HoldingContainer = container;
        item.OwnerId = container.OwnerId;
        container.Items.Add(item);
        container.UpdateFreeSlotCount();
    }

    private static void ResetItemManager()
    {
        ResetSingleton<ItemManager>();

        var itemManager = new ItemManager(
            Mock.Of<ISkillManager>().Object,
            new TestItemIdManager(),
            Mock.Of<IContainerIdManager>().Object,
            Mock.Of<ILocalizationManager>().Object,
            Mock.Of<ITaskManager>().Object,
            Mock.Of<IWorldManager>().Object);
        SetSingleton(itemManager);

        typeof(ItemManager)
            .GetField("_allItems", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(itemManager, new Dictionary<ulong, Item>());
        typeof(ItemManager)
            .GetField("_removedItems", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(itemManager, new List<ulong>());
        typeof(ItemManager)
            .GetField("_allPersistentContainers", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(itemManager, new Dictionary<ulong, ItemContainer>());
    }

    private static void ResetSingleton<T>() where T : class
    {
        SetSingleton<T>(null);
    }

    private static void SetSingleton<T>(T instance) where T : class
    {
        typeof(AAEmu.Commons.Utils.Singleton<T>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, instance);
    }

    private sealed class TestItemIdManager : IItemIdManager
    {
        private uint _nextId = 0x01000000u;

        public void Load()
        {
        }

        public bool Initialize(bool forceReset = false)
        {
            _nextId = 0x01000000u;
            return true;
        }

        public uint GetNextId()
        {
            return _nextId++;
        }

        public uint[] GetNextId(int count)
        {
            var ids = new uint[count];
            for (var i = 0; i < count; i++)
                ids[i] = GetNextId();
            return ids;
        }

        public void ReleaseId(uint usedObjectId)
        {
        }

        public void ReleaseId(IEnumerable<uint> usedObjectIds)
        {
        }
    }
}
