using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSelectHighAbilityPacket() : GamePacket(CSOffsets.CSSelectHighAbilityPacket, 5)
{
    public override void Read(PacketStream stream)
    {
        var isNpcSwap = stream.ReadBoolean();
        var objId = stream.ReadBc();
        var oldKind = stream.ReadByte();
        var oldAbility = (AbilityType)stream.ReadByte();
        var newKind = stream.ReadByte();
        var newAbility = (AbilityType)stream.ReadByte();
        var character = Connection?.ActiveChar;
        if (character != null && IsValidSource(character, isNpcSwap, objId))
            character.Abilities?.TrySelectHighAbility(oldKind, oldAbility, newKind, newAbility);
    }

    internal static bool IsValidSource(Character character, bool isNpcSwap, uint objId)
    {
        if (objId == character.ObjId)
            return !isNpcSwap;

        return character.ParentWorld?.GetUnit(objId) is Npc { Template.AbilityChanger: true, IsDead: false } npc &&
               character.GetDistanceTo(npc, true) <= 25f;
    }
}
