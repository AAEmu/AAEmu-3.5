using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NetCoreServer;
using NLog;

namespace AAEmu.Commons.Network.Core;

public interface ISession
{
    IPAddress Ip { get; }
    uint SessionId { get; }
    Socket Socket { get; }
    void SendPacket(byte[] packet);
    void AddAttribute(string name, object attribute);
    object GetAttribute(string name);
    void ClearAttribute(string name);
    void Close();
}

public class Session : TcpSession, ISession
{
    private static readonly Logger s_logger = LogManager.GetCurrentClassLogger();
    private readonly Dictionary<string, object> _attributes = [];
    private long _connectedTimestamp;
    private long _lastReceiveTimestamp;
    private long _lastSendTimestamp;
    private int _closeRequested;
    private int _lastSocketError;

    public IBaseProtocolHandler ProtocolHandler { get; private set; }
    public IPEndPoint RemoteEndPoint { get; private set; }
    public uint SessionId { get; private set; }
    public IPAddress Ip { get; private set; }

    public Session(Server server) : base(server)
    {
        ProtocolHandler = server.GetHandler();
    }

    protected override void OnConnecting()
    {
        RemoteEndPoint = (IPEndPoint)Socket.RemoteEndPoint;
        SessionId = (uint)RemoteEndPoint.GetHashCode();
        Ip = RemoteEndPoint.Address;
        _connectedTimestamp = Stopwatch.GetTimestamp();
        ProtocolHandler?.OnConnect(this);
    }

    protected override void OnConnected()
    {
        // Moved to OnConnecting due to a bug in TcpSession where OnReceived can happen before OnConnected.
        //_remoteEndPoint = (IPEndPoint)Socket.RemoteEndPoint;
        //_sessionId = (uint)RemoteEndPoint.GetHashCode();
        //_ip = RemoteEndPoint.Address;
        //ProtocolHandler?.OnConnect(this);
    }

    protected override void OnDisconnected()
    {
        var lastReceive = Interlocked.Read(ref _lastReceiveTimestamp);
        var lastSend = Interlocked.Read(ref _lastSendTimestamp);
        s_logger.Info(
            "[NET-DIAG] TCP disconnected: channel={Channel}, session={SessionId}, transport={TransportId}, remote={Remote}, localCloseRequested={LocalCloseRequested}, lastReportedSocketError={SocketError}, connectedSeconds={ConnectedSeconds:F3}, receiveIdleSeconds={ReceiveIdleSeconds:F3}, sendIdleSeconds={SendIdleSeconds:F3}",
            ProtocolHandler?.GetType().Name, SessionId, Id, RemoteEndPoint,
            Volatile.Read(ref _closeRequested) != 0, (SocketError)Volatile.Read(ref _lastSocketError),
            Stopwatch.GetElapsedTime(_connectedTimestamp).TotalSeconds,
            lastReceive == 0 ? -1 : Stopwatch.GetElapsedTime(lastReceive).TotalSeconds,
            lastSend == 0 ? -1 : Stopwatch.GetElapsedTime(lastSend).TotalSeconds);
        ProtocolHandler?.OnDisconnect(this);
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
        Interlocked.Exchange(ref _lastReceiveTimestamp, Stopwatch.GetTimestamp());
        ProtocolHandler?.OnReceive(this, buffer, (int)offset, (int)size);
    }

    protected override void OnSent(long sent, long pending)
    {
        Interlocked.Exchange(ref _lastSendTimestamp, Stopwatch.GetTimestamp());
    }

    protected override void OnError(SocketError error)
    {
        Interlocked.Exchange(ref _lastSocketError, (int)error);
        s_logger.Warn("[NET-DIAG] TCP socket error: channel={Channel}, session={SessionId}, transport={TransportId}, remote={Remote}, error={SocketError}",
            ProtocolHandler?.GetType().Name, SessionId, Id, RemoteEndPoint, error);
    }

    public virtual void SendMessage(PacketStream message)
    {
        // var stream = new PacketStream();
        // message.Write(stream);
        SendAsync(message);
    }

    public override bool SendAsync(byte[] buffer)
    {
        // TODO send to queue
        return SendAsync(buffer, 0L, buffer.Length);
    }

    public void AddAttribute(string name, object attribute)
    {
        _attributes.Add(name, attribute);
    }

    public object GetAttribute(string name)
    {
        _attributes.TryGetValue(name, out var attribute);
        return attribute;
    }

    public void ClearAttribute(string name)
    {
        _attributes.Remove(name);
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closeRequested, 1) == 0 && s_logger.IsInfoEnabled)
        {
            s_logger.Info("[NET-DIAG] Server requested TCP close: channel={Channel}, session={SessionId}, transport={TransportId}, remote={Remote}, caller={Caller}",
                ProtocolHandler?.GetType().Name, SessionId, Id, RemoteEndPoint, new StackTrace(1, false).ToString());
        }
        Disconnect();
    }

    public void SendPacket(byte[] packet)
    {
        SendAsync(packet);
    }
}
