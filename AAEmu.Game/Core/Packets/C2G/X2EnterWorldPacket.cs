using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class X2EnterWorldPacket() : GamePacket(CSOffsets.X2EnterWorldPacket, 5)
{
    public override void Read(PacketStream stream)
    {
        var pFrom = stream.ReadUInt32();
        var pTo = stream.ReadUInt32();
        var accountId = stream.ReadUInt64();
        var cookie = stream.ReadUInt32();
        var zoneId = stream.ReadInt32();
        var tb = stream.ReadInt16();
        var revision = stream.ReadUInt32();
        var index = stream.ReadUInt32();

        Logger.Debug("[ENTRY-DIAG] X2EnterWorld from {0}: account={1}, from={2}, to={3}, zone={4}, tb={5}, revision={6}, index={7}",
            Connection.Ip, accountId, pFrom, pTo, zoneId, tb, revision, index);
        EnterWorldManager.Instance.Login(Connection, accountId, cookie);
    }
}
