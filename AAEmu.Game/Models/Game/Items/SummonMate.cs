using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class SummonMate : Item
{
    public int DetailMateExp { get; set; }
    public byte DetailLevel { get; set; }
    public uint MateDbId { get; set; }
    public DateTime RepairStartTime { get; set; } = DateTime.MinValue;

    public override ItemDetailType DetailType => ItemDetailType.Mate;
    public override uint DetailBytesLength => 24;

    public SummonMate()
    {
    }

    public SummonMate(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
    }

    public override void ReadDetails(PacketStream stream)
    {
        if (stream.LeftBytes < DetailBytesLength)
            return;
        DetailMateExp = stream.ReadInt32();  // 4 bytes: exp
        _ = stream.ReadByte();               // 1 byte: padding
        DetailLevel = stream.ReadByte();     // 1 byte: level
        MateDbId = stream.ReadUInt32();      // 4 bytes: mate DB id
        var repairTicks = stream.ReadInt64(); // 8 bytes: repair cooldown ticks
        RepairStartTime = repairTicks > 0 ? DateTime.SpecifyKind(new DateTime(repairTicks), DateTimeKind.Utc) : DateTime.MinValue;
        _ = stream.ReadBytes(6);             // 6 bytes: padding to reach 24
    }

    public override void WriteDetails(PacketStream stream)
    {
        stream.Write(DetailMateExp);         // 4
        stream.Write((byte)0);               // 1
        stream.Write(DetailLevel);           // 1
        stream.Write(MateDbId);              // 4
        stream.Write(RepairStartTime > DateTime.MinValue ? RepairStartTime.Ticks : 0L); // 8
        stream.Write(new byte[6]);           // 6 — total 24
    }

    public override void OnManuallyDestroyingItem()
    {
        base.OnManuallyDestroyingItem();
        // TODO: Call function to remove mate entries from DB
    }

    public override bool CanDestroy()
    {
        // Mounts should always be able to be destroyed as they cannot carry any persistent items anyway
        // It should just un-summon it while the item is getting deleted
        return true;
    }
}
