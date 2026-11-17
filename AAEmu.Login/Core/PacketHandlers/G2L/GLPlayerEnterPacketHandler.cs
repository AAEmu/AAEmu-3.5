using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.G2L;

namespace AAEmu.Login.Core.PacketHandlers.G2L;

/// <summary>
/// Handles the <see cref="GLPlayerEnterPacket"/> which is sent by the game server when a player enters the game world.
/// </summary>
public class GLPlayerEnterPacketHandler(IGameController gameController, ILogger<GLPlayerEnterPacketHandler> logger)
    : IInternalPacketHandler<GLPlayerEnterPacket>
{
    public Task Execute(GLPlayerEnterPacket packet, InternalConnection connection,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("[ENTRY-DIAG] Game server {GameServerId} answered enter-world request with result {Result}",
            packet.GsId, packet.Result);
        gameController.RouteEnterWorldResponse(packet.ConnectionId, packet.GsId, packet.Result);
        return Task.CompletedTask;
    }
}
