using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMateSpawnedPacket(Mate mate) : GamePacket(SCOffsets.SCMateSpawnedPacket, 5)
{
    private const int MountSkillSlots = 10;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(mate.TlId);
        stream.Write((byte)mate.MateType);
        stream.Write(mate.Id);
        stream.Write(mate.ItemId);
        stream.Write(mate.UserState);
        stream.Write(mate.Experience);
        stream.Write(mate.Mileage);
        stream.Write(mate.SpawnDelayTime);

        for (var i = 0; i < MountSkillSlots; i++)
            stream.Write(i < mate.Skills.Count ? mate.Skills[i] : 0u);

        return stream;
    }
}
