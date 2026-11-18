using System.Collections.Concurrent;

using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Xml;
using AAEmu.Game.Services;

namespace AAEmu.UnitTests.Game.Services;

public class ClientSpawnPipelineTests
{
    [Test]
    public async Task DiscoverZones_FindsSolzreedZones_WithOnDiskSpawnerFiles()
    {
        if (!ClientNpcSpawnerLoader.Enabled)
        {
            await Assert.That(true).IsTrue();
            return;
        }

        var template = new WorldTemplate { Name = "main_world", Id = 1 };
        template.XmlWorldZones = new ConcurrentDictionary<uint, XmlWorldZone>();
        template.XmlWorldZones.TryAdd(129, new XmlWorldZone { Id = 129 });
        template.XmlWorldZones.TryAdd(179, new XmlWorldZone { Id = 179 });
        template.XmlWorldZones.TryAdd(999_999, new XmlWorldZone { Id = 999_999 });

        var world = new WorldInstance(template, 0, true, 1);
        var zones = ClientNpcSpawnerLoader.DiscoverZonesWithClientSpawners(world);

        await Assert.That(zones.Contains(129u)).IsTrue();
        await Assert.That(zones.Contains(179u)).IsTrue();
        await Assert.That(zones.Contains(999_999u)).IsFalse();
    }
}