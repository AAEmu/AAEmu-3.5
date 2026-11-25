using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class RestoreManaEffect : EffectTemplate
{
    public bool UseFixedValue { get; set; }
    public int FixedMin { get; set; }
    public int FixedMax { get; set; }
    public bool UseLevelValue { get; set; }
    public float LevelMd { get; set; }
    public int LevelVaStart { get; set; }
    public int LevelVaEnd { get; set; }
    public bool Percent { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Trace("RestoreManaEffect");

        if (target is not Unit trg)
            return;

        var value = Percent && UseFixedValue
            ? EffectResourceMath.RollPercentageOfPool(trg.MaxMp, FixedMin, FixedMax)
            : RollAbsoluteMana(caster, source);
        trg.BroadcastPacket(new SCUnitHealedPacket(castObj, casterObj, trg.ObjId, HealType.Mana, HealHitType.HealHit, value), true);
        trg.Mp = (int)Math.Clamp((long)trg.Mp + value, 0, trg.MaxMp);
        trg.BroadcastPacket(new SCUnitPointsPacket(trg.ObjId, trg.Hp, trg.Mp, trg.HighAbilityRsc), true);
    }

    private int RollAbsoluteMana(BaseUnit caster, EffectSource source)
    {
        var min = 0;
        var max = 0;
        if (UseFixedValue)
        {
            min += FixedMin;
            max += FixedMax;
        }

        var levelBonus = 0f;
        var skillLevel = 1;
        if (source?.Skill is { } skill)
        {
            skillLevel = (skill.GetEffectLevel(caster) - 1) * skill.Template.LevelStep + skill.Template.AbilityLevel;
            if (skillLevel >= skill.Template.AbilityLevel)
                levelBonus = 0.15f * (skillLevel - skill.Template.AbilityLevel + 1);
        }

        if (UseLevelValue && caster is Unit unit)
        {
            var levelMd = (levelBonus + 1) * LevelMd;
            min += (int)(unit.LevelDps * levelMd + 0.5f);
            max += (int)((((skillLevel - 1) * 0.020408163f * (LevelVaEnd - LevelVaStart) + LevelVaStart) * 0.0099999998f + 1f) *
                         unit.LevelDps * levelMd + 0.5f);
        }

        return Random.Shared.Next(Math.Min(min, max), Math.Max(min, max));
    }
}
