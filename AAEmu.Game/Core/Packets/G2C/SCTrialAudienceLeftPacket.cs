using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTrialAudienceLeftPacket(uint audienceObjId, string audienceName)
    : GamePacket(SCOffsets.SCTrialAudienceLeftPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(audienceObjId);
        stream.Write(audienceName);
        return base.Write(stream);
    }
}
