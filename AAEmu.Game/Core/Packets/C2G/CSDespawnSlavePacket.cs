using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSDespawnSlavePacket() : GamePacket(CSOffsets.CSDespawnSlavePacket, 5)
{
    public override void Read(PacketStream stream)
    {
        var slaveObjId = stream.ReadBc();
        var owner = Connection.ActiveChar;
        var world = owner?.ParentWorld;
        if (world == null)
            return;

        var slave = world.GetSlaveByObjId(slaveObjId);
        if (slave == null || !ReferenceEquals(slave.Summoner, owner))
        {
            Connection.SendPacket(new SCSlaveDespawnPacket(slaveObjId, false));
            return;
        }

        world.SlaveManager.Delete(owner, slaveObjId, false);
    }
}
