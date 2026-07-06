using AAEmu.Game.Models.Game.Crime;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class CrimeManagerTests
{
    [Test]
    public async Task CrimeEvent_MapsCrimeTableColumns()
    {
        var type = typeof(CrimeEvent);
        await Assert.That(type.GetProperty("Id")).IsNotNull();
        await Assert.That(type.GetProperty("Criminal")).IsNotNull();
        await Assert.That(type.GetProperty("Victim")).IsNotNull();
        await Assert.That(type.GetProperty("Reporter")).IsNotNull();
        await Assert.That(type.GetProperty("DoodadTemplate")).IsNotNull();
        await Assert.That(type.GetProperty("CrimeKind")).IsNotNull();
        await Assert.That(type.GetProperty("ZoneKey")).IsNotNull();
        await Assert.That(type.GetProperty("Position")).IsNotNull();
        await Assert.That(type.GetProperty("CrimeTime")).IsNotNull();
        await Assert.That(type.GetProperty("ReportTime")).IsNotNull();
        await Assert.That(type.GetProperty("Arg1")).IsNotNull();
        await Assert.That(type.GetProperty("Arg2")).IsNotNull();
        await Assert.That(type.GetProperty("Arg3")).IsNotNull();
        await Assert.That(type.GetProperty("Msg")).IsNotNull();
    }
}
