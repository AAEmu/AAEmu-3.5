using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCResetHeirSkillPacket : GamePacket
{
    private readonly uint _resetKind;
    private readonly ushort _type;
    private readonly byte _ability;

    public SCResetHeirSkillPacket(uint resetKind, ushort type, byte ability) : base(SCOffsets.SCResetHeirSkillPacket, 5)
    {
        _resetKind = resetKind;
        _type = type;
        _ability = ability;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_resetKind);
        stream.Write(_type);
        stream.Write(_ability);

        return stream;
    }
}
