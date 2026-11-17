using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUpdateSkillActiveTypePacket : GamePacket
{
    private readonly ushort _skillId;
    private readonly byte _activeType;

    public SCUpdateSkillActiveTypePacket(ushort skillId, byte activeType) : base(SCOffsets.SCUpdateSkillActiveTypePacket, 5)
    {
        _skillId = skillId;
        _activeType = activeType;
    }

    public override PacketStream Write(PacketStream stream)
    {
        // sub_3993F4D0: UInt32 skill type followed by byte active type.
        stream.Write((uint)_skillId);
        stream.Write(_activeType);

        return stream;
    }
}
