using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Models;
using Microsoft.Extensions.Options;

namespace AAEmu.Login.Core.Network.Connections;

/// <summary>
/// Manages the login flow state machine for a client connection.
/// The instance members of this class are thread-safe.
/// </summary>
public sealed class LoginSession(ILoginConnection connection, IGameController gameController, TimeProvider timeProvider, IOptions<AppConfiguration> appConfig, ILogger<LoginSession> logger) : ILoginSession
{
    private readonly TimeSpan _enterWorldTimeout = appConfig.Value.EnterWorldTimeout;
    private readonly Lock _lock = new();

    private LoginState _state = LoginState.Connected;
    private IAuthenticationFlow? _currentAuthFlow;
    private CancellationTokenSource? _enterWorldCts;
    private TaskCompletionSource<EnterWorldResult>? _enterWorldTcs;
    private GameServerId? _pendingGsId;
    private Task? _enterWorldBackgroundTask;

    internal Task? EnterWorldBackgroundTask => _enterWorldBackgroundTask;

    public ILoginConnection Connection { get; } = connection;

    public ILoginClient Client { get; } = new LoginClient(connection);

    public LoginState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    public IAuthenticationFlow? CurrentAuthFlow
    {
        get
        {
            lock (_lock)
            {
                return _currentAuthFlow;
            }
        }
    }

    public ValueTask SendPacketAsync(LoginPacket packet, CancellationToken cancellationToken) =>
        Connection.SendPacketAsync(packet, cancellationToken);

    public async Task AuthenticateAsync(IAuthenticationFlow flow, CancellationToken cancellationToken)
    {
        bool canProceed;

        lock (_lock)
        {
            if (_state != LoginState.Connected)
            {
                logger.LogWarning("Cannot authenticate: invalid state {State} for connection {ConnectionId}", _state, Connection.Id);
                canProceed = false;
            }
            else
            {
                _state = LoginState.Authenticating;
                canProceed = true;
            }
        }

        if (!canProceed)
        {
            await SendAuthResultAsync(new AuthFlowResult.Denied(LoginDeniedReason.BadResponse), cancellationToken);
            return;
        }

        var result = await flow.StartAsync(Client, cancellationToken);
        ProcessAuthResult(result, flow);
        await SendAuthResultAsync(result, cancellationToken);
    }

    public async Task ContinueAuthAsync<TFlow>(Func<TFlow, Task<AuthFlowResult>> step, CancellationToken cancellationToken) where TFlow : class, IAuthenticationFlow
    {
        TFlow? flow;
        LoginDeniedReason? earlyDenial = null;

        lock (_lock)
        {
            if (_state != LoginState.Authenticating)
            {
                logger.LogWarning("Cannot continue auth: invalid state {State} for connection {ConnectionId}", _state, Connection.Id);
                flow = null;
                earlyDenial = LoginDeniedReason.BadResponse;
            }
            else if (_currentAuthFlow is not TFlow typedFlow)
            {
                logger.LogWarning("Auth flow type mismatch: expected {Expected}, got {Actual} for connection {ConnectionId}", typeof(TFlow).Name, _currentAuthFlow?.GetType().Name ?? "null", Connection.Id);
                _currentAuthFlow = null;
                _state = LoginState.Disconnected;
                flow = null;
                earlyDenial = LoginDeniedReason.BadResponse;
                Connection.Shutdown();
            }
            else
            {
                flow = typedFlow;
            }
        }

        if (earlyDenial is not null)
        {
            await SendAuthResultAsync(new AuthFlowResult.Denied(earlyDenial.Value), cancellationToken);
            return;
        }

        var result = await step(flow!);
        ProcessAuthResult(result, flow!);
        await SendAuthResultAsync(result, cancellationToken);
    }

    private async Task SendAuthResultAsync(AuthFlowResult result, CancellationToken cancellationToken)
    {
        switch (result)
        {
            case AuthFlowResult.Success success:
                await Client.SendAuthSuccessAsync(success.AccountId, cancellationToken);
                break;

            case AuthFlowResult.Denied denied:
                await Client.SendAuthDeniedAsync(denied.Reason, cancellationToken);
                break;

                // Pending: flow is managing itself, nothing to send
        }
    }

