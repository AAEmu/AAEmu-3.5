using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Account;
using AAEmu.Game.Models.Game;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public partial class AccountManager
{
    public AuctionPostingAccess GetAuctionPostingAccess(ulong accountId) =>
        _accounts.TryGetValue(accountId, out var connection) ? connection.AuctionPostingAccess : null;

    private static AuctionPostingAccess LoadAuctionPostingAccess(ulong accountId)
    {
        if (accountId == 0)
            return null;

        try
        {
            using var connection = MySQL.CreateConnection();
            return ReadAuctionPostingAccess(connection, accountId);
        }
        catch (MySqlException ex) when (ex.Number == (int)MySqlErrorCode.BadFieldError)
        {
            // Existing installations that defer SQL updates keep their previous auction access.
            return AuctionPostingAccess.Default;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to read auction posting access for account {0}", accountId);
            return null;
        }
    }

    internal void SendAuctionPostingAccess(GameConnection connection)
    {
        lock (GetAuctionPostingLock(connection.AccountId))
        {
            var access = LoadAuctionPostingAccess(connection.AccountId);
            connection.AuctionPostingAccess = access;
            connection.SendPacket(new SCAccountAttributeListPacket(access ?? AuctionPostingAccess.Unavailable));
        }
    }

    internal ErrorMessageType ChangeAuctionPostingAccess(GameConnection gameConnection, bool canPost)
    {
        var accountId = gameConnection.AccountId;
        if (accountId == 0)
            return ErrorMessageType.AucInvalidPostAuthority;

        lock (GetAuctionPostingLock(accountId))
        {
            if (!_accounts.TryGetValue(accountId, out var currentSession) || !ReferenceEquals(currentSession, gameConnection))
                return ErrorMessageType.AucInvalidPostAuthority;

            var now = DateTime.UtcNow;
            try
            {
                using var connection = MySQL.CreateConnection();
                if (!WriteAuctionPostingAccess(connection, accountId, canPost, now))
                {
                    var current = ReadAuctionPostingAccess(connection, accountId);
                    if (current?.CanPost != canPost)
                        return ErrorMessageType.AucServiceUnavailable;

                    gameConnection.AuctionPostingAccess = current;
                    return canPost ? ErrorMessageType.AucPostAlreadyAuthority : ErrorMessageType.AucPostNonAuthority;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to change auction posting access for account {0}", accountId);
                return ErrorMessageType.AucServiceUnavailable;
            }

            gameConnection.AuctionPostingAccess = new AuctionPostingAccess(canPost, now);
            try
            {
                if (canPost)
                    gameConnection.SendPacket(new SCAccountAttributeUpdatedPacket(now));
                else
                    gameConnection.SendPacket(new SCAccountAttributeRemovedPacket());
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "The auction posting change for account {0} will be sent at next login", accountId);
            }

            return ErrorMessageType.NoErrorMessage;
        }
    }

    private object GetAuctionPostingLock(ulong accountId)
    {
        lock (_locks)
        {
            if (!_locks.TryGetValue(accountId, out var accountLock))
            {
                accountLock = new object();
                _locks.Add(accountId, accountLock);
            }

            return accountLock;
        }
    }

    internal static AuctionPostingAccess ReadAuctionPostingAccess(MySqlConnection connection, ulong accountId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT auction_post_allowed, auction_post_changed_at
            FROM accounts WHERE account_id = @accountId
            """;
        command.Parameters.AddWithValue("@accountId", accountId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        if (reader.IsDBNull(0))
            return AuctionPostingAccess.Default;

        return new AuctionPostingAccess(reader.GetBoolean(0),
            reader.IsDBNull(1) ? DateTime.UnixEpoch : reader.GetDateTime(1));
    }

    internal static bool WriteAuctionPostingAccess(MySqlConnection connection, ulong accountId, bool canPost, DateTime now)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE accounts
            SET auction_post_allowed = @canPost, auction_post_changed_at = @now
            WHERE account_id = @accountId AND COALESCE(auction_post_allowed, 1) <> @canPost
            """;
        command.Parameters.AddWithValue("@canPost", canPost);
        command.Parameters.AddWithValue("@now", now);
        command.Parameters.AddWithValue("@accountId", accountId);
        return command.ExecuteNonQuery() == 1;
    }
}
