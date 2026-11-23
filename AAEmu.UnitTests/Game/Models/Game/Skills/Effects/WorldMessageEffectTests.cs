using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

public class WorldMessageEffectTests
{
    [Test]
    public async Task NativePacketContainsTheEffectAndLocationsWithoutRetailFields()
    {
        var packet = new SCWorldMessagePacket(124, new Vector3(1.25f, -0.5f, 42), 148, "source", 149, "target");
        var stream = packet.Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadUInt32()).IsEqualTo(124u);
        await Assert.That(stream.ReadInt64()).IsEqualTo(0x140000000000L);
        await Assert.That(stream.ReadInt64()).IsEqualTo(-0x080000000000L);
        await Assert.That(stream.ReadSingle()).IsEqualTo(42f);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(148u);
        await Assert.That(stream.ReadString()).IsEqualTo("source");
        await Assert.That(stream.ReadUInt32()).IsEqualTo(149u);
        await Assert.That(stream.ReadString()).IsEqualTo("target");
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task SameFactionScopeIncludesAnotherRaceInTheSameTopFaction()
    {
        var effect = new WorldMessageEffect { FactionScopeId = 1 };
        var caster = new BaseUnit { Faction = new SystemFaction { Id = FactionsEnum.Nuian, MotherId = FactionsEnum.NuiaAlliance } };
        var alliedRace = new Character(null)
        {
            Faction = new SystemFaction { Id = FactionsEnum.Elf, MotherId = FactionsEnum.NuiaAlliance }
        };
        var opposingRace = new Character(null)
        {
            Faction = new SystemFaction { Id = FactionsEnum.Harani, MotherId = FactionsEnum.HaranyaAlliance }
        };

        await Assert.That(effect.IncludesRecipient(caster, alliedRace, 0, null)).IsTrue();
        await Assert.That(effect.IncludesRecipient(caster, opposingRace, 0, null)).IsFalse();
    }

    [Test]
    public async Task ZoneGroupScopeIncludesASeparateZoneOfTheSameGroup()
    {
        var effect = new WorldMessageEffect { ZoneGroupOnly = true, Message = "notification" };
        var caster = new BaseUnit { Transform = new(null) { ZoneId = 21 } };
        var sameGroup = new Character(null) { Transform = new(null) { ZoneId = 22 } };
        var otherGroup = new Character(null) { Transform = new(null) { ZoneId = 23 } };
        var zones = Mock.Of<IZoneManager>();
        zones.GetZoneByKey(21).Returns(new Zone { GroupId = 19 });
        zones.GetZoneByKey(22).Returns(new Zone { GroupId = 19 });
        zones.GetZoneByKey(23).Returns(new Zone { GroupId = 20 });

        var hasScope = effect.TryResolveAudienceZone(caster, zones.Object, out var group);

        await Assert.That(hasScope).IsTrue();
        await Assert.That(effect.IncludesRecipient(caster, sameGroup, group, zones.Object)).IsTrue();
        await Assert.That(effect.IncludesRecipient(caster, otherGroup, group, zones.Object)).IsFalse();
    }

    [Test]
    [Arguments(ZoneConflictType.Peace, false)]
    [Arguments(ZoneConflictType.War, true)]
    public async Task WarScopeUsesTheCurrentConflictState(ZoneConflictType state, bool expected)
    {
        var effect = new WorldMessageEffect { ZoneGroupWarState = true, Message = "notification" };
        var caster = new BaseUnit { Transform = new(null) { ZoneId = 21 } };
        var zones = Mock.Of<IZoneManager>();
        zones.GetZoneByKey(21).Returns(new Zone { GroupId = 19 });
        zones.GetConflicts().Returns([new TestConflict(19, state)]);

        var hasScope = effect.TryResolveAudienceZone(caster, zones.Object, out _);

        await Assert.That(hasScope).IsEqualTo(expected);
    }

    private sealed class TestConflict : ZoneConflict
    {
        public TestConflict(ushort groupId, ZoneConflictType state) : base(new ZoneGroup())
        {
            ZoneGroupId = groupId;
            CurrentZoneState = state;
        }
    }
}
