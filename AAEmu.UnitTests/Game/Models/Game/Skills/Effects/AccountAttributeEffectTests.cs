using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Account;
using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

public class AccountAttributeEffectTests
{
    private static readonly DateTime GrantedAt = new(2026, 11, 23, 2, 30, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task TheAuthoredPermanentAuctionChangeUsesTheEffectDirection(bool isAdd)
    {
        var effect = new AccountAttributeEffect { KindId = 1, IsAdd = isAdd };

        var supported = effect.TryGetAuctionPostingChange(out var canPost);

        await Assert.That(supported).IsTrue();
        await Assert.That(canPost).IsEqualTo(isAdd);
    }

    [Test]
    [Arguments(1u, 2u, false, 0u, 0u)]
    [Arguments(1u, 0u, true, 0u, 0u)]
    [Arguments(1u, 0u, false, 2u, 0u)]
    [Arguments(1u, 0u, false, 0u, 3u)]
    [Arguments(2u, 3u, false, 0u, 1440u)]
    public async Task UnsupportedEffectsLeaveTheSourceItemWithItsOwner(
        uint kind, uint value, bool bindWorld, uint count, uint minutes)
    {
        var owner = new Character(null) { AccountId = 10, ObjId = 20 };
        var skill = new Skill();
        var effect = new AccountAttributeEffect
        {
            KindId = kind, KindValue = value, BindWorld = bindWorld, Count = count, Time = minutes, IsAdd = true
        };

        effect.Apply(owner, new SkillCasterUnit(owner.ObjId), owner, new SkillCastUnitTarget(owner.ObjId),
            null, new EffectSource(skill), null, GrantedAt);

        // Skill.ApplyEffects only consumes the authored source item when the cast succeeds.
        await Assert.That(skill.Cancelled).IsTrue();
    }

    [Test]
    public async Task ListingUsesTheDurableGrantDateAndAccountWideSentinel()
    {
        var list = new SCAccountAttributeListPacket(new AuctionPostingAccess(true, GrantedAt)).Write(new PacketStream());
        list.Rollback();
        await Assert.That(list.ReadUInt32()).IsEqualTo(1u);
        await AssertAuctionAttribute(list);

        var update = new SCAccountAttributeUpdatedPacket(GrantedAt).Write(new PacketStream());
        update.Rollback();
        await AssertAuctionAttribute(update);
    }

    [Test]
    public async Task RevokedAccessPublishesAnEmptyListAndTheNativeRemovalKey()
    {
        var list = new SCAccountAttributeListPacket(new AuctionPostingAccess(false, GrantedAt)).Write(new PacketStream());
        list.Rollback();
        await Assert.That(list.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(list.Pos).IsEqualTo(list.Count);

        var removed = new SCAccountAttributeRemovedPacket().Write(new PacketStream());
        removed.Rollback();
        await Assert.That(removed.ReadByte()).IsEqualTo((byte)1);
        await Assert.That(removed.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(removed.Pos).IsEqualTo(removed.Count);
    }

    [Test]
    public async Task DeniedAccountStopsTheNormalPostingPathBeforeAnyItemWork()
    {
        var seller = new Character(null) { AccountId = 17, Id = 21 };
        var accounts = Mock.Of<IAccountManager>();
        accounts.GetAuctionPostingAccess(seller.AccountId).Returns(new AuctionPostingAccess(false, GrantedAt));
        var auctions = new AuctionManager(null, null, null, null, null, accounts.Object);

        auctions.PostLotOnAuction(seller, 10, 10, 100, 150, 250, (AuctionDuration)0);

        await Assert.That(seller.Money).IsEqualTo(0L);
    }

    [Test]
    public async Task PostingReadsTheRegisteredSessionStateAndDropsItOnDisconnect()
    {
        var account = new AccountManager(null, null);
        var field = typeof(AccountManager).GetField("_accounts", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var sessions = (ConcurrentDictionary<ulong, GameConnection>)field.GetValue(account)!;
        var session = new GameConnection(null) { AccountId = 17, AuctionPostingAccess = new(true, GrantedAt) };
        sessions.TryAdd(17, session);

        await Assert.That(account.GetAuctionPostingAccess(17).CanPost).IsTrue();
        session.AuctionPostingAccess = new AuctionPostingAccess(false, GrantedAt);
        await Assert.That(account.GetAuctionPostingAccess(17).CanPost).IsFalse();
        account.Remove(17);
        await Assert.That(account.GetAuctionPostingAccess(17)).IsNull();
    }

    private static async Task AssertAuctionAttribute(PacketStream stream)
    {
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0xFF);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadInt64()).IsEqualTo(1795401000L);
        await Assert.That(stream.ReadInt64()).IsEqualTo(0L);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }
}
