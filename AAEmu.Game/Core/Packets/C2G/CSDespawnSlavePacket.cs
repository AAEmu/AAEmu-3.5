using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSDespawnSlavePacket() : GamePacket(CSOffsets.CSDespawnSlavePacket, 5)
{
    public override void Read(PacketStream stream)
    {
        var slaveObjId = stream.ReadBc();

        // Logger.Debug($"DespawnSlave, SlaveObjId: {slaveObjId}");
        Connection.ActiveChar.ParentWorld.SlaveManager.Delete(Connection.ActiveChar, slaveObjId, false);
    }
}
