using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units.Static;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterMates(Character owner)
{
    internal const uint RecoveryPotionSkillId = 15220;
    private const uint GoldenWyvernNpcId = 16861;
    private static readonly TimeSpan s_defaultRecovery = TimeSpan.FromMinutes(5);
    private readonly Lock _stateLock = new();

    /*
     * TODO:
     * EQUIPMENT CHANGE
     * FINISH ATTRIBUTES
     * NAME FROM LOCALIZED TABLE
     */

    private Character Owner { get; set; } = owner;

    private readonly Dictionary<ulong, MateDb> _mates = []; // itemId, MountDb
    private readonly List<uint> _removedMates = [];

    public MateDb GetMateInfo(ulong itemId)
    {
        lock (_stateLock)
            return _mates.GetValueOrDefault(itemId);
    }

    private MateDb CreateNewMate(ulong itemId, NpcTemplate npcTemplate)
    {
        if (_mates.ContainsKey(itemId)) return null;
        var template = new MateDb
        {
            // TODO
            Id = MateIdManager.Instance.GetNextId(),
            ItemId = itemId,
            Level = npcTemplate.Level,
            Name = LocalizationManager.Instance.Get("npcs", "name", npcTemplate.Id, npcTemplate.Name), // npcTemplate.Name,
            Owner = Owner.Id,
            Mileage = 0,
            Xp = ExperienceManager.Instance.GetExpForLevel(npcTemplate.Level, true),
            Hp = 9999,
            Mp = 9999,
            UpdatedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _mates.Add(template.ItemId, template);
        return template;
    }

    public void SpawnMount(SkillItem skillData)
    {
        lock (_stateLock)
            SpawnMountCore(skillData);
    }

    private void SpawnMountCore(SkillItem skillData)
    {
        if (FindOwnedBagItem(skillData.ItemId) is not SummonMate { Template: SummonMateTemplate itemTemplate } item)
            return;

        var now = DateTime.UtcNow;
        if (item.RepairStartTime > now)
        {
            Owner.SendErrorMessage(ErrorMessageType.MateDead);
            return;
        }

        var despawnedOldPets = false;
        // Check if we had already spawned something
        foreach (var oldMate in Owner.ParentWorld.MateManager.GetActiveMates(Owner.Id))
        {
            DespawnMate(oldMate.TlId);
            despawnedOldPets = true;
        }
        if (despawnedOldPets)
            return;

        var npcId = itemTemplate.NpcId;
        var template = NpcManager.Instance.GetTemplate(npcId);
        if (template == null)
            return;
        var tlId = (ushort)TlIdManager.Instance.GetNextId();
        var objId = ObjectIdManager.Instance.GetNextId();
        var mateDbInfo = GetMateInfo(skillData.ItemId) ?? CreateNewMate(skillData.ItemId, template);
        if (item.RepairStartTime != DateTime.MinValue)
            CompleteRecovery(item, mateDbInfo, now);

        var mount = new Units.Mate
        {
            ObjId = objId,
            TlId = tlId,
            OwnerId = Owner.Id,
            Name = mateDbInfo.Name,
            TemplateId = template.Id,
            Template = template,
            ModelId = template.ModelId,
            Faction = Owner.Faction,
            Level = (byte)mateDbInfo.Level,
            Hp = mateDbInfo.Hp > 0 ? mateDbInfo.Hp : 100,
            Mp = mateDbInfo.Mp > 0 ? mateDbInfo.Mp : 100,
            OwnerObjId = Owner.ObjId,
            Id = mateDbInfo.Id,
            ItemId = mateDbInfo.ItemId,
            UserState = 1, // TODO
            Experience = mateDbInfo.Xp,
            Mileage = mateDbInfo.Mileage,
            SpawnDelayTime = 0, // TODO
            DbInfo = mateDbInfo
        };

        mount.Transform = Owner.Transform.CloneDetached(mount);
        SusManager.Instance.ResetAnalyzeMountDeltaMovement(mount.Id);

        foreach (var skill in MateGameData.Instance.GetMateSkills(npcId))
            mount.Skills.Add(skill);

        foreach (var buffId in template.Buffs)
        {
            var buff = SkillManager.Instance.GetBuffTemplate(buffId);
            if (buff == null)
                continue;

            var obj = new SkillCasterUnit(mount.ObjId);
            buff.Apply(mount, obj, mount, null, null, new EffectSource(), null, DateTime.UtcNow);
        }

        mount.Equipment = ItemManager.Instance.GetItemContainerForCharacter(Owner.Id, SlotType.EquipmentMate, mount, mount.Id);
        mount.UpdateGearBonuses(null, null);

        // Cap stats to their max
        mount.Hp = Math.Min(mount.Hp, mount.MaxHp);
        mount.Mp = Math.Min(mount.Mp, mount.MaxMp);

        mount.Transform.Local.AddDistanceToFront(3f);
        //Logger.Warn($"Spawn the pet:{mount.ObjId} X={mount.Transform.World.Position.X} Y={mount.Transform.World.Position.Y}");
        Owner.ParentWorld.MateManager.AddActiveMateAndSpawn(Owner, mount, item);
        mount.PostUpdateCurrentHp(mount, 0, mount.Hp, KillReason.Unknown);
    }

    public void DespawnMate(uint tlId)
    {
        if (Owner.ParentWorld?.MateManager is { } manager)
            DespawnMate(manager, tlId);
    }

    internal void DespawnMate(MateManager manager, uint tlId)
    {
        lock (_stateLock)
            DespawnMateCore(manager, tlId);
    }

    private void DespawnMateCore(MateManager manager, uint tlId, DateTime? deathTime = null)
    {
        var mateInfo = manager.GetActiveOwnedMateByTlId(Owner.Id, tlId);
        if (mateInfo == null)
            return;

        var mateDbInfo = GetMateInfo(mateInfo.ItemId);
        var now = deathTime ?? DateTime.UtcNow;
        if (mateDbInfo != null)
        {
            mateDbInfo.Hp = mateInfo.Hp;
            mateDbInfo.Mp = mateInfo.Mp;
            mateDbInfo.Level = mateInfo.Level;
            mateDbInfo.Xp = mateInfo.Experience;
            mateDbInfo.Mileage = mateInfo.Mileage;
            mateDbInfo.Name = mateInfo.Name;
            mateDbInfo.UpdatedAt = now;
        }

        // The standard recovery introduced for other pets expressly excluded the Golden Wyvern.
        var injured = mateInfo.IsDead && mateInfo.TemplateId != GoldenWyvernNpcId
            ? Owner.Inventory?.GetItemById(mateInfo.ItemId) as SummonMate
            : null;
        if (injured?.OwnerId != Owner.Id)
            injured = null;
        if (injured != null && injured.RepairStartTime == DateTime.MinValue)
            injured.RepairStartTime = now.Add(s_defaultRecovery);

        manager.RemoveActiveMateAndDespawn(Owner, tlId);
        if (injured != null)
            NotifyMateItemUpdate(injured);
    }

    private Item FindOwnedBagItem(ulong itemId)
    {
        var bag = Owner.Inventory?.Bag;
        var item = bag?.GetItemByItemId(itemId);
        return item?.OwnerId == Owner.Id && item.SlotType == SlotType.Inventory &&
               ReferenceEquals(item.HoldingContainer, bag) && item.Count > 0 ? item : null;
    }

    private Units.Mate FindActiveMate(ulong itemId)
    {
        if (Owner.ParentWorld?.MateManager is not { } manager)
            return null;

        foreach (var mate in manager.GetActiveMates(Owner.Id))
            if (mate.ItemId == itemId)
                return mate;

        return null;
    }

    internal bool CanUseRecoveryPotion(ulong potionId, SkillCastTarget target, DateTime now)
    {
        lock (_stateLock)
            return CanUseRecoveryPotionCore(potionId, target, now, out _, out _, out _);
    }

    private bool CanUseRecoveryPotionCore(ulong potionId, SkillCastTarget target, DateTime now,
        out Item potion, out SummonMate summon, out MateDb mate)
    {
        potion = FindOwnedBagItem(potionId);
        summon = target is SkillCastItemTarget itemTarget ? FindOwnedBagItem(itemTarget.Id) as SummonMate : null;
        mate = summon != null ? _mates.GetValueOrDefault(summon.Id) : null;
        return potion?.Template is { UseSkillId: RecoveryPotionSkillId, UseSkillAsReagent: true } &&
               summon != null && summon.RepairStartTime > now && mate?.Owner == Owner.Id &&
               FindActiveMate(summon.Id) == null;
    }

    internal bool TryUseRecoveryPotion(ulong potionId, SkillCastTarget target, DateTime now)
    {
        lock (_stateLock)
        {
            if (!CanUseRecoveryPotionCore(potionId, target, now, out var potion, out var summon, out var mate) ||
                !potion.HoldingContainer.TryConsumeExact(ItemTaskType.SkillReagents, potion.TemplateId, 1, potion))
                return false;

            CompleteRecovery(summon, mate, now);
            NotifyMateItemUpdate(summon);
            Owner.SendErrorMessage(ErrorMessageType.MateResurrected);
            return true;
        }
    }

    private static void CompleteRecovery(SummonMate item, MateDb mate, DateTime now)
    {
        item.RepairStartTime = DateTime.MinValue;
        // The summoned unit caps these values to its computed, equipment-adjusted maxima.
        mate.Hp = int.MaxValue;
        mate.Mp = int.MaxValue;
        mate.UpdatedAt = now;
    }

    internal void RecordDeathAndDespawn(Units.Mate mate, DateTime now)
    {
        lock (_stateLock)
        {
            if (mate.ParentWorld?.MateManager is not { } manager ||
                !ReferenceEquals(manager.GetActiveOwnedMateByTlId(Owner.Id, mate.TlId), mate))
                return;

            DespawnMateCore(manager, mate.TlId, now);
        }
    }

    private void NotifyMateItemUpdate(SummonMate item) =>
        Owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.UpdateSummonMateItem, [new ItemUpdate(item)], []));

    /// <summary>
    /// Load pet data of the player
    /// </summary>
    /// <param name="connection"></param>
    public void Load(MySqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM mates WHERE `owner` = @owner";
        command.Parameters.AddWithValue("@owner", Owner.Id);
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var template = new MateDb
            {
                Id = reader.GetUInt32("id"),
                ItemId = reader.GetUInt64("item_id"),
                Name = reader.GetString("name"),
                Xp = reader.GetInt32("xp"),
                Level = reader.GetUInt16("level"),
                Mileage = reader.GetInt32("mileage"),
                Hp = reader.GetInt32("hp"),
                Mp = reader.GetInt32("mp"),
                Owner = reader.GetUInt32("owner"),
                UpdatedAt = reader.GetDateTime("updated_at"),
                CreatedAt = reader.GetDateTime("created_at")
            };
            _mates.Add(template.ItemId, template);
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        lock (_stateLock)
            SaveCore(connection, transaction);
    }

    private void SaveCore(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (_removedMates.Count > 0)
        {
            using var command = connection.CreateCommand();
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = $"DELETE FROM mates WHERE owner = @owner AND id IN({string.Join(",", _removedMates)})";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.Prepare();
            command.ExecuteNonQuery();
            _removedMates.Clear();
        }

        foreach (var (_, value) in _mates)
        {
            using var command = connection.CreateCommand();
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText =
                "REPLACE INTO mates(`id`,`item_id`,`name`,`xp`,`level`,`mileage`,`hp`,`mp`,`owner`,`updated_at`,`created_at`) " +
                "VALUES (@id, @item_id, @name, @xp, @level, @mileage, @hp, @mp, @owner, @updated_at, @created_at)";
            command.Parameters.AddWithValue("@id", value.Id);
            command.Parameters.AddWithValue("@item_id", value.ItemId);
            command.Parameters.AddWithValue("@name", value.Name);
            command.Parameters.AddWithValue("@xp", value.Xp);
            command.Parameters.AddWithValue("@level", value.Level);
            command.Parameters.AddWithValue("@mileage", value.Mileage);
            command.Parameters.AddWithValue("@hp", value.Hp);
            command.Parameters.AddWithValue("@mp", value.Mp);
            command.Parameters.AddWithValue("@owner", value.Owner);
            command.Parameters.AddWithValue("@updated_at", value.UpdatedAt);
            command.Parameters.AddWithValue("@created_at", value.CreatedAt);
            command.ExecuteNonQuery();
        }
    }
}

public class MateDb
{
    public uint Id { get; set; }
    public ulong ItemId { get; set; }
    public string Name { get; set; }
    public int Xp { get; set; }
    public ushort Level { get; set; }
    public int Mileage { get; set; }
    public int Hp { get; set; }
    public int Mp { get; set; }
    public uint Owner { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