    private void ProcessAuthResult(AuthFlowResult result, IAuthenticationFlow flow)
    {
        lock (_lock)
        {
            switch (result)
            {
                case AuthFlowResult.Success success:
                    Connection.AccountId = success.AccountId;
                    if (success.Username is not null)
                        Connection.AccountName = success.Username;
                    Connection.LastLogin = timeProvider.GetUtcNow().DateTime;
                    Connection.LastIp = Connection.Ip;
                    _currentAuthFlow = null;
                    _state = LoginState.Authenticated;
                    break;

                case AuthFlowResult.Denied:
                    _currentAuthFlow = null;
                    _state = LoginState.Connected;
                    break;

                case AuthFlowResult.Pending:
                    _currentAuthFlow = flow;
                    // Stay in Authenticating
                    break;
            }
        }
    }

    public async Task InitiateEnterWorldAsync(GameServerId gsId, CancellationToken cancellationToken)
    {
        logger.LogDebug("[ENTRY-DIAG] Enter-world flow started for client {ClientIP}, game server {GameServerId}, state {LoginState}",
            Connection.Ip, gsId, State);

        CancellationTokenSource? linkedCts = null;
        TaskCompletionSource<EnterWorldResult>? tcs = null;
        var expectedGsId = default(GameServerId);
        var gameServerAvailable = gameController.GetGameServer(gsId) is { Active: true };

        lock (_lock)
        {
            if (_state != LoginState.Authenticated)
            {
                logger.LogWarning("[ENTRY-DIAG] Cannot enter world: invalid state {State} for client {ClientIP}", _state, Connection.Ip);
                return;
            }

            if (gameServerAvailable)
            {
                _state = LoginState.EnteringWorld;

                expectedGsId = gsId;
                if (gameController.TryGetParentId(gsId, out var parentGsId))
                {
                    logger.LogDebug("[ENTRY-DIAG] Mirror game server {MirrorGsId} resolved to parent {ParentGsId} for client {ClientIP}", gsId, parentGsId, Connection.Ip);
                    expectedGsId = parentGsId;
                }

                _pendingGsId = expectedGsId;

                _enterWorldCts = CancellationTokenSource.CreateLinkedTokenSource(Connection.ConnectionClosed, cancellationToken);
                _enterWorldTcs = new TaskCompletionSource<EnterWorldResult>(TaskCreationOptions.RunContinuationsAsynchronously);

                linkedCts = _enterWorldCts;
                tcs = _enterWorldTcs;
            }
        }

        if (!gameServerAvailable)
        {
            logger.LogWarning("[ENTRY-DIAG] Cannot enter world: game server {GsId} not available for client {ClientIP}", gsId, Connection.Ip);
            await Client.SendEnterWorldDeniedAsync(0, cancellationToken);
            return;
        }

        // Send request to the resolved (parent) game server (outside the lock)
        logger.LogDebug("[ENTRY-DIAG] Forwarding enter-world request for client {ClientIP} to game server {GameServerId}",
            Connection.Ip, expectedGsId);
        gameController.RequestEnterWorld(Connection.AccountId, Connection.Id, expectedGsId);

        // Spawn background task to await response and send result
        _enterWorldBackgroundTask = RunEnterWorldBackgroundAsync(tcs!, linkedCts!);
    }

