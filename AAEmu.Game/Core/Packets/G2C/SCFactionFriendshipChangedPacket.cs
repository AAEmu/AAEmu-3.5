using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFactionFriendshipChangedPacket(
    uint id,
    uint id2,
    byte state,
    byte nextState,
    DateTime updateTime,
    DateTime changeTime,
    string updaterName = "",
    string memo = "") : GamePacket(SCOffsets.SCFactionFriendshipChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(id2);
        stream.Write(state);
        stream.Write(nextState);
        stream.Write(updateTime);
        stream.Write(changeTime);
        stream.Write(updaterName);
        stream.Write(memo);
        return stream;
    }
}
