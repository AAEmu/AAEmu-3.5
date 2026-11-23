using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class HighAbilityResourceEffect : EffectTemplate
{
    public int MaxHighAbilityResource { get; set; }
    public int MinHighAbilityResource { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if ((target as Unit ?? caster as Unit) is not { } unit)
            return;

        var minimum = Math.Min(MinHighAbilityResource, MaxHighAbilityResource);
        var maximum = Math.Max(MinHighAbilityResource, MaxHighAbilityResource);
        var amount = minimum == maximum ? minimum : (int)Random.Shared.NextInt64(minimum, (long)maximum + 1);
        unit.ChangeHighAbilityResource(amount);
    }
}
