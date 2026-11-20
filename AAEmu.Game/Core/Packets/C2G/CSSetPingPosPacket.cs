using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSetPingPosPacket() : GamePacket(CSOffsets.CSSetPingPosPacket, 5)
{
    public override void Read(PacketStream stream)
    {
        if (stream.Count - stream.Pos < sizeof(uint))
            return;

        var teamId = stream.ReadUInt32();
        var pingState = TeamPingState.Read(stream);
        var owner = Connection.ActiveChar;
        if (owner == null || pingState == null)
            return;

        if (pingState.ChangedType == TeamPingState.MapPingType)
            owner.LocalPingPosition = pingState.GetMapPosition();

        if (teamId > 0)
        {
            TeamManager.Instance.SetPingPos(owner, teamId, pingState);
        }
        else
        {
            owner.SendPacket(new SCTeamPingPosPacket(pingState));
        }
    }
}
