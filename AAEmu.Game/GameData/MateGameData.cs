using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Mate;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

[GameData]
public class MateGameData : Singleton<MateGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private Dictionary<uint, List<uint>> _npcMountSkills = [];
    private Dictionary<uint, MountSkills> _mountSkills = [];
    private Dictionary<uint, MountAttachedSkills> _mountAttachedSkills = [];
    private Dictionary<int, MateType> _mateTypesByEquipSlotPack = [];

    public MateType GetMateType(int equipSlotPackId) => _mateTypesByEquipSlotPack.GetValueOrDefault(equipSlotPackId);

    /// <summary>
    /// Gets a list of pet skill Ids
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public IReadOnlyList<uint> GetMateSkills(uint id) =>
        _npcMountSkills.TryGetValue(id, out var skills) ? skills : [];

    /// <summary>
    /// Get the associated rider skill for a given mountSkill
    /// </summary>
    /// <param name="mateSkill">The skill the mate used</param>
    /// <param name="attachPoint">The attachPoint the player is currently on</param>
    /// <returns></returns>
    public uint GetMountAttachedSkills(uint mateSkill, AttachPointKind attachPoint)
    {
        return TryGetMountSkillIdBySkillId(mateSkill, out var mountSkillId)
            ? GetAttachedSkillByMountSkillId(mountSkillId, attachPoint)
            : 0;
    }

    public bool TryGetMountSkillIdBySkillId(uint skillId, out uint mountSkillId)
    {
        mountSkillId = 0;

        foreach (var ms in _mountSkills)
        {
            if (ms.Value.SkillId != skillId)
                continue;

            mountSkillId = ms.Key;
            return true;
        }

        return false;
    }

    public uint GetAttachedSkillByMountSkillId(uint mountSkillId, AttachPointKind attachPoint)
    {
        var skill = FindAttachedSkill(mountSkillId, attachPoint);
        if (skill != 0)
            return skill;

        if (attachPoint != AttachPointKind.Driver)
        {
            skill = FindAttachedSkill(mountSkillId, AttachPointKind.Driver);
            if (skill != 0)
                return skill;
        }

        return attachPoint != AttachPointKind.None
            ? FindAttachedSkill(mountSkillId, AttachPointKind.None)
            : 0;
    }

    private uint FindAttachedSkill(uint mountSkillId, AttachPointKind attachPoint)
    {
        foreach (var mas in _mountAttachedSkills)
        {
            if (mas.Value.MountSkillId == mountSkillId && mas.Value.AttachPointId == attachPoint)
                return mas.Value.SkillId;
        }

        return 0;
    }

    /// <summary>
    /// Loads the game db data for pets
    /// </summary>
    /// <param name="connection"></param>
    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        _npcMountSkills = [];
        _mountSkills = [];
        _mountAttachedSkills = [];
        _mateTypesByEquipSlotPack = [];

        #region MateTables

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, mate_type_id FROM mate_equip_slot_packs";
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
                _mateTypesByEquipSlotPack.Add(reader.GetInt32("id"), (MateType)reader.GetByte("mate_type_id"));
        }

        // Npc Mount skills
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM npc_mount_skills";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new NpcMountSkills();
                    //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                    template.NpcId = reader.GetUInt32("npc_id");
                    template.MountSkillId = reader.GetUInt32("mount_skill_id");

                    if (_npcMountSkills.TryGetValue(template.NpcId, out var value))
                    {
                        if (!value.Contains(template.MountSkillId))
                            value.Add(template.MountSkillId);
                        else
                            Logger.Trace($"Duplicate entry for npc_mount_skills");
                    }
                    else
                        _npcMountSkills.Add(template.NpcId, [template.MountSkillId]);
                }
            }
        }

        // Mount Skills
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM mount_skills";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new MountSkills
                    {
                        Id = reader.GetUInt32("id"),
                        //Name = reader.GetString("name", ""), // there is no such field in the database for version 3.0.3.0
                        SkillId = reader.GetUInt32("skill_id")
                    };
                    _mountSkills.Add(template.Id, template);
                }
            }
        }

        // Mount attached skills
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM mount_attached_skills";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new MountAttachedSkills
                    {
                        Id = reader.GetUInt32("id"),
                        MountSkillId = reader.GetUInt32("mount_skill_id"),
                        AttachPointId = (AttachPointKind)reader.GetUInt32("attach_point_id"),
                        SkillId = reader.GetUInt32("skill_id")
                    };
                    _mountAttachedSkills.Add(template.Id, template);
                }
            }
        }

        #endregion MateTables
    }

    public void PostLoad()
    {
        // Nothing to do here
    }
}
