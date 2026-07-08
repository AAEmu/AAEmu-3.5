using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCNpcFriendshipListPacket(SCNpcFriendshipListPacket.NpcFriendship[] friendships)
    : GamePacket(SCOffsets.SCNpcFriendshipListPacket, 5)
{
    public readonly struct NpcFriendship(uint type, short value, DateTime time)
    {
        public uint Type { get; } = type;
        public short Value { get; } = value;
        public DateTime Time { get; } = time;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(friendships.Length); // TODO max length 200
        for (var i = 0; i < friendships.Length; i++)
        {
            var friendship = friendships[i];
            stream.Write(friendship.Type);
            stream.Write(friendship.Value);
            stream.Write(friendship.Time);
        }

        return stream;
    }
}
