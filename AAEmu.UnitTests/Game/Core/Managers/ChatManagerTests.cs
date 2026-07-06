using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ChatManagerTests
{
    [Test]
    public async Task ChatChannel_HasExpectedProperties()
    {
        var type = typeof(ChatChannel);
        await Assert.That(type.GetProperty("ChatType")).IsNotNull();
        await Assert.That(type.GetProperty("Faction")).IsNotNull();
        await Assert.That(type.GetProperty("SubType")).IsNotNull();
        await Assert.That(type.GetProperty("InternalId")).IsNotNull();
        await Assert.That(type.GetProperty("InternalName")).IsNotNull();
        await Assert.That(type.GetProperty("Members")).IsNotNull();
    }
}