    private async Task RunEnterWorldBackgroundAsync(TaskCompletionSource<EnterWorldResult> tcs,
        CancellationTokenSource linkedCts)
    {
        try
        {
            var result = await tcs.Task.WaitAsync(_enterWorldTimeout, timeProvider, linkedCts.Token);

            if (result is { Success: true, Server: not null })
            {
                var serverIp = result.Server.Host;
                if (Connection.IsLocallyConnected)
                    serverIp = Connection.Ip.ToString();
                logger.LogInformation("[ENTRY-DIAG] Game server {GameServerId} authorized client {ClientIP}; sending world endpoint {GameServerIP}:{GameServerPort}",
                    result.Server.Id, Connection.Ip, serverIp, result.Server.Port);
                await Client.SendWorldCookieAsync(result.Server, CancellationToken.None);
                logger.LogDebug("[ENTRY-DIAG] World-cookie packet sent to {ClientIP} for game server {GameServerId}, endpoint {GameServerIP}:{GameServerPort}",
                    Connection.Ip, result.Server.Id, serverIp, result.Server.Port);
            }
            else
            {
                logger.LogWarning("[ENTRY-DIAG] Game server denied entry for client {ClientIP}, result {Result}",
                    Connection.Ip, result.DenialReason);
                await Client.SendEnterWorldDeniedAsync(result.DenialReason, CancellationToken.None);
                logger.LogDebug("[ENTRY-DIAG] Enter-world denial packet sent to {ClientIP}, reason={Reason}",
                    Connection.Ip, result.DenialReason);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("[ENTRY-DIAG] Enter-world wait cancelled for client {ClientIP}; connection closed={ConnectionClosed}",
                Connection.Ip, Connection.ConnectionClosed.IsCancellationRequested);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("[ENTRY-DIAG] Enter-world wait timed out for client {ClientIP}", Connection.Ip);
            try
            {
                await Client.SendEnterWorldDeniedAsync(0, CancellationToken.None);
            }
            catch
            {
                // Connection may be closed
            }
        }
        finally
        {
            lock (_lock)
            {
                CleanupEnterWorldState();
                if (_state == LoginState.EnteringWorld)
                {
                    _state = LoginState.Authenticated;
                }
            }
            logger.LogDebug("[ENTRY-DIAG] Enter-world flow finished for client {ClientIP}, state {LoginState}", Connection.Ip, State);
        }
    }

    public void CancelEnterWorld()
    {
        lock (_lock)
        {
            if (_state != LoginState.EnteringWorld)
            {
                logger.LogDebug("[ENTRY-DIAG] CancelEnterWorld ignored: client {ClientIP} is not in EnteringWorld state",
                    Connection.Ip);
                return;
            }

            logger.LogDebug("[ENTRY-DIAG] Cancelling EnterWorld for client {ClientIP}", Connection.Ip);

            // Cancel the CTS to trigger the awaiting task
            _enterWorldCts?.Cancel();
        }
    }

    public void CompleteEnterWorldRequest(GameServerId gsId, byte result)
    {
        lock (_lock)
        {
            if (_state != LoginState.EnteringWorld)
            {
                logger.LogWarning(
                    "[ENTRY-DIAG] Enter-world response ignored: session state is {State} for client {ClientIP}",
                    _state, Connection.Ip);
                return;
            }

            if (_pendingGsId != gsId)
            {
                logger.LogWarning(
                    "[ENTRY-DIAG] Enter-world response ignored: game server mismatch (expected {Expected}, got {Actual}) for client {ClientIP}",
                    _pendingGsId, gsId, Connection.Ip);
                return;
            }

            var success = result == 0;
            GameServer? server = null;

            if (success)
            {
                server = gameController.GetGameServer(gsId);
            }

            logger.LogDebug("[ENTRY-DIAG] Enter-world response received from game server {GameServerId}: success={Success}, result={Result}",
                gsId, success, result);
            _enterWorldTcs?.TrySetResult(new EnterWorldResult(success, server, result));
        }
    }

    public async Task DisconnectAsync()
    {
        Task? backgroundTask;

        lock (_lock)
        {
            if (_state == LoginState.Disconnected)
                return;

            logger.LogDebug("[ENTRY-DIAG] Session disconnected for client {ClientIP}", Connection.Ip);

            // Cancel any pending enter world operation
            _enterWorldCts?.Cancel();
            CleanupEnterWorldState();

            _state = LoginState.Disconnected;
            backgroundTask = _enterWorldBackgroundTask;
        }

        if (backgroundTask is not null)
        {
            // Ignore errors during cleanup
            await backgroundTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private void CleanupEnterWorldState()
    {
        // Must be called while holding _lock
        _enterWorldCts?.Dispose();
        _enterWorldCts = null;
        _enterWorldTcs = null;
        _pendingGsId = null;
    }
}
