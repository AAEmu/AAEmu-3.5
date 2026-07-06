using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class CashShopManagerTests
{
    [Test]
    public async Task IcsSku_MapsExpectedColumns()
    {
        var type = typeof(IcsSku);
        await Assert.That(type.GetProperty("Sku")).IsNotNull();
        await Assert.That(type.GetProperty("ShopId")).IsNotNull();
        await Assert.That(type.GetProperty("Position")).IsNotNull();
        await Assert.That(type.GetProperty("ItemId")).IsNotNull();
        await Assert.That(type.GetProperty("ItemCount")).IsNotNull();
        await Assert.That(type.GetProperty("SelectType")).IsNotNull();
        await Assert.That(type.GetProperty("IsDefault")).IsNotNull();
        await Assert.That(type.GetProperty("EventType")).IsNotNull();
        await Assert.That(type.GetProperty("EventEndDate")).IsNotNull();
        await Assert.That(type.GetProperty("Currency")).IsNotNull();
        await Assert.That(type.GetProperty("Price")).IsNotNull();
        await Assert.That(type.GetProperty("DiscountPrice")).IsNotNull();
        await Assert.That(type.GetProperty("BonusItemId")).IsNotNull();
        await Assert.That(type.GetProperty("BonusItemCount")).IsNotNull();
    }

    [Test]
    public async Task IcsItem_MapsExpectedColumns()
    {
        var type = typeof(IcsItem);
        await Assert.That(type.GetProperty("ShopId")).IsNotNull();
        await Assert.That(type.GetProperty("DisplayItemId")).IsNotNull();
        await Assert.That(type.GetProperty("Name")).IsNotNull();
        await Assert.That(type.GetProperty("LimitedType")).IsNotNull();
        await Assert.That(type.GetProperty("LimitedStockMax")).IsNotNull();
        await Assert.That(type.GetProperty("LevelMin")).IsNotNull();
        await Assert.That(type.GetProperty("LevelMax")).IsNotNull();
        await Assert.That(type.GetProperty("BuyRestrictType")).IsNotNull();
        await Assert.That(type.GetProperty("BuyRestrictId")).IsNotNull();
        await Assert.That(type.GetProperty("IsSale")).IsNotNull();
        await Assert.That(type.GetProperty("IsHidden")).IsNotNull();
        await Assert.That(type.GetProperty("SaleStart")).IsNotNull();
        await Assert.That(type.GetProperty("SaleEnd")).IsNotNull();
        await Assert.That(type.GetProperty("Remaining")).IsNotNull();
        await Assert.That(type.GetProperty("ShopButtons")).IsNotNull();
        await Assert.That(type.GetProperty("Skus")).IsNotNull();
    }
}
