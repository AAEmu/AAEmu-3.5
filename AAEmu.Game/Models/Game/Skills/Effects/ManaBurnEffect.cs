using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class ManaBurnEffect : EffectTemplate
{
    public int BaseMin { get; set; }
    public int BaseMax { get; set; }
    public int DamageRatio { get; set; }
    public float LevelMd { get; set; }
    public int LevelVaStart { get; set; }
    public int LevelVaEnd { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Trace("ManaBurnEffect");

        if (caster is not Unit casterUnit || target is not Unit { Hp: > 0 } targetUnit)
            return;
        if (targetUnit.Buffs.HasEffectsMatchingCondition(static buff => buff is { InUse: true, Template.ManaBurnImmune: true }))
            return;

        var lvlMd = casterUnit.LevelDps * LevelMd;
        var levelModifier = (((source?.Skill?.GetEffectLevel(caster) ?? 1) - 1) / 49f * (LevelVaEnd - LevelVaStart) + LevelVaStart) * 0.01f;
        var min = BaseMin + lvlMd - levelModifier * lvlMd + 0.5f;
        var max = BaseMax + (levelModifier + 1) * lvlMd + 0.5f;

        if (source?.Buff?.TickEffects.Count > 0 && source.Buff.Duration != 0)
        {
            min = (float)(min * (source.Buff.Tick / source.Buff.Duration));
            max = (float)(max * (source.Buff.Tick / source.Buff.Duration));
        }

        var charge = (int)Random.Shared.Next(min, max);
        if (charge <= 0 || targetUnit.Mp <= 0)
            return;

        var previousMana = targetUnit.Mp;
        targetUnit.ReduceCurrentMp(casterUnit, charge);
        var burnedMana = Math.Max(0, previousMana - targetUnit.Mp);

        // damage_ratio expresses health damage as a per-ten-thousand share of the mana lost.
        var healthDamage = (int)Math.Min((long)burnedMana * Math.Max(0, DamageRatio) / 10000, int.MaxValue);
        if (healthDamage > 0)
            targetUnit.ReduceCurrentHp(casterUnit, healthDamage);

        var packet = new SCUnitDamagedPacket(castObj, casterObj, casterUnit.ObjId, targetUnit.ObjId, healthDamage, 0)
        {
            _manaBurn = burnedMana
        };
        targetUnit.BroadcastPacket(packet, true);
    }
}
