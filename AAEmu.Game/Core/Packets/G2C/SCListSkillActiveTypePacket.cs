using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public sealed class SCListSkillActiveTypePacket(IReadOnlyCollection<KeyValuePair<uint, byte>> entries)
    : GamePacket(SCOffsets.SCListSkillActiveTypsPacket, 5)
{
    internal const int MaxEntries = 100;

    public override PacketStream Write(PacketStream stream)
    {
        if (entries.Count > MaxEntries)
            throw new ArgumentOutOfRangeException(nameof(entries));
        stream.Write((uint)entries.Count);
        foreach (var entry in entries)
            stream.Write(entry.Key).Write(entry.Value);
        return stream;
    }
}
