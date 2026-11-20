using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSpawnCharacter2Packet() : GamePacket(CSOffsets.CSSpawnCharacter2Packet, 5)
{
    public override void Read(PacketStream stream)
    {
        // The client acknowledges a local teleport with world coordinates and a quaternion.
        const int payloadSize = 2 * sizeof(long) + 5 * sizeof(float);
        // The decrypted level-5 frame can include trailing block padding.
        if (stream.Count - stream.Pos < payloadSize)
            return;

        var x = Helpers.ConvertLongX(stream.ReadInt64());
        var y = Helpers.ConvertLongY(stream.ReadInt64());
        var z = stream.ReadSingle();
        stream.Pos += 4 * sizeof(float); // orientation

        var character = Connection.CompleteUnitTeleport();
        if (character == null)
            return;

        Logger.Debug("TeleportEnded, X: {0}, Y: {1}, Z: {2}", x, y, z);
        WorldManager.ResendVisibleObjectsToCharacter(character);
    }
}
