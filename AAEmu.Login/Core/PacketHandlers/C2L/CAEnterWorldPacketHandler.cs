using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CAEnterWorldPacket"/> which is sent by the client to request entering the game world.
/// </summary>
public class CAEnterWorldPacketHandler(ILogger<CAEnterWorldPacketHandler> logger) : ILoginPacketHandler<CAEnterWorldPacket>
{
    public async Task Execute(CAEnterWorldPacket packet, ILoginSession session, CancellationToken cancellationToken)
    {
        logger.LogDebug("[ENTRY-DIAG] Enter-world packet from {ClientIP}: game server {GameServerId}, login state {LoginState}",
            session.Connection.Ip, packet.GsId, session.State);
        await session.InitiateEnterWorldAsync(packet.GsId, cancellationToken);
    }
}
