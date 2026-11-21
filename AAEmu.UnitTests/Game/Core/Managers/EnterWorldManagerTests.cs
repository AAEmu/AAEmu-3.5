using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Core.Packets.C2S;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Packets.Proxy;
using AAEmu.Game.Models.StaticValues;

using TUnit.Assertions.Enums;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class EnterWorldManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockAccount = Mock.Of<IAccountManager>();
        var mockStream = Mock.Of<IStreamManager>();
        var mockQuest = Mock.Of<IQuestManager>();
        var mockChat = Mock.Of<IChatManager>();
        var mockFamily = Mock.Of<IFamilyManager>();
        var mockWorld = Mock.Of<IWorldManager>();

        var manager = new EnterWorldManager(
            mockAccount.Object,
            mockStream.Object,
            mockQuest.Object,
            mockChat.Object,
            mockFamily.Object,
            mockWorld.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockAccount);
        Mock.VerifyNoOtherCalls(mockStream);
        Mock.VerifyNoOtherCalls(mockQuest);
        Mock.VerifyNoOtherCalls(mockChat);
        Mock.VerifyNoOtherCalls(mockFamily);
        Mock.VerifyNoOtherCalls(mockWorld);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CharacterSelect_ClosesStreamBeforeLobbyAndAcceptsAnotherJoin(bool delayDisconnect)
    {
        const uint gameId = 100_001;
        const ulong accountId = 100_002;
        var encryption = EncryptionManager.Instance;
        encryption.Load();
        var events = new List<string>();
        var handler = new StreamProtocolHandler();
        handler.RegisterPacket(CTOffsets.CTJoinPacket, typeof(CTJoinPacket));

        void RecordGamePacket(byte[] packet)
        {
            if (packet[3] == 5)
            {
                var payload = encryption.StoCEncrypt(packet[4..]);
                var opcode = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(2));
                events.Add($"world:{opcode:X3}:{payload[4]}");
                return;
            }

            var stateOpcode = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4));
            var state = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(6));
            events.Add($"proxy:{stateOpcode:X3}:{state}");
        }

        var game = new GameConnection(new TestSession(gameId, RecordGamePacket, () => events.Add("close-game")))
        {
            AccountId = accountId
        };
        var oldStream = new TestSession(100_003, packet => events.Add($"join-old:{packet[4]}"), () => events.Add("close-old"));
        var newStream = new TestSession(100_004, packet => events.Add($"join-new:{packet[4]}"), () => events.Add("close-new"));
        var body = new PacketStream().Write(CTOffsets.CTJoinPacket).Write(accountId).Write(gameId);
        byte[] joinPacket = new PacketStream().Write(body);
        var streamManager = StreamManager.Instance;
        var enterWorldManager = new EnterWorldManager(
            Mock.Of<IAccountManager>().Object, streamManager, Mock.Of<IQuestManager>().Object,
            Mock.Of<IChatManager>().Object, Mock.Of<IFamilyManager>().Object, Mock.Of<IWorldManager>().Object);

        GameConnectionTable.Instance.AddConnection(game);
        try
        {
            streamManager.AddToken(accountId, gameId);
            handler.OnConnect(oldStream);
            handler.OnReceive(oldStream, joinPacket, 0, joinPacket.Length);

            enterWorldManager.LeaveWorldTask(game, LeaveWorldTargetType.CharacterSelect, null);
            if (!delayDisconnect)
                handler.OnDisconnect(oldStream);

            handler.OnConnect(newStream);
            handler.OnReceive(newStream, joinPacket, 0, joinPacket.Length);
            if (delayDisconnect)
                handler.OnDisconnect(oldStream);

            enterWorldManager.LeaveWorldTask(game, LeaveWorldTargetType.CharacterSelect, null);

            var grant = $"world:{SCOffsets.SCLeaveWorldGrantedPacket:X3}:1";
            var lobby = $"proxy:{PPOffsets.ChangeStatePacket:X3}:0";
            string[] expected = ["join-old:0", "close-old", grant, lobby, "join-new:0", "close-new", grant, lobby];
            await Assert.That(events).IsEquivalentTo(expected, CollectionOrdering.Matching);
        }
        finally
        {
            handler.OnDisconnect(oldStream);
            handler.OnDisconnect(newStream);
            streamManager.RemoveToken(gameId);
            GameConnectionTable.Instance.RemoveConnection(gameId);
            encryption.RemoveConnectionKeysByConnectionId(gameId);
        }
    }

    private sealed class TestSession(uint id, Action<byte[]> onSend, Action onClose) : ISession
    {
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId => id;
        public Socket Socket => throw new NotSupportedException();

        public void SendPacket(byte[] packet) => onSend(packet);
        public void Close() => onClose();
        public void AddAttribute(string name, object attribute) => throw new NotSupportedException();
        public object GetAttribute(string name) => throw new NotSupportedException();
        public void ClearAttribute(string name) => throw new NotSupportedException();
    }
}
