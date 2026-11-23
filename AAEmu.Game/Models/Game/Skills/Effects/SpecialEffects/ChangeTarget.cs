using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ChangeTarget : SpecialEffectAction
{
    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is not Unit newTarget || target is not Unit affected)
            return;

        affected.CurrentTarget = newTarget;
        var packet = new SCTargetChangedPacket(affected.ObjId, newTarget.ObjId);
        if (affected is Character character)
            character.SendPacket(packet);
        else
            affected.BroadcastPacket(packet, true);
    }
}
