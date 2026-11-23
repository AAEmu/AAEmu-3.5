using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class ConversionEffect : EffectTemplate
{
    public uint CategoryId { get; set; }
    public uint SourceCategoryId { get; set; }
    public int SourceValue { get; set; }
    public uint TargetCategoryId { get; set; }
    public int TargetValue { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (target is not Unit { Hp: > 0 } unit || CategoryId is not (1 or 2))
            return;

        var fromMana = CategoryId == 1;
        var sourceCurrent = fromMana ? unit.Mp : unit.Hp;
        if (sourceCurrent <= 0)
            return;

        var sourceMax = fromMana ? unit.MaxMp : unit.MaxHp;
        var taken = SourceCategoryId switch
        {
            1 => SourceValue,
            2 => EffectResourceMath.PercentageOfPool(sourceMax, SourceValue),
            3 => EffectResourceMath.PercentageOfPool(sourceCurrent, SourceValue),
            _ => 0
        };
        taken = Math.Min(taken, sourceCurrent);
        if (taken <= 0)
            return;

        var targetMax = fromMana ? unit.MaxHp : unit.MaxMp;
        var targetCurrent = fromMana ? unit.Hp : unit.Mp;
        var given = TargetCategoryId switch
        {
            1 => TargetValue,
            2 => EffectResourceMath.PercentageOfPool(targetMax, TargetValue),
            3 => EffectResourceMath.PercentageOfPool(targetCurrent, TargetValue),
            4 => EffectResourceMath.PercentageOfPool(taken, TargetValue),
            _ => 0
        };
        var resultingTarget = (int)Math.Min((long)targetCurrent + Math.Max(0, given), targetMax);
        var previousHealth = unit.Hp;

        if (fromMana)
        {
            unit.Mp -= taken;
            unit.Hp = resultingTarget;
        }
        else
        {
            unit.Hp -= taken;
            unit.Mp = resultingTarget;
        }

        unit.BroadcastPacket(new SCUnitPointsPacket(unit.ObjId, unit.Hp, unit.Mp, unit.HighAbilityRsc), true);
        if (previousHealth != unit.Hp)
            unit.PostUpdateCurrentHp(caster, previousHealth, unit.Hp);
    }
}
