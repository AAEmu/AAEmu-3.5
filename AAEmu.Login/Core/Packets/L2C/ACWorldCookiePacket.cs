using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// A packet sent by the login server to the client containing the world cookie and game server connection details.
/// </summary>
/// <param name="connection">The client connection.</param>
/// <param name="gs">The game server the client wishes to connect to.</param>
public class ACWorldCookiePacket(ILoginConnection connection, GameServer gs)
    : LoginPacket(LCOffsets.ACWorldCookiePacket)
{
    private readonly uint _cookie = connection.Id.Value;

    public override PacketStream Write(PacketStream stream)
    {
        var serverIp = gs.Host;
        if (connection.IsLocallyConnected)
            serverIp = connection.Ip.ToString();

        stream.Write(_cookie);
        stream.Write(Helpers.ConvertIp(serverIp));
        stream.Write(gs.Port);

        // Empty slots are not considered gateway candidates by the client.
        for (var i = 0; i < 3; i++)
        {
            stream.Write(0u);
            stream.Write((ushort)0);
        }

        return stream;
    }
}
