using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCNpcFriendshipChangedPacket(SCNpcFriendshipListPacket.NpcFriendship friendship)
    : GamePacket(SCOffsets.SCNpcFriendshipChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(friendship.Type);
        stream.Write(friendship.Value);
        stream.Write(friendship.Time);
        return stream;
    }
}
