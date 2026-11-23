using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class Backpack : Item
{
    private byte[] _unrecognizedGliderDetails;

    internal bool IsGlider => Template is BackpackTemplate { BackpackType: BackpackType.Glider };
    public override ItemDetailType DetailType => IsGlider ? ItemDetailType.Glider : base.DetailType;

    public Backpack()
    {
    }

    public Backpack(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
    }

    public override void ReadDetails(PacketStream stream)
    {
        if (DetailType != ItemDetailType.Glider)
        {
            base.ReadDetails(stream);
            return;
        }

        _unrecognizedGliderDetails = null;
        ImageItemTemplateId = 0;
        if (stream is { LeftBytes: sizeof(uint) })
            ImageItemTemplateId = stream.ReadUInt32();
        else if (stream is { LeftBytes: > 0 })
            _unrecognizedGliderDetails = stream.ReadBytes(stream.LeftBytes);
    }

    public override void WriteDetails(PacketStream stream)
    {
        if (DetailType == ItemDetailType.Glider)
            stream.Write(ImageItemTemplateId);
        else
            base.WriteDetails(stream);
    }

    protected override void ReadClientDetails(PacketStream stream)
    {
        if (base.DetailType == ItemDetailType.Glider)
            ImageItemTemplateId = stream.ReadUInt32();
        else if (IsGlider && base.DetailType == ItemDetailType.Invalid)
            ImageItemTemplateId = 0;
        else
            base.ReadDetails(stream);
    }

    internal void WriteStoredGliderDetails(PacketStream stream)
    {
        // Unknown imported bytes belong to persistence. The client always expects exactly one uint.
        if (_unrecognizedGliderDetails is not null && ImageItemTemplateId == 0)
            stream.Write(_unrecognizedGliderDetails);
        else
            WriteDetails(stream);
    }

    internal void ChangeGliderAppearance(uint imageTemplateId)
    {
        _unrecognizedGliderDetails = null;
        ImageItemTemplateId = imageTemplateId;
    }
}
