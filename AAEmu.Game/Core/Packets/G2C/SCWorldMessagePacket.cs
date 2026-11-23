using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCWorldMessagePacket(uint effectId, Vector3 position, uint sourceFaction, string sourceName,
    uint targetFaction, string targetName)
    : GamePacket(SCOffsets.SCWorldMessagePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(effectId);
        stream.Write(Helpers.ConvertLongX(position.X));
        stream.Write(Helpers.ConvertLongY(position.Y));
        stream.Write(position.Z);
        stream.Write(sourceFaction);
        stream.Write(sourceName ?? string.Empty);
        stream.Write(targetFaction);
        stream.Write(targetName ?? string.Empty);
        return stream;
    }
}
