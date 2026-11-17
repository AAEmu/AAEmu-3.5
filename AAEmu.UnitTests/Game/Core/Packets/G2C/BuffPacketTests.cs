using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class BuffPacketTests
{
    [Test]
    public async Task UpdatedBuff_PreservesClientIndexStackAndCharge()
    {
        var stream = new SCBuffUpdatedPacket(74387, 1, 3, 7).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadBc()).IsEqualTo(74387u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);
        await Assert.That(stream.ReadInt32()).IsEqualTo(3);
        await Assert.That(stream.ReadInt32()).IsEqualTo(7);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task GlideRemoval_PreservesClientBuffIdentityAndPosition()
    {
        var position = new Vector3(10, 20, 30);
        var stream = new SCGlideBuffRemovedPacket(74387, 1, position).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadBc()).IsEqualTo(74387u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);
        var (x, y, z) = stream.ReadPosition();
        await Assert.That(x).IsEqualTo(10f).Within(0.001f);
        await Assert.That(y).IsEqualTo(20f).Within(0.001f);
        await Assert.That(z).IsEqualTo(30f).Within(0.001f);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task BuffState_HasNoPayloadForClientSerializer()
    {
        // The 0x04E reader (sub_39384300) reads no fields; its serializer is a no-op.
        var owner = new BaseUnit { ObjId = 74387 };
        var buff = new Buff(owner, null, new SkillCasterUnit(owner.ObjId),
            new BuffTemplate { Id = 4645 }, null, DateTime.UtcNow) { Index = 1 };
        var stream = new SCBuffStatePacket(buff).Write(new PacketStream());

        await Assert.That(stream.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(1u)]
    [Arguments(256u)]
    [Arguments(0x12345678u)]
    public async Task CreatedThenRemoved_UsesSameClientBuffIdentity(uint index)
    {
        var owner = new BaseUnit { ObjId = 74387 };
        var buff = new Buff(owner, null, new SkillCasterUnit(owner.ObjId),
            new BuffTemplate { Id = 4645 }, null, DateTime.UtcNow) { Index = index };
        var created = new SCBuffCreatedPacket(buff).Write(new PacketStream());
        created.Rollback();

        // x2game 3.5 sub_399438C0: caster, casterId, target bc, buff index.
        // CryNetwork's Block() returns true without consuming a presence byte.
        created.ReadByte();
        created.ReadBc();
        created.ReadUInt32();
        var createdTarget = created.ReadBc();
        var createdIndex = created.ReadUInt32();
        var templateId = created.ReadUInt32();
        await Assert.That(createdTarget).IsEqualTo(owner.ObjId);
        await Assert.That(createdIndex).IsEqualTo(index);
        await Assert.That(templateId).IsEqualTo(4645u);

        var clientBuffs = new Dictionary<(uint Target, uint Index), uint>
        {
            [(createdTarget, createdIndex)] = templateId
        };
        var removed = new SCBuffRemovedPacket(owner.ObjId, index).Write(new PacketStream());
        removed.Rollback();

        // x2game sub_3993E3D0: target bc (3 bytes), index (4 bytes).
        var removedTarget = removed.ReadBc();
        var removedIndex = removed.ReadUInt32();
        clientBuffs.Remove((removedTarget, removedIndex));

        await Assert.That(clientBuffs.Count).IsEqualTo(0);
        await Assert.That(removed.Pos).IsEqualTo(removed.Count);
    }
}
