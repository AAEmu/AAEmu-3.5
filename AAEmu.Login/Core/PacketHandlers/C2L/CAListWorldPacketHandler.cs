using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CAListWorldPacket"/> which is sent by the client to request the list of available game
/// worlds.
/// </summary>
public class CAListWorldPacketHandler(IGameController gameController, ILogger<CAListWorldPacketHandler> logger) : ILoginPacketHandler<CAListWorldPacket>
{
    public async Task Execute(CAListWorldPacket packet, ILoginSession session,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("[ENTRY-DIAG] World-list request from {ClientIP}, account {AccountId}",
            session.Connection.Ip, session.Connection.AccountId);
        var worldList = await gameController.GetWorldListAsync(session.Connection);

        logger.LogDebug("[ENTRY-DIAG] World-list response for {ClientIP}: {ServerCount} configured servers, {CharacterCount} characters",
            session.Connection.Ip, worldList.GameServers.Count, worldList.Characters.Count);
        foreach (var gameServer in worldList.GameServers)
        {
            logger.LogDebug("[ENTRY-DIAG] World-list server {GameServerId} ({Name}): active={Active}, endpoint={Host}:{Port}",
                gameServer.Id, gameServer.Name, gameServer.Active, gameServer.Host, gameServer.Port);
        }

        await session.Client.SendWorldListAsync(worldList, cancellationToken);
        logger.LogDebug("[ENTRY-DIAG] World-list packet sent to {ClientIP}", session.Connection.Ip);
    }
}
