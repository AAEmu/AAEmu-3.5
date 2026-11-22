#nullable enable

using System.Net;
using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Core.Packets.L2C;
using AAEmu.Login.Models;
using AAEmu.Commons.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ISession = AAEmu.Commons.Network.Core.ISession;

namespace AAEmu.UnitTests.Login.Core.Network.Connections;

public class LoginSessionTests
{
    private static readonly AccountId s_testAccountId = new(100);
    private static readonly ConnectionId s_testConnectionId = new(42);
    private static readonly GameServerId s_testGsId = new(1);
    private static readonly IPAddress s_testIp = IPAddress.Parse("10.0.0.1");

    private readonly Mock<ILoginConnection> _mockConnection = Mock.Of<ILoginConnection>();
    private readonly Mock<IGameController> _mockGameController = Mock.Of<IGameController>();
    private readonly List<LoginPacket> _sentPackets = [];
    private readonly CancellationTokenSource _connectionClosedCts = new();
    private readonly FakeTimeProvider _fakeTimeProvider = new();
    private readonly IOptions<AppConfiguration> _appConfig;
    private readonly LoginSession _session;

    public LoginSessionTests()
    {
        _mockConnection.Id.Returns(s_testConnectionId);
        _mockConnection.Ip.Returns(s_testIp);
        _mockConnection.ConnectionClosed.Returns(_connectionClosedCts.Token);
        _mockConnection.SendPacketAsync(Any<LoginPacket>(), Any<CancellationToken>())
            .Callback((p, _) => _sentPackets.Add(p));

        _appConfig = Options.Create(new AppConfiguration
        {
            SecretKey = "test", EnterWorldTimeout = TimeSpan.FromMilliseconds(200), GameServers = []
        });

        _session = CreateSession(_mockGameController.Object);
    }

    private static Mock<IAuthenticationFlow> CreateMockFlow(AuthFlowResult result)
    {
        var mock = Mock.Of<IAuthenticationFlow>();
        mock.StartAsync(Any<ILoginClient>(), Any<CancellationToken>())
            .Returns(result);
        return mock;
    }

    private async Task AuthenticateSuccessfullyAsync(CancellationToken cancellationToken)
    {
        var flow = CreateMockFlow(new AuthFlowResult.Success(s_testAccountId, "testuser"));
        await _session.AuthenticateAsync(flow.Object, cancellationToken);
        _sentPackets.Clear();
    }

    private LoginSession CreateSession(IGameController gameController) =>
        new(
            _mockConnection.Object,
            gameController,
            _fakeTimeProvider,
            _appConfig,
            Mock.Of<ILogger<LoginSession>>().Object);

    private GameServer CreateActiveGameServer(GameServerId gsId)
    {
        var server = new GameServer(gsId, "TestServer", "127.0.0.1", 1234)
        {
            Connection = new InternalConnection(Mock.Of<ISession>().Object)
        };
        _mockGameController.GetGameServer(gsId).Returns(server);
        return server;
    }

    private sealed class MirrorGameControllerFake : IGameController
    {
        private readonly Dictionary<GameServerId, GameServer> _gameServers = [];
        private readonly Dictionary<GameServerId, GameServerId> _parentGameServers = [];

        public void AddMirror(GameServerId mirrorGsId, GameServerId parentGsId)
        {
            _parentGameServers[mirrorGsId] = parentGsId;
        }

        public void AddGameServer(GameServerId gsId, GameServer gameServer)
        {
            _gameServers[gsId] = gameServer;
        }

        public bool TryGetParentId(GameServerId gsId, out GameServerId id) =>
            _parentGameServers.TryGetValue(gsId, out id);

        public void Load()
        {
        }

        public void Add(GameServerId gsId, List<GameServerId> mirrorsId, InternalConnection connection)
        {
        }

        public void Remove(GameServerId gsId)
        {
        }

        public Task<WorldListResult> GetWorldListAsync(ILoginConnection connection) =>
            throw new NotSupportedException();

        public void SetLoad(GameServerId gsId, byte load)
        {
        }

        public GameServer? GetGameServer(GameServerId gsId) => _gameServers.GetValueOrDefault(gsId);

        public void RequestEnterWorld(AccountId accountId, ConnectionId connectionId, GameServerId gsId)
        {
        }

        public void RouteEnterWorldResponse(ConnectionId connectionId, GameServerId gsId, byte result)
        {
        }
    }

