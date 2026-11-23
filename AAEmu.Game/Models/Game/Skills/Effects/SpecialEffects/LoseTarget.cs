using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class LoseTarget : SpecialEffectAction
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
        if (target is Unit unit)
            ClearFor(unit);
    }

    internal static void ClearFor(Unit unit)
    {
        unit.CurrentTarget = null;
        var packet = new SCTargetChangedPacket(unit.ObjId, 0);
        if (unit is Character character)
            character.SendPacket(packet);
        else
            unit.BroadcastPacket(packet, true);
    }
}
