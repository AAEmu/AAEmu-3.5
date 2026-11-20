using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class LocalizationManagerTests
{
    [Test]
    public async Task TransportFactionPacket_WritesEmptyNameForLocalizationMarker()
    {
        var localizationManager = new LocalizationManager();
        var factionId = FactionsEnum.PcFriendly;
        localizationManager.AddTranslation("system_factions", "name", (uint)factionId, "170906 DO NOT TRANSLATE");

        var faction = new SystemFaction
        {
            Id = factionId,
            Name = localizationManager.Get("system_factions", "name", (uint)factionId),
            OwnerName = string.Empty
        };
        var stream = new SCSystemFactionListPacket(faction).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1);
        await Assert.That(stream.ReadUInt32()).IsEqualTo((uint)factionId);
        await Assert.That(stream.ReadUInt32()).IsEqualTo((uint)faction.MotherId);
        await Assert.That(stream.ReadString()).IsEqualTo(string.Empty);
    }

    [Test]
    [Arguments("Alternate text (Do Not Translate)", "Carriage")]
    [Arguments("Hackney Carriage", "Hackney Carriage")]
    [Arguments("", "Carriage")]
    public async Task Get_ReturnsUsableTranslationOrFallback(string translation, string expected)
    {
        var localizationManager = new LocalizationManager();
        localizationManager.AddTranslation("transfers", "comment", 134, translation);

        await Assert.That(localizationManager.Get("transfers", "comment", 134, "Carriage")).IsEqualTo(expected);
    }
}
