using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCWorldInteractionSkillListPacket : GamePacket
{
    private readonly uint _id1;
    private readonly uint _id2;
    private readonly uint _innerId1;
    private readonly uint _innerId2;
    private readonly ushort _type;
    private readonly uint _innerId3;
    private readonly uint _interactCount;
    private readonly uint _extraInfo;
    private readonly ushort[] _skillList;
    private readonly byte _mouseButton;
    private readonly uint _modifierKeys;

    public SCWorldInteractionSkillListPacket(
        uint id1,
        uint id2,
        uint innerId1,
        uint innerId2,
        ushort type,
        uint innerId3,
        uint interactCount,
        uint extraInfo,
        ushort[] skillList,
        byte mouseButton,
        uint modifierKeys) : base(SCOffsets.SCWorldInteractionSkillListPacket, 5)
    {
        _id1 = id1;
        _id2 = id2;
        _innerId1 = innerId1;
        _innerId2 = innerId2;
        _type = type;
        _innerId3 = innerId3;
        _interactCount = interactCount;
        _extraInfo = extraInfo;
        _skillList = skillList;
        _mouseButton = mouseButton;
        _modifierKeys = modifierKeys;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_id1);
        stream.WriteBc(_id2);
        stream.WriteBc(_innerId1);
        stream.WriteBc(_innerId2);
        stream.Write(_type);
        stream.WriteBc(_innerId3);
        stream.Write(_interactCount);
        stream.Write(_extraInfo);
        foreach (var skillId in _skillList)
        {
            stream.Write(skillId);
        }
        stream.Write(_mouseButton);
        stream.Write(_modifierKeys);

        return stream;
    }
}
