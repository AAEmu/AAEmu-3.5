using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class DoodadItemChangeEffect : EffectTemplate
{
    public int Idx { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (caster is not Character player || source?.Skill is not { } skill)
            return;

        var result = (target as Doodad)?.ApplyItemChange(player, skill.Template, Idx) ?? SkillResult.InvalidTarget;
        if (result == SkillResult.Success)
            return;

        skill.Cancelled = true;
        player.SendErrorMessage(GetInteractionError(result));
    }

    internal static ErrorMessageType GetInteractionError(SkillResult result) => result switch
    {
        SkillResult.NeedReagent => ErrorMessageType.NotEnoughRequiredItem,
        SkillResult.NoPerm => ErrorMessageType.InteractionPermissionDeny,
        SkillResult.TooFarRange => ErrorMessageType.TooFarAway,
        _ => ErrorMessageType.NoInteractionAvailable
    };
}
