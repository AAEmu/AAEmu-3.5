using System.Numerics;

using AAEmu.Commons.IO;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.IO;
using AAEmu.Game.Models.Game.AI;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Physics.Static;

using NLog;

namespace AAEmu.Game.Services;

/// <summary>
/// Primary NPC placements from client <c>npc_spawners.g</c> (ArcheageZones).
/// Paths resolve via <c>editor/npc.xml</c> at AI load; movement via <c>AiPathHandler</c>.
/// <c>npc_spawns.json</c> loads afterward in <see cref="SpawnManager"/> as fallback.
/// </summary>
public static class ClientNpcSpawnerLoader
{
    private const float ZoneCellMetres = 1024f;

    private static readonly string[] SpawnerSubfolders = ["zone_server", "editor", "world_server"];

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>Load client <c>level_design</c> spawners unless env/config disables.</summary>
    public static bool Enabled
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("AAEMU_CLIENT_NPC_SPAWN");
            if (env is "0" or "false" or "False")
                return false;
            return AppConfiguration.Instance.World.ClientNpcSpawnEnabled;
        }
    }

    /// <summary>Zone keys to load; empty = all zones in <c>world.xml</c>. Env overrides <see cref="WorldConfig.ClientNpcSpawnZoneKeys"/>.</summary>
    public static HashSet<uint> ZoneFilter
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("AAEMU_CLIENT_SPAWN_ZONES");
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var set = new HashSet<uint>();
                foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (uint.TryParse(part, out var key))
                        set.Add(key);
                }

                return set;
            }

            var fromConfig = AppConfiguration.Instance.World.ClientNpcSpawnZoneKeys;
            return fromConfig is { Count: > 0 } ? [.. fromConfig] : [];
        }
    }

    /// <summary>Zone keys that have a readable <c>npc_spawners.g</c> within <paramref name="filter"/>.</summary>
    public static HashSet<uint> DiscoverZonesWithClientSpawners(WorldInstance world)
    {
        var discovered = new HashSet<uint>();
        if (!Enabled || world.Template.XmlWorldZones is not { Count: > 0 } zones)
            return discovered;

        var filter = ZoneFilter;
        foreach (var (zoneKey, _) in zones)
        {
            if (filter.Count > 0 && !filter.Contains(zoneKey))
                continue;
            if (TryReadZoneFile(world.Template.Name, zoneKey, out _, out _))
                discovered.Add(zoneKey);
        }

        return discovered;
    }

    public static int LoadNpcSpawners(SpawnManager spawnManager, WorldInstance world)
    {
        if (!Enabled || world.Template.XmlWorldZones is not { Count: > 0 } zones)
            return 0;

        var filter = ZoneFilter;
        var loaded = 0;
        var parsedZones = 0;
        foreach (var (zoneKey, zone) in zones)
        {
            if (filter.Count > 0 && !filter.Contains(zoneKey))
                continue;

            if (!TryReadZoneFile(world.Template.Name, zoneKey, out var text, out var source))
                continue;

            parsedZones++;
            var placements = NpcSpawnersFileRules.Parse(text, out var skipped);
            if (skipped > 0)
                Logger.Debug("npc_spawners.g zone {0} ({2}): skipped {1} malformed blocks", zoneKey, skipped, source);

            var originX = zone.OriginX * ZoneCellMetres;
            var originY = zone.OriginY * ZoneCellMetres;
            var zoneRegistered = 0;
            var skippedNoTemplate = 0;
            var skippedNoNpc = 0;

            if (placements.Any(p => p.Paths.Count > 0))
                AiPathsManager.Instance.EnsureDesignerZoneLoaded(world.Template.Name, zoneKey);

            foreach (var placement in placements)
            {
                var template = NpcGameData.Instance.GetNpcSpawnerTemplate(placement.SpawnerType);
                if (template == null)
                {
                    skippedNoTemplate++;
                    Logger.Trace("Client spawn zone {0}: no sqlite template for spawnerType {1} (sid {2})",
                        zoneKey, placement.SpawnerType, placement.SpawnerId);
                    continue;
                }

                var memberId = template.Npcs.FirstOrDefault()?.MemberId ?? 0;
                if (!NpcManager.Instance.Exist(memberId))
                {
                    skippedNoNpc++;
                    continue;
                }

                if (spawnManager.HasClientSpawnerId(placement.SpawnerId))
                    continue;

                var (local, yaw) = PickPlacementPoint(placement);
                var worldXProbe = local.X + originX;
                var worldYProbe = local.Y + originY;
                var worldX = local.X + originX;
                var worldY = local.Y + originY;
                // Zones SpawnerSystem: authored Z + ClearanceMetres (0.4) before terrain/brush settle.
                var spawnZ = NpcSpawnHeightRules.Refine(
                    world, zoneKey, worldX, worldY, local.Z + NpcGroundResolver.ClearanceMetres);
                var anchorLocal = placement.Points.Count > 0
                    ? PathWalkerRules.AnchorPlacementPoint(placement.Points, local)?.Position ?? placement.Anchor.Position
                    : local;
                var position = new WorldSpawnPosition
                {
                    X = worldX,
                    Y = worldY,
                    Z = spawnZ,
                    Yaw = yaw,
                    WorldId = world.Id,
                    ZoneId = zoneKey
                };

                var spawner = new NpcSpawner
                {
                    ParentWorld = world,
                    Id = placement.SpawnerId,
                    SpawnerId = placement.SpawnerId,
                    UnitId = memberId,
                    Template = template,
                    Count = 1,
                    Position = position,
                    FollowPath = placement.Paths.Count > 0 ? placement.Paths[0].PathName : string.Empty,
                    PathPointNo = placement.Paths.Count > 0 ? placement.Paths[0].PointNo : 0,
                    PlacementSpawnLocal = new Vector3(local.X, local.Y, local.Z),
                    PlacementAnchorLocal = new Vector3(anchorLocal.X, anchorLocal.Y, anchorLocal.Z)
                };
                spawner.InitializeSpawnableNpcs(template);
                spawner.NpcSpawnerIds.Add(placement.SpawnerType);

                if (ZoneNpcSpawnRules.AuthoritativeFromClientG)
                {
                    world.ZoneNpcSpawn ??= new ZoneNpcSpawnOrchestrator(world);
                    world.ZoneNpcSpawn.RegisterPlacement(spawner);
                }
                else
                    spawnManager.AddNpcSpawner(spawner);

                loaded++;
                zoneRegistered++;
            }

            if (zoneKey is 129 or 179)
                Logger.Info(
                    "Solzreed (zone {0}): parsed {1} placements from {2}, registered {3} new spawners; skipped no sqlite template={4}, unknown npc id={5}",
                    zoneKey, placements.Count, source, zoneRegistered, skippedNoTemplate, skippedNoNpc);
        }

        if (loaded > 0)
            Logger.Info("Client npc_spawners.g: {0} spawners in {1} zones for world {2}", loaded, parsedZones, world.Template.Name);
        return loaded;
    }

    /// <summary>Finds <c>npc_spawners.g</c> for a zone (10.0 <c>zone_server</c>, 3.5 <c>editor</c>).</summary>
    public static bool TryReadZoneFile(string worldName, uint zoneKey, out string text, out string source)
    {
        text = string.Empty;
        source = string.Empty;
        foreach (var sub in SpawnerSubfolders)
        {
            var clientRelative = Path.Combine("game", "worlds", worldName, "level_design", "zone",
                zoneKey.ToString(), sub, NpcSpawnersFileRules.FileName);
            if (ClientFileManager.Sources.Count > 0 && ClientFileManager.FileExists(clientRelative))
            {
                text = ClientFileManager.GetFileAsString(clientRelative);
                source = clientRelative;
                return !string.IsNullOrWhiteSpace(text);
            }

            var disk = Path.Combine(FileManager.AppPath, "Data", "Worlds", worldName, "level_design", "zone",
                zoneKey.ToString(), sub, NpcSpawnersFileRules.FileName);
            if (!File.Exists(disk))
                continue;
            text = File.ReadAllText(disk);
            source = disk;
            return !string.IsNullOrWhiteSpace(text);
        }

        return false;
    }

    private static (Vector3 Local, float Yaw) PickPlacementPoint(NpcSpawnerPlacement placement)
    {
        if (placement.AreaType == NpcSpawnAreaType.Area && placement.Triangles.Count > 0)
        {
            var triangle = PickTriangle(placement.Triangles);
            var yaw = placement.Points.Count > 0 ? placement.Points[0].ZRot : 0f;
            return (triangle.RandomPoint(), yaw);
        }

        var point = placement.AreaType == NpcSpawnAreaType.Point && placement.Points.Count > 1
            ? placement.Points[Random.Shared.Next(placement.Points.Count)]
            : placement.Anchor;
        return (point.Position, point.ZRot);
    }

    private static SpawnAreaTriangle PickTriangle(IReadOnlyList<SpawnAreaTriangle> triangles)
    {
        var total = triangles.Sum(t => t.Rate > 0f ? t.Rate : 1f);
        var roll = (float)Random.Shared.NextDouble() * total;
        foreach (var tri in triangles)
        {
            roll -= tri.Rate > 0f ? tri.Rate : 1f;
            if (roll <= 0f)
                return tri;
        }

        return triangles[0];
    }
}