using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCActivatedHeirSkillPacket : GamePacket
{
    private readonly ushort _groupId;
    private readonly ushort _successorId;
    private readonly bool _isChange;

    public SCActivatedHeirSkillPacket(ushort groupId, ushort successorId, bool isChange) : base(SCOffsets.SCActivatedHeirSkillPacket, 5)
    {
        _groupId = groupId;
        _successorId = successorId;
        _isChange = isChange;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_groupId);
        stream.Write(_successorId);
        stream.Write(_isChange);

        return stream;
    }
}
