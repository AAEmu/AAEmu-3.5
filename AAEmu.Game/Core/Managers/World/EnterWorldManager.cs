using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Login;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Packets.G2L;
using AAEmu.Game.Core.Packets.Proxy;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.StaticValues;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

public class EnterWorldManager(
    IAccountManager accountManager,
    IStreamManager streamManager,
    IQuestManager questManager,
    IChatManager chatManager,
    IFamilyManager familyManager,
    IWorldManager worldManager) : Singleton<EnterWorldManager>, IEnterWorldManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// List of connected accounts (connection token, accountId)
    /// </summary>
    private readonly Dictionary<uint, ulong> _accounts = [];
    private readonly object _accountsLock = new();

    /// <summary>
    /// Adds an account to the connection list and notifies the login server the client is connected
    /// </summary>
    /// <param name="accountId"></param>
    /// <param name="connectionId"></param>
    public void AddAccount(ulong accountId, uint connectionId)
    {
        var connection = LoginNetwork.Instance.GetConnection();
        var gsId = AppConfiguration.Instance.Id;
        var accountAlreadyActive = accountManager.Contains((uint)accountId);

        Logger.Debug("[ENTRY-DIAG] Login handoff received for account {0} on game server {1}: account already active={2}",
            accountId, gsId, accountAlreadyActive);

        if (accountAlreadyActive)
        {
            lock (_accountsLock)
                _accounts.Remove(connectionId);
            connection.SendPacket(new GLPlayerEnterPacket(connectionId, gsId, 1));
        }
        else
        {
            // Login connection IDs are leased and reused. A client can disconnect after this
            // packet but before X2EnterWorld consumes the token, leaving an entry for that ID.
            // Replace that stale token when the login server reuses the ID.
            lock (_accountsLock)
                _accounts[connectionId] = accountId;
            connection.SendPacket(new GLPlayerEnterPacket(connectionId, gsId, 0));
        }
    }

    /// <summary>
    /// Performs an enter world event and places the connection in lobby state.
    /// Also notifies the stream server that a new connection has been made
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="accountId"></param>
    /// <param name="token"></param>
    public void Login(GameConnection connection, ulong accountId, uint token)
    {
        ulong account;
        bool found;
        lock (_accountsLock)
        {
            found = _accounts.TryGetValue(token, out account);
            if (found && account == accountId)
                _accounts.Remove(token);
        }

        Logger.Debug("[ENTRY-DIAG] X2EnterWorld validation from {0}: authorization token found={1}, account matches={2}",
            connection.Ip, found, found && account == accountId);

        if (found)
        {
            if (account == accountId)
            {
                connection.AccountId = (uint)accountId;
                connection.State = GameState.Lobby;

                accountManager.Add(connection);
                streamManager.AddToken(connection.AccountId, connection.Id);

                var port = AppConfiguration.Instance.StreamNetwork.Port;
                var gm = connection.GetAttribute("gmFlag") != null;
                Logger.Debug("[ENTRY-DIAG] X2EnterWorld accepted from {0}; sending response with stream port {1}",
                    connection.Ip, port);
                connection.SendPacket(new X2EnterWorldResponsePacket(0, gm, connection.Id, port, connection));
                connection.SendPacket(new ChangeStatePacket(0));
                Logger.Debug("[ENTRY-DIAG] X2EnterWorld response and lobby state sent to {0}", connection.Ip);
            }
            else
            {
                // TODO: Token did not match the expected account (phishing attempt?)
                Logger.Warn("[ENTRY-DIAG] X2EnterWorld rejected from {0}: authorization token belongs to a different account (account={1})",
                    connection.Ip, accountId);
                connection.Shutdown();
            }
        }
        else
        {
            // TODO: Invalid token (hacking attempt?)
            Logger.Warn("[ENTRY-DIAG] X2EnterWorld rejected from {0}: authorization token not found (account={1})",
                connection.Ip, accountId);
            connection.Shutdown();
        }
    }

    /// <summary>
    /// Start a leave world task
    /// Delay is also calculated
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="leaveWorldTargetType"></param>
    public void Leave(GameConnection connection, LeaveWorldTargetType leaveWorldTargetType)
    {
        switch (leaveWorldTargetType)
        {
            case LeaveWorldTargetType.QuitGame: // выход из игры, quit game
            case LeaveWorldTargetType.CharacterSelect: // выход к списку персонажей, go to character select
                if (connection.State == GameState.World)
                {

                    if (connection.LeaveTask != null)
                    {
                        break;
                    }

                    // Say goodbye if player is quitting (but not going to character select)
                    if (leaveWorldTargetType == 0)
                        connection.ActiveChar?.SendMessage(ChatType.System, AppConfiguration.Instance.World.LogoutMessage);

                    var logoutTime = 10000; // in ms

                    // Make it 5 minutes if you're still in combat
                    if (connection.ActiveChar?.IsInBattle ?? false)
                        logoutTime *= 30;

                    // Add 10 minutes if you have a Slave Active
                    if (connection.ActiveChar?.ParentWorld?.SlaveManager.GetActiveSlaveByOwnerObjId(connection.ActiveChar.ObjId) != null)
                        logoutTime += 1000 * 60 * 10;

                    connection.SendPacket(new SCPrepareLeaveWorldPacket(logoutTime, leaveWorldTargetType, false));

                    connection.CancelTokenSource = new CancellationTokenSource();
                    var token = connection.CancelTokenSource.Token;
                    connection.LeaveTask = Task.Run(async () =>
                    {
                        await Task.Delay(logoutTime, token);
                        Instance.LeaveWorldTask(connection, leaveWorldTargetType, connection.ActiveChar);
                    }, token);
                }

                break;
            case LeaveWorldTargetType.ServerSelect: // выбор сервера, server select
                if (connection.State == GameState.Lobby)
                {
                    var gsId = AppConfiguration.Instance.Id;
                    LoginNetwork
                        .Instance
                        .GetConnection()
                        .SendPacket(new GLPlayerReconnectPacket(gsId, connection.AccountId, connection.Id));
                }

                break;
            default:
                Logger.Warn($"[Leave] Unknown type: {leaveWorldTargetType}");
                break;
        }
    }

    /// <summary>
    /// Actually leave the game world and return connection to lobby state
    /// Also despawns all owned mounts/pet/vehicles still in the world
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="leaveWorldTarget"></param>
    /// <param name="activeChar"></param>
    public void LeaveWorldTask(GameConnection connection, LeaveWorldTargetType leaveWorldTarget, Character activeChar)
    {
        if (activeChar != null)
        {
            activeChar.DisabledSetPosition = true;
            activeChar.IsOnline = false;
            activeChar.LeaveTime = DateTime.UtcNow;

            // Remove all remaining quest timer tasks
            questManager.RemoveQuestTimer(activeChar.Id, 0);

            // Despawn and unmount everybody from owned Mates
            activeChar.ParentWorld.MateManager.RemoveAndDespawnAllActiveOwnedMates(activeChar);
            activeChar.ParentWorld.SlaveManager.RemoveAndDespawnAllActiveOwnedSlaves(activeChar);

            // Check if still mounted on somebody else's mount and dismount that if needed
            activeChar.ForceDismount(/*AttachUnitReason.PrefabChanged*/); // Dismounting a mount because of unsummoning sends "10" for this

            // NOTE: do NOT MemberRemoveFromTeam here. Setting IsOnline = false above
            // already routed through TeamManager.SetOffline, which broadcasts
            // SCTeamMemberDisconnectedPacket so team-mates see the player as offline
            // while the TeamMember entry stays in Team.Members[] for a clean reconnect
            // via UpdateAtLogin. Calling MemberRemoveFromTeam here used to immediately
            // overwrite the offline indicator with a "left the team" broadcast and
            // wipe the slot — which is what made every disconnect look like a kick.

            // Remove from all Chat
            chatManager.LeaveAllChannels(activeChar);

            // Handle Family
            if (activeChar.Family > 0)
                familyManager.OnCharacterLogout(activeChar);

            // Handle Guild
            activeChar.Expedition?.OnCharacterLogout(activeChar);

            // Remove player from world (hides and release Id)
            activeChar.Delete();
            // ObjectIdManager.Instance.ReleaseId(activeChar.ObjId);

            // Cancel auto-regen
            //activeChar.StopRegen();

            // Clear Buyback table
            activeChar.BuyBackItems.Wipe();

            // Remove subscribers
            foreach (var subscriber in activeChar.Subscribers)
                subscriber.Dispose();

            // Remove from server
            worldManager.TryRemoveCharacter(activeChar.ObjId);
        }

        GameConnection.SaveAndRemoveFromWorld(activeChar);

        // connection isn't set if we are removing an orphaned Character object
        if (connection != null)
        {
            connection.State = GameState.Lobby;
            connection.LeaveTask = null;

            // The client must see the old Stream disconnect before it opens Stream again in the lobby.
            if (leaveWorldTarget == LeaveWorldTargetType.CharacterSelect)
                connection.CloseStream();

            connection.SendPacket(new SCLeaveWorldGrantedPacket(leaveWorldTarget));
            connection.SendPacket(new ChangeStatePacket(0));
        }
    }
}
