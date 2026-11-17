using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCBuffStatePacket(Buff buff) : GamePacket(SCOffsets.SCBuffStatePacket, 5)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override PacketStream Write(PacketStream stream)
    {
        // The 3.5 client reader at 0x39384300 reads no payload for opcode 0x04E.
        // 0x399438C0 belongs to SCBuffCreated (0x1EE), not this packet.
        return stream;
    }

    public override string Verbose()
    {
        return $" - {buff.Owner.DebugName()} <- {buff?.Template?.BuffId}";
    }
}
