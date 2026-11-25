#nullable enable

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSUnhangPacket() : GamePacket(CSOffsets.CSUnhangPacket, 5)
{
    public override void Read(PacketStream stream)
    {
        var unitObjId = stream.ReadBc();
        var reason = stream.ReadUInt32();
        // 0 climbed off from bottom
        // 2 climbed off on top
        // 7 jumped off

        var character = Connection.ActiveChar;
        if (character == null || character.ObjId != unitObjId)
            return;

        var target = character.Transform.StickyParent?.GameObject;
        character.Transform.StickyParent = null;
        character.BroadcastPacket(new SCUnhungPacket(unitObjId, target?.ObjId ?? 0, reason), false);

        if (target is Slave stickySlave)
            ShipHarpoonRopeController.BreakRopeForClients(stickySlave, cutouted: false);
    }
}
