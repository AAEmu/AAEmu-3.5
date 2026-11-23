namespace AAEmu.Game.Models.Account;

public sealed record AuctionPostingAccess(bool CanPost, DateTime GrantedAt)
{
    internal const byte Kind = 1;
    internal const uint Listing = 0;
    internal const byte AllWorlds = byte.MaxValue;

    internal static AuctionPostingAccess Default { get; } = new(true, DateTime.UnixEpoch);
    internal static AuctionPostingAccess Unavailable { get; } = new(false, DateTime.MinValue);
}
