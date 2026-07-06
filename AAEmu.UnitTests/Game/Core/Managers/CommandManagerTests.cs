using AAEmu.Game.Core.Managers;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class CommandManagerTests
{
    [Test]
    public async Task CommandPrefix_IsSlash()
    {
        await Assert.That(CommandManager.CommandPrefix).IsEqualTo("/");
    }
}
