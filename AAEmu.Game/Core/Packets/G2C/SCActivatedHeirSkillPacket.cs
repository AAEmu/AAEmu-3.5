using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCActivatedHeirSkillPacket : GamePacket
{
    private readonly ushort _type1;
    private readonly ushort _type2;
    private readonly bool _isChange;

    public SCActivatedHeirSkillPacket(ushort type1, ushort type2, bool isChange) : base(SCOffsets.SCActivatedHeirSkillPacket, 5)
    {
        _type1 = type1;
        _type2 = type2;
        _isChange = isChange;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_type1);
        stream.Write(_type2);
        stream.Write(_isChange);

        return stream;
    }
}
