#nullable enable

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Models.Game.Slaves;

/// <summary>Applies in-process hull contacts using AAC 3.5 formula 28 and the struck face's authored gain.</summary>
public static class SlaveCollisionDamage
{
    private const float DefaultMass = 1000f;

    /// <summary>Unknown zone groups retain damage; authored groups can disable it.</summary>
    public static bool AllowsDamage(ZoneGroup? group) => group?.EnablePhysicsCollisionDamage != false;

    /// <summary>Fallback for AAC 3.5 formula 28 before integer rounding.</summary>
    public static double RawDamage(double impact, double mass, double partGain, double equipGain, double armorGain)
    {
        if (!double.IsFinite(impact) || impact <= 0d || !double.IsFinite(mass) || mass <= 0d ||
            !double.IsFinite(partGain) || partGain <= 0d || !double.IsFinite(equipGain) || equipGain <= 0d ||
            !double.IsFinite(armorGain) || armorGain <= 0d)
            return 0d;

        return 0.000002d * (impact * Math.Pow(mass, 1.6d)) * partGain * equipGain / armorGain;
    }

    /// <summary>A positive glancing contact deals at least one HP, and large contacts cannot overflow an int.</summary>
    public static int RoundedDamage(double raw)
    {
        if (double.IsNaN(raw) || raw <= 0d)
            return 0;
        if (raw >= int.MaxValue)
            return int.MaxValue;
        return Math.Max(1, (int)Math.Round(raw));
    }

    public static void ApplyFromImpact(Slave slave, byte part, float impact, float ownMass, BaseUnit? attacker = null)
    {
        if (slave == null || slave.IsDead || slave.Template?.IsABoat() != true || !float.IsFinite(impact) || impact <= 0f)
            return;

        var zone = ZoneManager.Instance.GetZoneByKey(slave.Transform?.ZoneId ?? 0);
        var group = zone == null ? null : ZoneManager.Instance.GetZoneGroupById(zone.GroupId);
        if (!AllowsDamage(group))
            return;

        if (!float.IsFinite(ownMass) || ownMass < 1f)
            ownMass = slave.ShipModel?.Mass ?? ModelManager.Instance.GetShipModel(slave.ModelId)?.Mass ?? DefaultMass;
        if (!float.IsFinite(ownMass) || ownMass < 1f)
            ownMass = DefaultMass;

        var desc = SlaveGameData.Instance.GetCollisionDamageDesc(slave.Template.SlaveCollisionDamageId);
        var partGain = desc?.GainFor(part) ?? 1f;
        var equipGain = PercentGain(slave, UnitAttribute.PhysicsCollisionDamageMul);
        var armorGain = Math.Max(0.01d, PercentGain(slave, UnitAttribute.PhysicsCollisionArmorMul));
        if (!float.IsFinite(partGain) || partGain <= 0f || equipGain <= 0d)
            return;

        var formula = FormulaManager.Instance.GetFormula((uint)FormulaKind.PhysicsCollisionDamage);
        var raw = formula?.Evaluate(new Dictionary<string, double>(5)
        {
            ["impact"] = impact,
            ["mass"] = ownMass,
            ["part_gain"] = partGain,
            ["equip_gain"] = equipGain,
            ["armor_gain"] = armorGain
        }) ?? RawDamage(impact, ownMass, partGain, equipGain, armorGain);
        var damage = RoundedDamage(raw);
        if (damage == 0)
            return;

        var oldHp = slave.Hp;
        slave.ReduceCurrentHp(attacker ?? slave, damage);
        var dealt = oldHp - slave.Hp;
        if (dealt > 0)
        {
            slave.BroadcastPacket(new SCEnvDamagePacket(EnvSource.Collision, slave.ObjId, (uint)dealt), true);
            slave.Events.OnDamagedCollision(slave, new OnDamagedCollisionArgs
            {
                Attacker = attacker,
                Receiver = slave,
                Amount = dealt,
                Impact = impact
            });
        }
    }

    private static double PercentGain(Slave slave, UnitAttribute attribute)
    {
        var gain = slave.CalculateWithBonuses(100d, attribute) / 100d;
        return double.IsFinite(gain) ? Math.Max(0d, gain) : 0d;
    }
}
