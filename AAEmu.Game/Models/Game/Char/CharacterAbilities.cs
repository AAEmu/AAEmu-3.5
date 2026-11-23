using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterAbilities
{
    private readonly Lock _highAbilityLock = new();
    private readonly Lock _specialAbilityLock = new();
    public Dictionary<AbilityType, Ability> Abilities { get; set; }
    public Character Owner { get; set; }

    public CharacterAbilities(Character owner)
    {
        Owner = owner;
        Abilities = [];
        for (var i = 1; i < 13; i++) //1.2 = 10 ability, 3.0.3.0 = 12 ability, 5.0 = 29 ability
        {
            var id = (AbilityType)i;
            Abilities[id] = new Ability(id);
        }
    }

    public IEnumerable<Ability> Values => Abilities.Values;

    internal static bool IsSpecialAbility(AbilityType type) => type is AbilityType.Predator or AbilityType.Trooper;

    internal bool TryLearnSpecialAbility(AbilityType type, uint catalystId, int catalystCount)
    {
        if (!IsSpecialAbility(type) || catalystId == 0 || catalystCount <= 0)
            return false;

        lock (_specialAbilityLock)
        {
            if (!Abilities.TryGetValue(type, out var ability) || ability.Exp > 0)
                return false;

            var experience = Math.Min(Owner.Experience, ExperienceManager.Instance.GetExpForLevel(Owner.Level));
            if (experience <= 0 || Owner.Inventory?.Bag?.TryConsumeExact(ItemTaskType.SkillEffectConsumption, catalystId, catalystCount) != true)
                return false;

            ability.Exp = experience;
            return true;
        }
    }

    internal bool CanUseSpecialAbilitySkill(SkillTemplate template) =>
        (template.CharRaceId == 0 || template.CharRaceId == (int)Owner.Race) &&
        GetLearnedSpecialAbilityLevel(template.AbilityId) >= Math.Max(1, template.AbilityLevel);

    internal int GetLearnedSpecialAbilityLevel(AbilityType type) =>
        Abilities.TryGetValue(type, out var ability) && ability.Exp > 0
            ? ExperienceManager.Instance.GetLevelFromExp(ability.Exp, out _)
            : 0;

    public void SetAbility(AbilityType id, byte order)
    {
        Abilities[id].Order = order;
    }

    internal bool TrySelectHighAbility(byte oldKind, AbilityType oldAbility, byte newKind, AbilityType newAbility,
        bool isSwapAbilitySet = false)
    {
        lock (_highAbilityLock)
        {
            if (oldKind > 1 || newKind > 1 || (oldKind == 0 && newKind == 0))
                return false;
            if (oldKind == 1 && !SkillManager.Instance.TryGetHighAbility(oldAbility, oldKind, out _))
                return false;
            if (newKind == 1 &&
                (Owner.Ability1 is AbilityType.None or AbilityType.General ||
                 Owner.Ability2 is AbilityType.None or AbilityType.General ||
                 Owner.Ability3 is AbilityType.None or AbilityType.General ||
                 Owner.Ability1 == Owner.Ability2 || Owner.Ability1 == Owner.Ability3 || Owner.Ability2 == Owner.Ability3))
                return false;

            var abilities = new[] { Owner.HighAbility1, Owner.HighAbility2, Owner.HighAbility3 };
            var oldSlot = -1;
            var freeSlot = -1;
            for (var index = 0; index < abilities.Length; index++)
            {
                if (abilities[index] is AbilityType.None or AbilityType.General)
                    freeSlot = freeSlot < 0 ? index : freeSlot;
                else if (oldKind == 0)
                    return false;
                if (oldKind == 1 && abilities[index] == oldAbility)
                    oldSlot = index;
                if (newKind == 1 && abilities[index] == newAbility && !(oldKind == 1 && oldAbility == newAbility))
                    return false;
            }

            if (oldKind == 1 && oldSlot < 0)
                return false;
            if (newKind == 1 &&
                (!SkillManager.Instance.TryGetHighAbility(newAbility, newKind, out var data) ||
                 !UnitRequirementsGameData.Instance.CanActivateHighAbility(data.Id, data.OrUnitReqs, Owner)))
                return false;

            var slot = oldKind == 1 ? oldSlot : freeSlot;
            if (slot < 0)
                return false;
            if (oldKind == 1 && newKind == 1)
            {
                if (oldAbility == newAbility)
                    return false;
                var formula = FormulaManager.Instance.GetFormula((uint)FormulaKind.SwapAbilityCost);
                if (formula == null)
                    return false;
                var amount = formula.Evaluate(new Dictionary<string, double>
                {
                    ["pc_level"] = Owner.Level,
                    ["config_swap_ability_cost"] = 600
                });
                if (!double.IsFinite(amount) || amount < 0 || amount > int.MaxValue)
                    return false;
                var cost = (int)Math.Round(amount);
                if (cost > 0 && !Owner.ChangeMoney(SlotType.Inventory, SlotType.None, cost, ItemTaskType.AbilityChange))
                    return false;
            }
            abilities[slot] = newKind == 1 ? newAbility : AbilityType.None;
            Owner.HighAbility1 = abilities[0];
            Owner.HighAbility2 = abilities[1];
            Owner.HighAbility3 = abilities[2];

            if (Owner.HighAbilityRsc > Owner.MaxHighAbilityRsc)
                Owner.ChangeHighAbilityResource(Owner.MaxHighAbilityRsc - Owner.HighAbilityRsc);
            Owner.BroadcastPacket(new SCHighAbilityActivedPacket(Owner.ObjId, oldKind, oldAbility, newKind, newAbility,
                isSwapAbilitySet), true);
            return true;
        }
    }

    public List<AbilityType> GetActiveAbilities()
    {
        var list = new List<AbilityType>();
        if (Owner.Ability1 != AbilityType.None)
            list.Add(Owner.Ability1);
        if (Owner.Ability2 != AbilityType.None)
            list.Add(Owner.Ability2);
        if (Owner.Ability3 != AbilityType.None)
            list.Add(Owner.Ability3);
        return list;
    }

    internal (AbilityType First, AbilityType Second, AbilityType Third) CaptureHighAbilities()
    {
        lock (_highAbilityLock)
            return (Owner.HighAbility1, Owner.HighAbility2, Owner.HighAbility3);
    }

    internal bool IsHighAbilityActive(int kind, AbilityType ability)
    {
        if (kind != 1)
            return false;

        lock (_highAbilityLock)
        {
            if (ability == AbilityType.General)
                return Owner.HighAbility1 is not (AbilityType.None or AbilityType.General) ||
                       Owner.HighAbility2 is not (AbilityType.None or AbilityType.General) ||
                       Owner.HighAbility3 is not (AbilityType.None or AbilityType.General);
            return ability != AbilityType.None &&
                   (Owner.HighAbility1 == ability || Owner.HighAbility2 == ability || Owner.HighAbility3 == ability);
        }
    }

    public void AddExp(AbilityType type, int exp)
    {
        // TODO SCAbilityExpChangedPacket
        if (type != AbilityType.None)
            Abilities[type].Exp += exp;
    }

    public void AddActiveExp(int exp)
    {
        // TODO SCExpChangedPacket
        var maxLevelExp = ExperienceManager.Instance.GetExpForLevel(ExperienceManager.Instance.MaxPlayerLevel);
        if (Owner.Ability1 != AbilityType.None)
            Abilities[Owner.Ability1].Exp = Math.Min(Abilities[Owner.Ability1].Exp + exp, maxLevelExp);
        if (Owner.Ability2 != AbilityType.None)
            Abilities[Owner.Ability2].Exp = Math.Min(Abilities[Owner.Ability2].Exp + exp, maxLevelExp);
        if (Owner.Ability3 != AbilityType.None)
            Abilities[Owner.Ability3].Exp = Math.Min(Abilities[Owner.Ability3].Exp + exp, maxLevelExp);
    }

    internal void RaiseActiveExperienceTo(int total)
    {
        foreach (var type in GetActiveAbilities())
        {
            if (!Abilities.TryGetValue(type, out var ability) || ability.Exp >= total)
                continue;

            var delta = total - ability.Exp;
            ability.Exp = total;
            Owner.SendPacket(new SCAbilityExpChangedPacket(Owner.ObjId, type, delta));
        }
    }

    public void Swap(AbilityType oldAbilityId, AbilityType abilityId)
    {
        if (IsSpecialAbility(abilityId))
            return;

        Owner.Skills.Reset(oldAbilityId);
        if (Owner.Ability1 == oldAbilityId)
        {
            Owner.Ability1 = abilityId;
            Abilities[abilityId].Order = 0;
        }
        else if (Owner.Ability2 == oldAbilityId)
        {
            Owner.Ability2 = abilityId;
            Abilities[abilityId].Order = 1;

            //This sets are current ability level to match ability1 since its suppost to be in sync
            if (oldAbilityId == AbilityType.None)
            {
                Abilities[Owner.Ability2].Exp = Abilities[Owner.Ability1].Exp;
            }
        }
        else if (Owner.Ability3 == oldAbilityId)
        {
            Owner.Ability3 = abilityId;
            Abilities[abilityId].Order = 2;

            if (oldAbilityId == AbilityType.None)
            {
                Abilities[Owner.Ability3].Exp = Abilities[Owner.Ability1].Exp;

                //every unchosen ability is default level 10 besides are selected ones since spillover exp can unsync character exp with skill exp
                var c = GetActiveAbilities();
                for (var i = 1; i < (int)AbilityType.Predator; i++)
                {
                    var id = (AbilityType)i;
                    if (!c.Contains(Abilities[id].Id))
                    {
                        Abilities[id].Exp = 42000;
                    }
                }
            }
        }

        if (oldAbilityId != AbilityType.None)
            Abilities[oldAbilityId].Order = 255;
        Owner.BroadcastPacket(new SCAbilitySwappedPacket(Owner.ObjId, oldAbilityId, abilityId), true);
    }

    public void Load(MySqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM abilities WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var ability = new Ability
                    {
                        Id = (AbilityType)reader.GetByte("id"),
                        Exp = reader.GetInt32("exp")
                    };
                    if (ability.Id == Owner.Ability1)
                        ability.Order = 0;
                    if (ability.Id == Owner.Ability2)
                        ability.Order = 1;
                    if (ability.Id == Owner.Ability3)
                        ability.Order = 2;
                    Abilities[ability.Id] = ability;
                }
            }
        }

        using var highCommand = connection.CreateCommand();
        highCommand.CommandText = "SELECT ability1, ability2, ability3 FROM character_high_abilities WHERE owner = @owner";
        highCommand.Parameters.AddWithValue("@owner", Owner.Id);
        using var highReader = highCommand.ExecuteReader();
        if (highReader.Read())
        {
            Owner.HighAbility1 = (AbilityType)highReader.GetByte(0);
            Owner.HighAbility2 = (AbilityType)highReader.GetByte(1);
            Owner.HighAbility3 = (AbilityType)highReader.GetByte(2);
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        foreach (var ability in Abilities.Values)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = "REPLACE INTO abilities(`id`,`exp`,`owner`) VALUES (@id, @exp, @owner)";
                command.Parameters.AddWithValue("@id", (byte)ability.Id);
                command.Parameters.AddWithValue("@exp", ability.Exp);
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.ExecuteNonQuery();
            }
        }


        var high = CaptureHighAbilities();
        using var highCommand = connection.CreateCommand();
        highCommand.Transaction = transaction;
        highCommand.CommandText = """
            INSERT INTO character_high_abilities (owner, ability1, ability2, ability3)
            VALUES (@owner, @first, @second, @third)
            ON DUPLICATE KEY UPDATE ability1 = @first, ability2 = @second, ability3 = @third
            """;
        highCommand.Parameters.AddWithValue("@owner", Owner.Id);
        highCommand.Parameters.AddWithValue("@first", (byte)high.First);
        highCommand.Parameters.AddWithValue("@second", (byte)high.Second);
        highCommand.Parameters.AddWithValue("@third", (byte)high.Third);
        highCommand.ExecuteNonQuery();
    }

    public byte GetAbilityLevel(AbilityType abilityType)
    {
        return Abilities.TryGetValue(abilityType, out var ability) ? ExperienceManager.Instance.GetLevelFromExp(ability.Exp, out _) : (byte)0;
    }
}
