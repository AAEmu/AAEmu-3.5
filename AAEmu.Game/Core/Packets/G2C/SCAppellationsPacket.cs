using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Sends the list of acquired appellations (titles) to the client.
/// </summary>
/// <remarks>
/// Matches the client-side structure <c>SCAppellations_0x2C5</c> in x2game_dump.dll.
/// In memory the client stores <c>countApps</c>, then <c>type[512]</c> at offset 0x10
/// and <c>selected[512]</c> at offset 0x810, but the network stream is interleaved:
/// count, then (type, selected) pairs.
/// </remarks>
public class SCAppellationsPacket((uint id, bool selected)[] appellations) : GamePacket(SCOffsets.SCAppellationsPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(appellations.Length); // countApps, max 512
        foreach (var (id, selected) in appellations)
        {
            stream.Write(id);       // type
            stream.Write(selected); // selected
        }

        return stream;
    }
}
