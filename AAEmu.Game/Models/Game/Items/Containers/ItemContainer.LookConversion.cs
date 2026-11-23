using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Models.Game.Items.Containers;

public partial class ItemContainer
{
    internal ErrorMessageType ConvertItemLook(Character owner, ulong statId, ulong donorId, uint npcObjId, out uint materialId)
    {
        materialId = 0;
        Item target;
        List<ItemTask> clientTasks;
        List<(Item Item, int Count)> consumed;
        List<(Item Item, byte PreviousSlot, GamePacket ExpirationSync)> removed = [];

        lock (_consumeLock)
        {
            if (!ItemLookConversion.CanUse(owner, npcObjId))
                return ErrorMessageType.InvalidTarget;

            target = GetOwnedBagItem(owner.Id, statId);
            var donor = GetOwnedBagItem(owner.Id, donorId);
            if (!IsCurrentBagSlot(target) || !IsCurrentBagSlot(donor) || statId == donorId || donor.Count != 1 || !donor.CanDestroy())
                return ErrorMessageType.BagInvalidItem;
            if (target.HasFlag(ItemFlag.Secure) || donor.HasFlag(ItemFlag.Secure))
                return ErrorMessageType.ItemSecureCondition;

            var result = ItemLookConversion.GetConversion(target, donor, out var conversion, out var image);
            if (result != ErrorMessageType.NoErrorMessage)
                return result;

            materialId = conversion.RequiredItemId;
            GetAllItemsByTemplate(materialId, -1, out var materialStacks, out _);
            materialStacks.Sort(static (first, second) => first.Slot.CompareTo(second.Slot));
            var remaining = conversion.RequiredItemCount;
            consumed = [];
            foreach (var stack in materialStacks)
            {
                if (remaining <= 0)
                    break;
                if (ReferenceEquals(stack, target) || ReferenceEquals(stack, donor) || stack.OwnerId != owner.Id ||
                    !IsCurrentBagSlot(stack) || stack.HasFlag(ItemFlag.Secure))
                    continue;

                var count = Math.Min(remaining, stack.Count);
                if (count == stack.Count && !stack.CanDestroy())
                    continue;

                consumed.Add((stack, count));
                remaining -= count;
            }

            if (remaining > 0 || consumed.Count == 0)
                return ErrorMessageType.NotEnoughRequiredItem;

            clientTasks = new List<ItemTask>(consumed.Count + 2);
            foreach (var (stack, count) in consumed)
            {
                if (stack.Count == count)
                    RemoveLookConversionItem(stack, clientTasks, removed);
                else
                {
                    stack.Count -= count;
                    clientTasks.Add(new ItemCountUpdate(stack, -count));
                }
            }
            RemoveLookConversionItem(donor, clientTasks, removed);
            consumed.Add((donor, 1));
            if (target is Backpack glider)
                glider.ChangeGliderAppearance(image);
            else
            {
                target.ImageItemTemplateId = image;
                clientTasks.Add(new ItemUpdate(target));
            }
            UpdateFreeSlotCount();
        }

        foreach (var (_, _, expirationSync) in removed)
        {
            if (expirationSync != null)
                owner.SendPacket(expirationSync);
        }
        foreach (var (item, count) in consumed)
            Owner?.Inventory.OnConsumedItem(item, count);
        foreach (var (item, previousSlot, _) in removed)
            OnLeaveContainer(item, null, previousSlot);
        if (target is Backpack)
            owner.SendPacket(new SCItemDetailUpdatedPacket(target));
        owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.ConvertItemLook, clientTasks, []));
        return ErrorMessageType.NoErrorMessage;
    }

    private bool IsCurrentBagSlot(Item item) => item is { Slot: >= 0, SlotType: SlotType.Inventory, Count: > 0 } &&
        item.HoldingContainer == this && ReferenceEquals(GetItemBySlot(item.Slot), item);

    private void RemoveLookConversionItem(Item item, List<ItemTask> clientTasks,
        List<(Item Item, byte PreviousSlot, GamePacket ExpirationSync)> removed)
    {
        clientTasks.Add(new ItemRemoveSlot(item));
        removed.Add((item, (byte)item.Slot, PrepareItemRemovalExpirationSync(item)));
        Items.Remove(item);
        item.HoldingContainer = null;
        item.Count = 0;
        ItemManager.Instance.ReleaseId(item.Id);
    }
}
