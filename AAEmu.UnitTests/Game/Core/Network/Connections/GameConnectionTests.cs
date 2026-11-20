#nullable enable

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.UnitTests.Game.Core.Network.Connections;

[NotInParallel]
public class GameConnectionTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ConcurrentSends_EnqueueConsecutivePacketNumbers(bool pauseDuringEncoding)
    {
        var encryption = EncryptionManager.Instance;
        encryption.Load();
        var timeout = TimeSpan.FromSeconds(10);
        using var firstPaused = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();

        void PauseFirst()
        {
            firstPaused.Set();
            if (!releaseFirst.Wait(timeout))
                throw new TimeoutException("The first send was not released.");
        }

        var session = new CapturingSession(pauseDuringEncoding ? null : PauseFirst);
        var connection = new GameConnection(session) { AccountId = 1 };
        var first = Task.Run(() => connection.SendPacket(
            new SequenceTestPacket(pauseDuringEncoding ? PauseFirst : null)));
        Task second;
        bool secondReturnedWhileFirstPaused;
        try
        {
            if (!firstPaused.Wait(timeout))
            {
                if (first.IsCompleted)
                    await first;
                throw new TimeoutException("The first send did not reach the pause.");
            }

            var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            second = Task.Run(() =>
            {
                secondStarted.SetResult();
                connection.SendPacket(new SequenceTestPacket(null));
            });
            await secondStarted.Task.WaitAsync(timeout);
            // Let the second send advance as far as it can while the first remains paused.
            secondReturnedWhileFirstPaused = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(500))) == second;
        }
        finally
        {
            releaseFirst.Set();
        }

        await Task.WhenAll(first, second).WaitAsync(timeout);
        var numbers = session.Numbers.ToArray();
        await Assert.That(numbers.Length).IsEqualTo(2);
        await Assert.That(numbers[0]).IsEqualTo((byte)0);
        await Assert.That(numbers[1]).IsEqualTo((byte)1);
        if (!pauseDuringEncoding)
            await Assert.That(secondReturnedWhileFirstPaused).IsTrue();
    }

    private sealed class SequenceTestPacket(Action? beforeWrite) : GamePacket(0x008, 5)
    {
        public override PacketStream Write(PacketStream stream)
        {
            beforeWrite?.Invoke();
            return stream.Write(0f);
        }
    }

    private sealed class CapturingSession(Action? beforeFirstEnqueue) : ISession
    {
        public ConcurrentQueue<byte> Numbers { get; } = new();
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId => 1;
        public Socket Socket => throw new NotSupportedException();

        public void SendPacket(byte[] packet)
        {
            // The first four bytes are the frame length and marker/level. XOR is its own inverse.
            var payload = EncryptionManager.Instance.StoCEncrypt(packet[4..]);
            var sequence = payload[1];
            if (sequence == 0)
                beforeFirstEnqueue?.Invoke();
            Numbers.Enqueue(sequence);
        }

        public void AddAttribute(string name, object attribute) => throw new NotSupportedException();
        public object GetAttribute(string name) => throw new NotSupportedException();
        public void ClearAttribute(string name) => throw new NotSupportedException();
        public void Close() => throw new NotSupportedException();
    }
}
