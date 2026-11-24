using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class ItemTaskPacketWireFormatTests
{
    private const string EmptyTrailer = "00000000000000000000000000";

    [Test]
    public async Task LootedCoins_WriteLogTypeBeforeMoneyDelta()
    {
        var packet = new SCItemTaskSuccessPacket(ItemTaskType.DepositMoney, new MoneyChange(3), []);
        var payload = packet.Write(new PacketStream()).GetBytes();

        await Assert.That(Convert.ToHexString(payload)).IsEqualTo("5301010003000000" + EmptyTrailer);
    }

    [Test]
    [Arguments(5, "0A010403020608070605040302010500000013720000")]
    [Arguments(-5, "0A01040202060807060504030201FBFFFFFF13720000")]
    public async Task StackChange_WritesLogTypeBeforeSlot(int delta, string expectedTask)
    {
        var item = new Item(1) { TemplateId = 29203, Id = 0x0102030405060708, SlotType = SlotType.Inventory, Slot = 6 };
        var packet = new SCItemTaskSuccessPacket(ItemTaskType.LootAll, new ItemCountUpdate(item, delta), []);
        var payload = packet.Write(new PacketStream()).GetBytes();

        await Assert.That(Convert.ToHexString(payload)).IsEqualTo(expectedTask + EmptyTrailer);
    }

    [Test]
    public async Task LootedItem_WritesGainLogTypeBeforeSlot()
    {
        var item = new Item(1)
        {
            TemplateId = 29203,
            Id = 0x0102030405060708,
            SlotType = SlotType.Inventory,
            Slot = 6,
            Grade = 2,
            ItemFlags = ItemFlag.SoulBound,
            Count = 4,
            CreateTime = DateTime.UnixEpoch,
            UnsecureTime = DateTime.UnixEpoch,
            UnpackTime = DateTime.UnixEpoch,
            ChargeUseSkillTime = DateTime.UnixEpoch
        };
        var packet = new SCItemTaskSuccessPacket(ItemTaskType.Loot, new ItemAdd(item), []);
        var payload = packet.Write(new PacketStream()).GetBytes();
        const string expectedTask = "09010501020613720000080706050403020102010400000000" +
            "0000000000000000000000000000000001" +
            "000000000000000000000000000000000000000000000000";

        await Assert.That(Convert.ToHexString(payload)).IsEqualTo(expectedTask + EmptyTrailer);
    }

    [Test]
    public async Task TradeMade_WritesLockItemSlotKeyAfterType()
    {
        var packet = new SCTradeMadePacket(ItemTaskType.Trade, [], []);
        var payload = packet.Write(new PacketStream()).GetBytes();

        await Assert.That(Convert.ToHexString(payload)).IsEqualTo("2F00000000000000000000");
    }
}
