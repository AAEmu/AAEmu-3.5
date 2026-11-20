using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class GmCommandPacketWireFormatTests
{
    [Test]
    public async Task UnitTeleport_WritesClientOpcodeAndPackedPosition()
    {
        var packet = new SCTeleportUnitPacket(TeleportReason.Gm, 0, 12.5f, 25.25f, -100f, 1f);
        var stream = packet.Write(new PacketStream());

        await Assert.That(packet.TypeId).IsEqualTo((ushort)0x157);
        await Assert.That(Convert.ToHexString(stream.GetBytes())).IsEqualTo("0C00000019008032000000000000803F");
    }

    [Test]
    public async Task ClientTeleportCompletion_UsesSpawnCharacter2Opcode()
    {
        var packet = new CSSpawnCharacter2Packet();

        await Assert.That(packet.TypeId).IsEqualTo((ushort)0x089);
    }

    [Test]
    public async Task JoinedTeam_WritesHeirLevelAfterMemberObjectId()
    {
        var character = new Character(null) { Id = 1, ObjId = 0x123456, Name = "Test", HeirLevel = 17 };
        var team = new Team { Id = 3, OwnerId = character.Id, IsParty = true };
        team.AddMember(character);
        var stream = new SCJoinedTeamPacket(team).Write(new PacketStream());

        // Skip team metadata and the member's fields preceding its object ID.
        stream.Pos = 288;
        await Assert.That(stream.ReadUInt32()).IsEqualTo(character.Id);
        await Assert.That(stream.ReadString()).IsEqualTo(character.Name);
        stream.Pos += 4; // race, gender, level, role
        await Assert.That(stream.ReadBc()).IsEqualTo(character.ObjId);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count - 1);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)17);
    }

    [Test]
    public async Task TeamMapPing_WritesSixSlotsAndTwoCompressedInstanceIdBlocks()
    {
        var position = new WorldSpawnPosition { X = 12.5f, Y = 25.25f, Z = -100f };
        var stream = new SCTeamPingPosPacket(TeamPingState.FromMapPosition(position, 0x123456)).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.Count).IsEqualTo(67);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1); // updated slot
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)2); // active slot 1
        stream.Pos += 9; // unused slot 0
        await Assert.That(stream.ReadPosition()).IsEqualTo((12.5f, 25.25f, -100f));
        stream.Pos += 4 * 9;
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)8); // slot 1 uses three bytes
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(stream.ReadBc()).IsEqualTo(0x123456u);
        stream.Pos += 5; // remaining zero instance IDs and the second PISC header
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0); // line point count
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SetPingPos_UpdatesLocalMapPositionFromSlotOne(bool active)
    {
        var character = new Character(null) { LocalPingPosition = new WorldSpawnPosition { X = 7, Y = 9 } };
        var packet = new CSSetPingPosPacket { Connection = new GameConnection(null) { ActiveChar = character } };
        var stream = new PacketStream()
            .Write(0u) // team ID, zero for a personal ping
            .Write((byte)1).Write(active ? (byte)2 : (byte)0)
            .Write(new byte[9]) // slot 0
            .Write(Convert.FromHexString("001900803200000000"))
            .Write(new byte[4 * 9])
            .Write(new byte[9]) // two PISC groups with zero IDs, then zero line points
            .Write(new byte[9]); // trailing bytes from the decrypted level-5 frame
        stream.Rollback();

        packet.Read(stream);

        await Assert.That(stream.Pos).IsEqualTo(stream.Count - 9);
        await Assert.That(character.LocalPingPosition.X).IsEqualTo(active ? 12.5f : 0f);
        await Assert.That(character.LocalPingPosition.Y).IsEqualTo(active ? 25.25f : 0f);
    }

    [Test]
    [Arguments(20, true)]
    [Arguments(21, false)]
    public async Task TeamLinePing_ValidatesClientPointLimit(int lineCount, bool valid)
    {
        var stream = new PacketStream()
            .Write((byte)4).Write((byte)16)
            .Write(new byte[6 * 9])
            .Write(new byte[8])
            .Write((byte)lineCount)
            .Write(new byte[lineCount * 9]);
        var payload = stream.GetBytes();
        stream.Rollback();

        var pingState = TeamPingState.Read(stream);

        await Assert.That(pingState != null).IsEqualTo(valid);
        if (valid)
        {
            var reply = new SCTeamPingPosPacket(pingState).Write(new PacketStream());
            await Assert.That(Convert.ToHexString(reply.GetBytes())).IsEqualTo(Convert.ToHexString(payload));
        }
    }
}
