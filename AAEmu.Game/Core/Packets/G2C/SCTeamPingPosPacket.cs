using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamPingPosPacket(TeamPingState pingState)
    : GamePacket(SCOffsets.SCTeamPingPosPacket, 5)
{
    public override PacketStream Write(PacketStream stream) => pingState.Write(stream);
}
