using System.Collections.Concurrent;
using System.Numerics;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.Crime;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class CrimeManager(IWorldManager worldManager,
    ICrimeIdManager crimeIdManager,
    IZoneManager zoneManager,
    IDoodadManager doodadManager) : Singleton<CrimeManager>, ICrimeManager
{
    public const int WantedCrimePointThreshold = 50;
    public const int PirateCrimePointThreshold = 3000;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private ConcurrentDictionary<ulong, CrimeEvent> CrimeEvents { get; } = [];
    // ReSharper disable once CollectionNeverUpdated.Local
    private List<ulong> DeletedEventIds { get; } = []; // Later needed for trial and evidence tempering skills
    private List<ulong> UpdatedEventIds { get; } = [];

    public List<CrimeEvent> GetCrimesOfPlayer(uint playerId)
    {
        return CrimeEvents.Values.Where(x => x.Criminal == playerId).ToList();
    }

    /// <summary>
    /// Загружает события преступлений из таблицы <c>crime</c> БД MySQL.
    /// </summary>
    /// <remarks>
    /// Используемые поля таблицы <c>crime</c>: id, criminal, victim, reporter,
    /// doodad_template, crime_type, zone_key, x, y, z, crime_time, report_time,
    /// arg1, arg2, arg3, msg.
    /// compact.sqlite3 / compact.server.table.sqlite3 не используются.
    /// Контейнер: <c>CrimeEvents</c> (id → CrimeEvent).
    /// </remarks>
    public void Load()
    {
        Logger.Info("Loading crime events...");
        CrimeEvents.Clear();
        DeletedEventIds.Clear();
        UpdatedEventIds.Clear();

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM crime";
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var crimeEvent = new CrimeEvent()
            {
                Id = reader.GetUInt32("id"),
                Criminal = reader.GetUInt32("criminal"),
                Victim = reader.GetUInt32("victim"),
                Reporter = reader.GetUInt32("reporter"),
                DoodadTemplate = reader.GetUInt32("doodad_template"),
                CrimeKind = (CrimeKind)reader.GetUInt32("crime_type"),
                ZoneKey = reader.GetUInt32("zone_key"),
                Position = new Vector3(reader.GetFloat("x"), reader.GetFloat("y"), reader.GetFloat("z")),
                CrimeTime = reader.GetDateTime("crime_time"),
                ReportTime = reader.GetDateTime("report_time"),
                Arg1 = reader.GetUInt32("arg1"),
                Arg2 = reader.GetUInt32("arg2"),
                Arg3 = reader.GetUInt32("arg3"),
                Msg = reader.GetString("msg")
            };
            if (!CrimeEvents.TryAdd(crimeEvent.Id, crimeEvent))
                Logger.Warn($"Was not able to load Crime Event {crimeEvent.Id}");
        }
    }

    public (int, int) Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        var deleteCount = 0;
        var updateCount = 0;
        // Remove deleted items from DB
        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;
            lock (DeletedEventIds)
            {
                if (DeletedEventIds.Count > 0)
                {
                    using (var deleteCommand = connection.CreateCommand())
                    {
                        var removedItemList = string.Join(",", DeletedEventIds);
                        deleteCommand.CommandText = $"DELETE FROM crime WHERE `id` IN({removedItemList})";
                        deleteCommand.Prepare();
                        deleteCount += deleteCommand.ExecuteNonQuery();
                    }

                    if (deleteCount != DeletedEventIds.Count)
                        Logger.Error($"Some crime events could not be deleted, only {deleteCount}/{DeletedEventIds.Count} events removed !");
                    DeletedEventIds.Clear();
                }
            }
        }

        // Update Container Info
        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;

            lock (UpdatedEventIds)
            {
                foreach (var c in UpdatedEventIds.ToArray())
                {
                    var crimeEvent = CrimeEvents.GetValueOrDefault(c);
                    if (crimeEvent == null)
                        continue;

                    command.CommandText = "REPLACE INTO crime (" +
                                          "`id`,`criminal`,`victim`,`reporter`,`doodad_template`,`crime_type`,`zone_key`,`x`,`y`,`z`,`crime_time`,`report_time`,`arg1`,`arg2`,`arg3`,`msg`" +
                                          ") VALUES ( " +
                                          "@id, @criminal, @victim, @reporter, @doodad_template, @crime_type, @zone_key, @x, @y, @z, @crime_time, @report_time, @arg1, @arg2, @arg3, @msg" +
                                          ")";

                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@id", crimeEvent.Id);
                    command.Parameters.AddWithValue("@criminal", crimeEvent.Criminal);
                    command.Parameters.AddWithValue("@victim", crimeEvent.Victim);
                    command.Parameters.AddWithValue("@reporter", crimeEvent.Reporter);
                    command.Parameters.AddWithValue("@doodad_template", crimeEvent.DoodadTemplate);
                    command.Parameters.AddWithValue("@crime_type", crimeEvent.CrimeKind);
                    command.Parameters.AddWithValue("@zone_key", crimeEvent.ZoneKey);
                    command.Parameters.AddWithValue("@x", crimeEvent.Position.X);
                    command.Parameters.AddWithValue("@y", crimeEvent.Position.Y);
                    command.Parameters.AddWithValue("@z", crimeEvent.Position.Z);
                    command.Parameters.AddWithValue("@crime_time", crimeEvent.CrimeTime);
                    command.Parameters.AddWithValue("@report_time", crimeEvent.ReportTime);
                    command.Parameters.AddWithValue("@arg1", crimeEvent.Arg1);
                    command.Parameters.AddWithValue("@arg2", crimeEvent.Arg2);
                    command.Parameters.AddWithValue("@arg3", crimeEvent.Arg3);
                    command.Parameters.AddWithValue("@msg", crimeEvent.Msg);
                    try
                    {
                        var res = command.ExecuteNonQuery();
                        updateCount += res;
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                }
            }
        }

        return (updateCount, deleteCount);
    }

    /// <summary>
    /// Reports a crime for a given evidence
    /// </summary>
    /// <param name="reporter"></param>
    /// <param name="evidence"></param>
    /// <param name="arg1"></param>
    /// <param name="arg2"></param>
    /// <param name="arg3"></param>
    /// <param name="message"></param>
    /// <returns></returns>
    public CrimeEvent ReportCrime(Character reporter, Doodad evidence, uint arg1, uint arg2, uint arg3, string message)
    {
        if (reporter == null || evidence == null)
            return null;

        if (evidence.OwnerType == DoodadOwnerType.Character && evidence.OwnerId == reporter.Id)
        {
            Logger.Warn($"{reporter.Name} ({reporter.Id}) tried to somehow report their own crimes at {evidence.Transform.World.Position}");
            SusManager.Instance.LogActivity(SusManager.CategoryCheating, reporter, $"Reported own crime at {evidence.Transform.World.Position} (type={evidence.TemplateId}");
            return null;
        }
        if (evidence.TryClaimCrimeReport(reporter, doodadManager) is not { } claim)
            return null;

        CrimeEvent newEvent;
        var stored = false;
        try
        {
            var zoneKey = zoneManager.GetZoneByKey(claim.ZoneId)?.Id ?? 0u;
            var newId = crimeIdManager.GetNextId();
            newEvent = new CrimeEvent
            {
                Id = newId,
                Criminal = claim.Criminal,
                Victim = claim.Victim,
                Reporter = reporter.Id,
                DoodadTemplate = claim.ItemTemplate,
                CrimeKind = claim.Kind,
                CrimeTime = claim.PlantedAt,
                ReportTime = DateTime.UtcNow,
                Position = claim.Position,
                ZoneKey = zoneKey,
                Arg1 = arg1,
                Arg2 = arg2,
                Arg3 = arg3,
                Msg = message
            };

            stored = CrimeEvents.TryAdd(newEvent.Id, newEvent);
            if (!stored)
            {
                crimeIdManager.ReleaseId(newId);
                Logger.Warn($"Unable to report crime at {newEvent.Position} with message {newEvent.Msg}");
                return null;
            }

            lock (UpdatedEventIds)
                UpdatedEventIds.Add(newEvent.Id);
        }
        finally
        {
            if (!stored)
                evidence.ReleaseCrimeReportClaim();
        }

        try
        {
            AddCrimePoints(newEvent.Criminal, claim.Kind, claim.Points);
        }
        finally
        {
            evidence.AdvanceReportedEvidence(reporter, claim);
        }

        return newEvent;
    }

    private void AddCrimePoints(uint criminalId, CrimeKind crimeType, short crimePoints)
    {
        // TODO: Handle this in the character manager instead so it can do offline people as well?
        Logger.Debug($"Adding {crimePoints} crime points to player {criminalId} for {crimeType} ({(int)crimeType})");
        var criminal = worldManager.GetCharacterById(criminalId);
        if (criminal is { IsOnline: true })
        {
            criminal.AddCrime(crimePoints);
        }
        else
        {
            CharacterManager.AddOfflineCrimePoints(criminalId, crimePoints);
        }
    }

    private Doodad GenerateEvidence(Character criminal, uint evidenceDoodadTemplateId, Vector3 targetLocation, Vector3 targetRotation, uint victimPlayerId, uint sourceDoodadTemplateId)
    {
        // TODO: floor/water surface the bloodstain Z height?
        return doodadManager.CreatePlayerDoodad(criminal, evidenceDoodadTemplateId,
            targetLocation.X, targetLocation.Y, targetLocation.Z, targetRotation.Z, 1f, 0, FarmType.Invalid, sourceDoodadTemplateId, (int)victimPlayerId, true);
    }

    public Doodad GenerateEvidenceFromDamage(BaseUnit criminal, Unit victim)
    {
        var criminalPlayer = criminal.GetOwnerCharacter();
        var victimPlayer = victim.GetOwnerCharacter();
        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        if (criminal is null)
            return null;

        /*
        // Check if there is already a small bloodstain nearby with the same data
        var nearbyDoodads = WorldManager.GetAround<Doodad>(victim).ToArray();
        foreach (var doodad in nearbyDoodads)
        {
            if (doodad.TemplateId != DoodadConstants.SmallBloodstain)
                continue;
            if (doodad.GetDistanceTo(victim, true) > 1f)
                continue;
            if (doodad.OwnerId == criminalPlayer.Id && doodad.Data == (victimPlayer?.Id ?? 0))
                return null;
        }
        */

        return GenerateEvidence(criminalPlayer,
            DoodadConstants.SmallBloodstain,
            victimPlayer?.Transform.World.Position ?? criminal.Transform.World.Position,
            victimPlayer?.Transform.World.Rotation ?? Vector3.Zero,
            victimPlayer?.Id ?? 0,
            0);
    }

    public Doodad GenerateEvidenceFromKill(BaseUnit criminal, Unit victim)
    {
        var criminalPlayer = criminal.GetOwnerCharacter();
        var victimPlayer = victim.GetOwnerCharacter();
        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        if (criminal is null)
            return null;

        return GenerateEvidence(criminalPlayer,
            DoodadConstants.LargeBloodstain,
            victimPlayer?.Transform.World.Position ?? criminal.Transform.World.Position,
            victimPlayer?.Transform.World.Rotation ?? Vector3.Zero,
            victimPlayer?.Id ?? 0,
            0);
    }

    public Doodad GenerateEvidenceFromTheft(Character criminal, Doodad stolenDoodad)
    {
        if (stolenDoodad.OwnerType != DoodadOwnerType.Character)
            return null;
        if (criminal is null)
            return null;

        return GenerateEvidence(criminal,
            criminal.Gender == Gender.Female ? DoodadConstants.FootprintFemale : DoodadConstants.FootprintMale,
            stolenDoodad.Transform.World.Position, stolenDoodad.Transform.World.Rotation,
            stolenDoodad.OwnerId, stolenDoodad.TemplateId);
    }
}
