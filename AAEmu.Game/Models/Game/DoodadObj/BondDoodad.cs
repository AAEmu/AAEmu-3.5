using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.DoodadObj.Static;

namespace AAEmu.Game.Models.Game.DoodadObj;

public class BondDoodad : PacketMarshaler
{
    private Doodad _owner;
    private readonly byte _attachPoint;
    private readonly uint _kind;
    private readonly int _space;
    private readonly int _spot;

    public uint ObjId => _owner?.ObjId ?? 0;

    public BondDoodad(AttachPointKind attachPoint, BondKind kind, int space, int spot)
    {
        _attachPoint = (byte)attachPoint;
        _kind = (uint)kind;
        _space = space;
        _spot = spot;
    }

    public BondDoodad(Doodad owner, AttachPointKind attachPoint, BondKind kind, int space, int spot)
    {
        SetOwner(owner);
        _attachPoint = (byte)attachPoint;
        _kind = (uint)kind;
        _space = space;
        _spot = spot;
    }

    public void SetOwner(Doodad owner)
    {
        _owner = owner;
    }

    public Doodad GetOwner()
    {
        return _owner;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_attachPoint);
        stream.WriteBc(_owner.ObjId);
        stream.Write(_space);
        stream.Write(_spot);
        stream.Write(_kind);
        return stream;
    }
}
