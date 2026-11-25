using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Static;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSUnitAttachedPacket() : GamePacket(CSOffsets.CSUnitAttachedPacket, 5)
{
    public override void Read(PacketStream stream)
    {
        var unitObjId = stream.ReadBc();
        var targetObjId = stream.ReadBc();

        var character = Connection.ActiveChar;
        if (character == null || character.ObjId != unitObjId || character.IsDead || character.IsRiding ||
            character.AttachedPoint != AttachPointKind.None ||
            character.ParentWorld?.GetDoodad(targetObjId) is not { IsVisible: true } doodad ||
            doodad.Despawn != DateTime.MinValue || character.GetDistanceTo(doodad, true) > 25f)
            return;

        if (!doodad.CurrentFuncs.Exists(func => func.FuncType == nameof(DoodadFuncClimb)) ||
            ReferenceEquals(character.Transform.StickyParent, doodad.Transform))
            return;

        character.Transform.StickyParent = doodad.Transform;
        character.BroadcastPacket(new SCHungPacket(unitObjId, targetObjId), false);
    }
}
