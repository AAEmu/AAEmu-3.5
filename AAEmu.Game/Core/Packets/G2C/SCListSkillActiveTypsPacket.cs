using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCListSkillActiveTypsPacket : GamePacket
{
    public readonly record struct SkillActiveType(ushort SkillId, byte ActiveType);

    private readonly SkillActiveType[] _entries;

    public SCListSkillActiveTypsPacket() : base(SCOffsets.SCListSkillActiveTypsPacket, 5)
    {
        _entries = [];
    }

    public SCListSkillActiveTypsPacket(SkillActiveType[] entries) : base(SCOffsets.SCListSkillActiveTypsPacket, 5)
    {
        _entries = entries;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)_entries.Length);
        foreach (var entry in _entries)
        {
            // sub_397F7260: UInt32 skill type followed by byte active type.
            stream.Write((uint)entry.SkillId);
            stream.Write(entry.ActiveType);
        }

        return stream;
    }
}
