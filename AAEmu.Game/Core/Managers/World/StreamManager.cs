using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.S2C;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Managers.World;

public class StreamManager : Singleton<StreamManager>, IStreamManager
{
    private readonly Dictionary<uint, ulong> _accounts = [];
    private readonly Lock _accountsLock = new();

    public static void Load()
    {
        // TODO ...
    }

    public void AddToken(ulong accountId, uint connectionId)
    {
        lock (_accountsLock)
            _accounts.Add(connectionId, accountId);
    }

    public void RemoveToken(uint token)
    {
        lock (_accountsLock)
            _accounts.Remove(token);
    }

    public void Login(StreamConnection connection, ulong accountId, uint token)
    {
        bool authorized;
        lock (_accountsLock)
        {
            authorized = _accounts.TryGetValue(token, out var expectedAccount) && expectedAccount == accountId;
            if (!authorized)
                _accounts.Remove(token);
        }

        var gameConnection = authorized ? GameConnectionTable.Instance.GetConnection(token) : null;
        var attached = gameConnection != null && gameConnection.AccountId == accountId && gameConnection.TryAttachStream(connection);
        connection.SendPacket(new TCJoinResponsePacket(attached ? (byte)0 : (byte)1));
    }

    public static void RequestCell(StreamConnection connection, uint instanceId, int x, int y)
    {
        if (connection is not null)
        {
            var worldInstanceId = connection.GameConnection?.ActiveChar?.Transform?.InstanceId ?? WorldManager.DefaultInstanceId;
            if (worldInstanceId != instanceId)
            {
                // Trying to grab cell info of a instance the player is not inside of
            }
            var world = WorldManager.Instance.GetWorld(instanceId);
            // TODO: Handle requests for instances correctly ?
            var doodads = world.GetInCell<Doodad>(x, y).ToArray();
            var requestId = connection.GetNextRequestId(doodads);
            var count = Math.Min(doodads.Length, 30);
            var res = new Doodad[count];
            Array.Copy(doodads, 0, res, 0, count);
            connection.SendPacket(new TCDoodadStreamPacket(requestId, count, res));
        }
    }

    public static void ContinueCell(StreamConnection connection, int requestId, int next)
    {
        var doodads = connection.GetRequest(requestId);
        if (doodads == null)
            return;
        if (next >= doodads.Length)
            connection.RemoveRequest(requestId);
        var count = Math.Min(doodads.Length - next, 30);
        var res = new Doodad[count];
        Array.Copy(doodads, next > 0 ? next - 1 : 0, res, 0, count);
        next += count;
        connection.SendPacket(new TCDoodadStreamPacket(requestId, next, res));
    }
}
