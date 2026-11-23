using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class SetVariable : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.SetVariable;

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
        var state = skill?.ActivePlotState;
        if (state == null)
        {
            Logger.Error("No active plot state located.");
            return;
        }
        if (value1 < 0 || value1 >= state.Variables.Length)
        {
            Logger.Error("SetVariable index {0} out of range", value1);
            return;
        }

        switch (value3)
        {
            case 1:
                state.Variables[value1] += value2;
                break;
            case 11:
                state.Variables[value1] = value2;
                break;
            case 12:
                state.Variables[value1] = state.LastEffectedTargetCount;
                break;
            default:
                Logger.Error("Invalid Plot Variable Operation Kind {0}.", value3);
                break;
        }
    }
}
