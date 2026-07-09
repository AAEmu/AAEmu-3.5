using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCGmDumpSkillPacket : GamePacket
{
    private readonly string _name;
    private readonly ushort[] _skillIds;

    public SCGmDumpSkillPacket(string name, ushort[] skillIds) : base(SCOffsets.SCGmDumpSkillPacket, 5)
    {
        _name = name;
        _skillIds = skillIds;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_name);
        stream.Write((uint)_skillIds.Length);
        foreach (var skillId in _skillIds)
        {
            stream.Write(skillId);
        }

        return stream;
    }
}
