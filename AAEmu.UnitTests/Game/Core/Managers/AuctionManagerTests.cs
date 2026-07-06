using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class AuctionManagerTests
{
    [Test]
    public async Task AuctionLot_MapsAuctionHouseColumns()
    {
        var lotType = typeof(AuctionLot);
        await Assert.That(lotType.GetProperty("Id")).IsNotNull();
        await Assert.That(lotType.GetProperty("Duration")).IsNotNull();
        await Assert.That(lotType.GetProperty("Item")).IsNotNull();
        await Assert.That(lotType.GetProperty("PostDate")).IsNotNull();
        await Assert.That(lotType.GetProperty("EndTime")).IsNotNull();
        await Assert.That(lotType.GetProperty("WorldId")).IsNotNull();
        await Assert.That(lotType.GetProperty("ClientId")).IsNotNull();
        await Assert.That(lotType.GetProperty("ClientName")).IsNotNull();
        await Assert.That(lotType.GetProperty("StartMoney")).IsNotNull();
        await Assert.That(lotType.GetProperty("DirectMoney")).IsNotNull();
        await Assert.That(lotType.GetProperty("BidWorldId")).IsNotNull();
        await Assert.That(lotType.GetProperty("BidderId")).IsNotNull();
        await Assert.That(lotType.GetProperty("BidderName")).IsNotNull();
        await Assert.That(lotType.GetProperty("BidMoney")).IsNotNull();
        await Assert.That(lotType.GetProperty("Extra")).IsNotNull();
    }
}
