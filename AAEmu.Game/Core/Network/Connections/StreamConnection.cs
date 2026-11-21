using System.Net;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Network.Connections;

public class StreamConnection(ISession session)
{
    private readonly Lock _bindingLock = new();
    private GameConnection _gameConnection;
    private bool _disconnected;
    private int _requestId = -1;
    private int _receiveChunkCount;
    private int _receivedPacketCount;
    private readonly Dictionary<int, Doodad[]> _requests = [];

    public uint Id => session.SessionId;
    public IPAddress Ip => session.Ip;
    public GameConnection GameConnection => Volatile.Read(ref _gameConnection);
    public PacketStream LastPacket { get; set; }

    internal bool TryBind(GameConnection gameConnection)
    {
        lock (_bindingLock)
        {
            if (_disconnected || (_gameConnection != null && _gameConnection != gameConnection))
                return false;

            Volatile.Write(ref _gameConnection, gameConnection);
            return true;
        }
    }

    public int IncrementReceiveChunkCount() => Interlocked.Increment(ref _receiveChunkCount);

    public int IncrementReceivedPacketCount() => Interlocked.Increment(ref _receivedPacketCount);

    public int GetNextRequestId(Doodad[] doodads)
    {
        Interlocked.Increment(ref _requestId);
        _requests.Add(_requestId, doodads);
        return _requestId;
    }

    public Doodad[] GetRequest(int requestId)
    {
        if (_requests.TryGetValue(requestId, out var request))
            return request;
        return null;
    }

    public void RemoveRequest(int requestId)
    {
        _requests.Remove(requestId);
    }

    public void SendPacket(StreamPacket packet)
    {
        SendPacket(packet.Encode());
    }

    public void SendPacket(byte[] packet)
    {
        session?.SendPacket(packet);
    }

    public static void OnConnect()
    {
    }

    public void Shutdown()
    {
        if (TryDisconnect())
            session?.Close();
    }

    internal void OnDisconnect() => TryDisconnect();

    private bool TryDisconnect()
    {
        GameConnection gameConnection;
        lock (_bindingLock)
        {
            if (_disconnected)
                return false;

            _disconnected = true;
            gameConnection = _gameConnection;
            Volatile.Write(ref _gameConnection, null);
        }

        gameConnection?.DetachStream(this);
        return true;
    }
}
