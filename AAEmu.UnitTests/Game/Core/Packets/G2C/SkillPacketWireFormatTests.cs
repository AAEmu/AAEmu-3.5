using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SkillPacketWireFormatTests
{
    [Test]
    public async Task PositionTarget_ReadsAllThreeObjectIdsBeforeSkillObjectFlag()
    {
        byte[] payload =
        [
            0, 0, 0, 0, 0, 0, 0, 0, // X
            0, 0, 0, 0, 0, 0, 0, 0, // Y
            0, 0, 0, 0, 0, 0, 0, 0, // Z and rotation
            0x03, 0x02, 0x01, 0x06, 0x05, 0x04, 0x09, 0x08, 0x07,
            0x80
        ];
        var stream = new PacketStream(payload);
        var target = SkillCastTarget.GetByType(SkillCastTargetType.Position);

        target.Read(stream);

        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0x80);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
        var written = target.Write(new PacketStream()).GetBytes();
        byte[] expected = [1, .. payload.AsSpan(0, payload.Length - 1)];
        await Assert.That(written.AsSpan().SequenceEqual(expected)).IsTrue();
    }

    [Test]
    public async Task HeirSkillListSendsOneSelectedSuccessorWithItsBaseSkillAndRank()
    {
        var stream = new SCHeirSkillListPacket([new(1, 18132, 36401, 11, 1, 0, true)]).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)1);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)18132);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)36401);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(11u);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task HeirLevelUpSendsOnlyACompressedObjectId()
    {
        var stream = new SCHeirLevelUpPacket(74387).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadBc()).IsEqualTo(74387u);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    // Expectations come from the x2game 3.5 readers and CryNetwork's byte-free Block().
    [Test]
    public async Task SkillStopped_ReadsUnitAndSkillWithoutTrailingBytes()
    {
        var stream = new SCSkillStoppedPacket(74387, 15857).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadBc()).IsEqualTo(74387u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(15857u);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task SkillLearned_PreservesFullClientSkillId()
    {
        var stream = new SCSkillLearnedPacket(new Skill { Id = 0x12345678 }).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.Count).IsEqualTo(4);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0x12345678u);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task ActiveSkillList_ReadsEveryEntryWithoutShiftingFields()
    {
        var packet = new SCListSkillActiveTypsPacket([new(100, 1), new(15857, 2)]);
        var stream = packet.Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadInt32()).IsEqualTo(2);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(100u);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(15857u);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)2);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task ActiveSkillUpdate_ReadsSkillAndActiveType()
    {
        var stream = new SCUpdateSkillActiveTypePacket(15857, 2).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadUInt32()).IsEqualTo(15857u);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)2);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task QuestChatLink_PreservesFullQuestIdAndFollowingFields()
    {
        var packet = new SCChatMessagePacket(ChatType.System, null, "test", 0, 0,
            [3, 0, 0, 0], [0, 0, 0, 0], [4, 0, 0, 0], null,
            [0x12345678, 0, 0, 0], null);
        var stream = packet.Write(new PacketStream());
        stream.Rollback();

        stream.ReadInt64(); // channel
        stream.ReadBc(); // character object id
        stream.ReadUInt32(); // character id
        stream.ReadByte(); // language
        stream.ReadByte(); // race
        stream.ReadUInt32(); // faction
        stream.ReadString(); // name
        stream.ReadString(); // message
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)3);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)0);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)4);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0x12345678u);
        for (var i = 0; i < 3; i++)
            await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(stream.ReadInt32()).IsEqualTo(0); // ability
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0); // option
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }
}
