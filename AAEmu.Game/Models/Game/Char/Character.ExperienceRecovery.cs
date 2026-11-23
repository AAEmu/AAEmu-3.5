using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.Game.Models.Game.Char;

public partial class Character
{
    public int PenaltiedExp { get; private set; }

    internal (byte Level, int Experience, uint HeirExp, int RecoverableExp, int PenaltiedExp) CaptureExperienceState()
    {
        lock (_experienceLock)
            return (Level, Experience, HeirExp, RecoverableExp, Math.Max(PenaltiedExp, RecoverableExp));
    }

    internal void ApplyDeathExperiencePenalty(BaseUnit killer, KillReason reason)
    {
        if (reason is not (KillReason.Damage or KillReason.Fall or KillReason.Collide or KillReason.Drowning) ||
            IsInDuel || (killer?.GetOwnerCharacter() is { } player && !ReferenceEquals(player, this)) ||
            Buffs.HasEffectsMatchingCondition(static buff => buff.InUse && !buff.IsEnded() && buff.Template.NoExpPenalty))
            return;

        lock (_experienceLock)
        {
            var floor = ExperienceManager.Instance.GetExpForLevel(Level);
            var progress = Math.Max(0, Experience - floor);
            if (progress == 0)
                return;

            var formulas = FormulaManager.Instance;
            var parameters = new Dictionary<string, double> { ["experience"] = progress };
            var lost = EvaluateDeathExperience(formulas.GetFormula((uint)FormulaKind.PenaltyExp), parameters,
                CalculateWithBonuses(100d, UnitAttribute.PenaltyExpMul), progress);
            if (lost == 0)
                return;

            var recoverable = EvaluateDeathExperience(formulas.GetFormula((uint)FormulaKind.RecoverableExp), parameters,
                CalculateWithBonuses(100d, UnitAttribute.RecoverableExpMul), lost);
            PenaltiedExp = (int)Math.Min(int.MaxValue, (long)Math.Max(PenaltiedExp, RecoverableExp) + lost);
            RecoverableExp = (int)Math.Min(PenaltiedExp, (long)RecoverableExp + recoverable);
            Experience -= lost;
            SendPacket(new SCExpChangedPacket(ObjId, -lost, false));
            SendPacket(new SCRecoverableExpPacket(ObjId, RecoverableExp, PenaltiedExp, 0));
        }
    }

    private static int EvaluateDeathExperience(Formula formula, Dictionary<string, double> parameters,
        double percent, int limit)
    {
        if (formula == null)
            return 0;
        var amount = formula.Evaluate(parameters) * percent / 100d;
        return double.IsFinite(amount) ? (int)Math.Clamp(Math.Round(amount), 0, limit) : 0;
    }

    internal bool TryRecoverExperience(bool needMoney, bool needLabor, bool fullPenalty)
    {
        lock (_experienceLock)
        {
            // Rows saved before the full penalty was tracked still retain the recoverable portion.
            var totalLost = Math.Max(PenaltiedExp, RecoverableExp);
            var requested = fullPenalty ? totalLost : RecoverableExp;
            if (requested <= 0)
                return false;
            var manager = ExperienceManager.Instance;
            var capacity = manager.GetExpForLevel(manager.MaxPlayerLevel) - Experience;
            var experience = Math.Min(requested, capacity);
            if (experience <= 0)
                return false;

            var labor = needLabor ? GetRecoveryCost(FormulaKind.LaborPowerForRecoverExp) : 0;
            var money = needMoney ? GetRecoveryCost(FormulaKind.MoneyForRecoverExp) : 0;
            if (needLabor && labor <= 0 || money < 0 || labor > short.MaxValue)
                return false;
            if (labor > LaborPower)
            {
                SendErrorMessage(ErrorMessageType.NotEnoughLaborPower);
                return false;
            }
            if (money > 0 && !ChangeMoney(SlotType.Inventory, SlotType.None, money, ItemTaskType.RecoverExp))
                return false;

            if (labor > 0)
            {
                // A priest's recovery fee does not perform a proficiency action or earn labor XP.
                LaborPower -= (short)labor;
                SendPacket(new SCCharacterLaborPowerChangedPacket((short)-labor, 0, 0, 0));
            }

            PenaltiedExp = totalLost - experience;
            RecoverableExp = Math.Max(0, RecoverableExp - experience);
            AddExpCore(experience, shouldAddAbilityExp: false, useExpRate: false);
            SendPacket(new SCRecoverableExpPacket(ObjId, RecoverableExp, PenaltiedExp, fullPenalty ? 2 : 1));
            return true;
        }
    }

    private int GetRecoveryCost(FormulaKind kind)
    {
        var formula = FormulaManager.Instance.GetFormula((uint)kind);
        if (formula == null)
            return -1;

        var cost = formula.Evaluate(new Dictionary<string, double> { ["pc_level"] = Level });
        return cost is >= 0 and <= int.MaxValue ? (int)Math.Round(cost) : -1;
    }
}