    [Test]
    public async Task AuthenticateAsync_Success_SendsJoinThenAuthResponse(CancellationToken cancellationToken)
    {
        var flow = CreateMockFlow(new AuthFlowResult.Success(s_testAccountId, "testuser"));

        await _session.AuthenticateAsync(flow.Object, cancellationToken);

        await Assert.That(_sentPackets.Count).IsEqualTo(2);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACJoinResponsePacket));
        await Assert.That(_sentPackets[1].GetType()).IsEqualTo(typeof(ACAuthResponsePacket));
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticated);
    }

    [Test]
    public async Task AuthenticateAsync_Denied_SendsLoginDeniedPacket(CancellationToken cancellationToken)
    {
        var flow = CreateMockFlow(new AuthFlowResult.Denied(LoginDeniedReason.BadAccount));

        await _session.AuthenticateAsync(flow.Object, cancellationToken);

        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACLoginDeniedPacket));
        await Assert.That(_session.State).IsEqualTo(LoginState.Connected);
    }

    [Test]
    public async Task AuthenticateAsync_FromWrongState_SendsDeniedBadResponse(CancellationToken cancellationToken)
    {
        await AuthenticateSuccessfullyAsync(cancellationToken);

        var flow = CreateMockFlow(new AuthFlowResult.Success(s_testAccountId));
        await _session.AuthenticateAsync(flow.Object, cancellationToken);

        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACLoginDeniedPacket));
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticated);
    }

    [Test]
    public async Task AuthenticateAsync_Pending_NoPacketSent(CancellationToken cancellationToken)
    {
        var flow = CreateMockFlow(new AuthFlowResult.Pending());

        await _session.AuthenticateAsync(flow.Object, cancellationToken);

        await Assert.That(_sentPackets).IsEmpty();
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticating);
    }

    [Test]
    public async Task AuthenticateAsync_Success_SetsConnectionProperties(CancellationToken cancellationToken)
    {
        // Enable property tracking so setters update the getter return values
        Mock.SetupAllProperties(_mockConnection);

        var flow = CreateMockFlow(new AuthFlowResult.Success(s_testAccountId, "testuser"));

        await _session.AuthenticateAsync(flow.Object, cancellationToken);

        await Assert.That(_mockConnection.Object.AccountId).IsEqualTo(s_testAccountId);
        await Assert.That(_mockConnection.Object.AccountName).IsEqualTo("testuser");
        await Assert.That(_mockConnection.Object.LastIp).IsEqualTo(s_testIp);
        await Assert.That(_mockConnection.Object.LastLogin).IsEqualTo(_fakeTimeProvider.GetUtcNow().DateTime);
    }

    [Test]
    public async Task ContinueAuthAsync_MatchingFlow_Success_SendsAuthPackets(CancellationToken cancellationToken)
    {
        // Start with pending flow
        var flow = Mock.Of<IAuthenticationFlow>();
        flow.StartAsync(Any<ILoginClient>(), Any<CancellationToken>())
            .Returns(new AuthFlowResult.Pending());
        await _session.AuthenticateAsync(flow.Object, cancellationToken);
        _sentPackets.Clear();

        // Continue with success
        await _session.ContinueAuthAsync<IAuthenticationFlow>(
            _ => Task.FromResult<AuthFlowResult>(new AuthFlowResult.Success(s_testAccountId, "user")),
            cancellationToken);

        await Assert.That(_sentPackets.Count).IsEqualTo(2);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACJoinResponsePacket));
        await Assert.That(_sentPackets[1].GetType()).IsEqualTo(typeof(ACAuthResponsePacket));
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticated);
    }

    [Test]
    public async Task ContinueAuthAsync_MatchingFlow_Denied_SendsDeniedPacket(CancellationToken cancellationToken)
    {
        var flow = Mock.Of<IAuthenticationFlow>();
        flow.StartAsync(Any<ILoginClient>(), Any<CancellationToken>())
            .Returns(new AuthFlowResult.Pending());
        await _session.AuthenticateAsync(flow.Object, cancellationToken);
        _sentPackets.Clear();

        await _session.ContinueAuthAsync<IAuthenticationFlow>(
            _ => Task.FromResult<AuthFlowResult>(new AuthFlowResult.Denied(LoginDeniedReason.BadAccount)),
            cancellationToken);

        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACLoginDeniedPacket));
        await Assert.That(_session.State).IsEqualTo(LoginState.Connected);
    }

    /// <summary>
    /// A distinct flow interface for type-mismatch testing.
    /// </summary>
    private interface IOtherFlow : IAuthenticationFlow;

    [Test]
    public async Task ContinueAuthAsync_WrongFlowType_SendsDeniedAndShutdown(CancellationToken cancellationToken)
    {
        // Start with a mocked IAuthenticationFlow
        var flow = Mock.Of<IAuthenticationFlow>();
        flow.StartAsync(Any<ILoginClient>(), Any<CancellationToken>())
            .Returns(new AuthFlowResult.Pending());
        await _session.AuthenticateAsync(flow.Object, cancellationToken);
        _sentPackets.Clear();

        // Continue expecting IOtherFlow = type mismatch
        await _session.ContinueAuthAsync<IOtherFlow>(
            _ => Task.FromResult<AuthFlowResult>(new AuthFlowResult.Success(s_testAccountId)),
            cancellationToken);

        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACLoginDeniedPacket));
        _mockConnection.Shutdown().WasCalled(Times.Once);
        await Assert.That(_session.State).IsEqualTo(LoginState.Disconnected);
    }

    [Test]
    public async Task ContinueAuthAsync_FromWrongState_SendsDenied(CancellationToken cancellationToken)
    {
        // Session is in Connected state (no pending auth)
        await _session.ContinueAuthAsync<IAuthenticationFlow>(
            _ => Task.FromResult<AuthFlowResult>(new AuthFlowResult.Success(s_testAccountId)),
            cancellationToken);

        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACLoginDeniedPacket));
        await Assert.That(_session.State).IsEqualTo(LoginState.Connected);
    }

    [Test]
    [Arguments(false, 0x7F000001u)]
    [Arguments(true, 0x0A000001u)]
    public async Task InitiateEnterWorld_Success_SendsOneWorldCookieEndpoint(
        bool isLocallyConnected, uint expectedAddress, CancellationToken cancellationToken)
    {
        _mockConnection.IsLocallyConnected.Returns(isLocallyConnected);
        await AuthenticateSuccessfullyAsync(cancellationToken);
        CreateActiveGameServer(s_testGsId);

        await _session.InitiateEnterWorldAsync(s_testGsId, cancellationToken);
        _session.CompleteEnterWorldRequest(s_testGsId, 0);
        await _session.EnterWorldBackgroundTask!;

        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACWorldCookiePacket));
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticated);

        var stream = _sentPackets[0].Encode();
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)30);
        await Assert.That(stream.ReadUInt16()).IsEqualTo(LCOffsets.ACWorldCookiePacket);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(s_testConnectionId.Value);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(expectedAddress);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)1234);
        for (var i = 0; i < 3; i++)
        {
            await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
            await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)0);
        }
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task InitiateEnterWorld_MirrorServer_ResolvesToParent(CancellationToken cancellationToken)
    {
        var parentGsId = new GameServerId(10);
        var mirrorGsId = new GameServerId(11);
        var gameController = new MirrorGameControllerFake();
        var parentServer = new GameServer(parentGsId, "ParentServer", "127.0.0.1", 1234)
        {
            Connection = new InternalConnection(Mock.Of<ISession>().Object)
        };
        var mirrorServer = new GameServer(mirrorGsId, "MirrorServer", "127.0.0.2", 4321)
        {
            Connection = new InternalConnection(Mock.Of<ISession>().Object)
        };
        gameController.AddGameServer(parentGsId, parentServer);
        gameController.AddGameServer(mirrorGsId, mirrorServer);
        gameController.AddMirror(mirrorGsId, parentGsId);

        var session = CreateSession(gameController);
        var flow = CreateMockFlow(new AuthFlowResult.Success(s_testAccountId, "testuser"));
        await session.AuthenticateAsync(flow.Object, cancellationToken);
        _sentPackets.Clear();

        await session.InitiateEnterWorldAsync(mirrorGsId, cancellationToken);
        session.CompleteEnterWorldRequest(parentGsId, 0);
        await session.EnterWorldBackgroundTask!;

        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACWorldCookiePacket));
        await Assert.That(session.State).IsEqualTo(LoginState.Authenticated);
        // In the 3.5 flow the game server replies with its own (parent) id,
        // so the client receives the parent server's address/port.
        await Assert.That(ReadWorldCookiePort(_sentPackets[0])).IsEqualTo((ushort)1234);
    }

    private static ushort ReadWorldCookiePort(LoginPacket packet)
    {
        var stream = packet.Encode();
        stream.ReadUInt16();
        stream.ReadUInt16();
        stream.ReadUInt32();
        stream.ReadUInt32();
        return stream.ReadUInt16();
    }

    [Test]
    public async Task InitiateEnterWorld_Failure_SendsEnterWorldDeniedPacket(CancellationToken cancellationToken)
    {
        await AuthenticateSuccessfullyAsync(cancellationToken);
        CreateActiveGameServer(s_testGsId);

        await _session.InitiateEnterWorldAsync(s_testGsId, cancellationToken);
        _session.CompleteEnterWorldRequest(s_testGsId, 1);
        await _session.EnterWorldBackgroundTask!;

        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACEnterWorldDeniedPacket));
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticated);
    }

    [Test]
    public async Task InitiateEnterWorld_Timeout_SendsEnterWorldDenied(CancellationToken cancellationToken)
    {
        await AuthenticateSuccessfullyAsync(cancellationToken);
        CreateActiveGameServer(s_testGsId);

        await _session.InitiateEnterWorldAsync(s_testGsId, cancellationToken);

        // Advance past the 200ms timeout
        _fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(300));
        await _session.EnterWorldBackgroundTask!;

        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACEnterWorldDeniedPacket));
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticated);
    }

    [Test]
    public async Task InitiateEnterWorld_FromWrongState_NoAction(CancellationToken cancellationToken)
    {
        // Session is in Connected state
        await _session.InitiateEnterWorldAsync(s_testGsId, cancellationToken);

        await Assert.That(_sentPackets).IsEmpty();
        await Assert.That(_session.State).IsEqualTo(LoginState.Connected);
    }

    [Test]
    public async Task InitiateEnterWorld_ServerNotActive_NoAction(CancellationToken cancellationToken)
    {
        await AuthenticateSuccessfullyAsync(cancellationToken);

        // Server with no connection (not active)
        var server = new GameServer(s_testGsId, "TestServer", "127.0.0.1", 1234);
        _mockGameController.GetGameServer(s_testGsId).Returns(server);

        await _session.InitiateEnterWorldAsync(s_testGsId, cancellationToken);

        await Assert.That(_sentPackets).IsEmpty();
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticated);
    }

    [Test]
    public async Task CompleteEnterWorld_GsIdMismatch_Ignored(CancellationToken cancellationToken)
    {
        await AuthenticateSuccessfullyAsync(cancellationToken);
        CreateActiveGameServer(s_testGsId);

        await _session.InitiateEnterWorldAsync(s_testGsId, cancellationToken);

        // Complete with a different gsId
        var wrongGsId = new GameServerId(99);
        _session.CompleteEnterWorldRequest(wrongGsId, 0);
        await Assert.That(_sentPackets).IsEmpty();

        // Clean up: cancel to avoid timeout firing later
        _session.CancelEnterWorld();
        await _session.EnterWorldBackgroundTask!;
    }

    [Test]
    public async Task CancelEnterWorld_WhileEntering_CancelsOperation(CancellationToken cancellationToken)
    {
        await AuthenticateSuccessfullyAsync(cancellationToken);
        CreateActiveGameServer(s_testGsId);

        await _session.InitiateEnterWorldAsync(s_testGsId, cancellationToken);
        _session.CancelEnterWorld();
        await _session.EnterWorldBackgroundTask!;

        await Assert.That(_sentPackets).IsEmpty();
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticated);
    }

    [Test]
    public async Task CancelEnterWorld_NotEntering_Ignored()
    {
        await AuthenticateSuccessfullyAsync(CancellationToken.None);

        _session.CancelEnterWorld();

        await Assert.That(_sentPackets).IsEmpty();
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticated);
    }

    [Test]
    public async Task DisconnectAsync_CancelsPendingEnterWorld(CancellationToken cancellationToken)
    {
        await AuthenticateSuccessfullyAsync(cancellationToken);
        CreateActiveGameServer(s_testGsId);

        await _session.InitiateEnterWorldAsync(s_testGsId, cancellationToken);
        await _session.DisconnectAsync();

        // Should not send any packets since disconnect cancels before result
        await Assert.That(_sentPackets).IsEmpty();
        await Assert.That(_session.State).IsEqualTo(LoginState.Disconnected);
    }

    [Test]
    public async Task DisconnectAsync_FromAuthenticated_SetsDisconnected(CancellationToken cancellationToken)
    {
        await AuthenticateSuccessfullyAsync(cancellationToken);

        await _session.DisconnectAsync();

        await Assert.That(_sentPackets).IsEmpty();
        await Assert.That(_session.State).IsEqualTo(LoginState.Disconnected);
    }

    [Test]
    public async Task DisconnectAsync_AlreadyDisconnected_Idempotent()
    {
        await _session.DisconnectAsync();
        await _session.DisconnectAsync();

        await Assert.That(_sentPackets).IsEmpty();
        await Assert.That(_session.State).IsEqualTo(LoginState.Disconnected);
    }
}
