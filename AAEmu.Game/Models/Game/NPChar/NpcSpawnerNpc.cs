using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Models.Game.NPChar;

public class NpcSpawnerNpc : Spawner<Npc>
{
    // ReSharper disable once InconsistentNaming
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// NpcSpawnerTemplateId
    /// </summary>
    public uint NpcSpawnerTemplateId { get; init; }
    /// <summary>
    /// NpcTemplateId
    /// </summary>
    public uint MemberId { get; set; }
    /// <summary>
    /// MemberType should be "Npc" here
    /// </summary>
    public string MemberType { get; set; }
    /// <summary>
    /// Spawn priority weight
    /// </summary>
    public float Weight { get; init; }

    public NpcSpawnerNpc()
    {
        //
    }

    /// <summary>
    /// Creates a new instance of NpcSpawnerNpcs with a Spawner template id (npc_spanwers)
    /// </summary>
    /// <param name="spawnerTemplateId"></param>
    public NpcSpawnerNpc(uint spawnerTemplateId)
    {
        NpcSpawnerTemplateId = spawnerTemplateId;
    }

    public NpcSpawnerNpc(uint spawnerTemplateId, uint npcTemplateId)
    {
        NpcSpawnerTemplateId = spawnerTemplateId;
        MemberId = npcTemplateId;
        MemberType = "Npc";
    }

    /// <summary>
    /// Spawns Npcs from a NpcSpawner
    /// </summary>
    /// <param name="npcSpawner"></param>
    /// <param name="ownerId"></param>
    /// <returns>List of newly spawned NPCs</returns>
    /// <exception cref="InvalidOperationException"></exception>
    public List<Npc> Spawn(NpcSpawner npcSpawner, uint ownerId = 0)
    {
        switch (MemberType)
        {
            case "Npc":
                return SpawnNpc(npcSpawner, ownerId);
            case "NpcGroup":
                return SpawnNpcGroup(npcSpawner, ownerId);
            default:
                throw new InvalidOperationException($"Tried spawning an unsupported line from NpcSpawnerNpc - Id: {Id}");
        }
    }

    /// <summary>
    /// Internal Spawn Npc function for Spawn
    /// </summary>
    /// <param name="npcSpawner"></param>
    /// <param name="ownerId"></param>
    /// <returns></returns>
    private List<Npc> SpawnNpc(NpcSpawner npcSpawner, uint ownerId = 0)
    {
        var npcs = new List<Npc>();
        var npc = NpcManager.Instance.Create(npcSpawner.ParentWorld, 0, MemberId);
        if (npc == null)
        {
            Logger.Warn($"Npc {MemberId}, from spawner Id {npcSpawner.Id} not exist at db. Spawner Position: {npcSpawner.Position}");
            return null;
        }

        npc.ParentWorld = npcSpawner.ParentWorld;
        npc.OwnerId = ownerId;

        npc.RegisterNpcEvents();

        Logger.Trace($"Spawn npc templateId {MemberId} objId {npc.ObjId} from spawnerId {NpcSpawnerTemplateId} at Position: {npcSpawner.Position}");

        var settleOnGround = false;
        var deferGrounding = false;
        uint spawnZone = 0;
        if (!npc.CanFly)
        {
            // The layered ground rules (plan S3): the NPC settles onto the surface under its spawn point — a bridge
            // deck, a floor indoors — but never teleports across a cliff: a ground more than a few metres away is
            // ignored and the spawner keeps its own height. The result goes to the instance position (below), never to
            // the shared spawner: one settled NPC must not drag its whole family onto its own floor.
            var worldManager = WorldManager.Instance;
            if (worldManager.StaticGeometry != null && worldManager.NpcGroundResolver != null)
            {
                settleOnGround = true;
            }
            else if (npcSpawner.ParentWorld is { } parentWorld)
            {
                var pos = npcSpawner.Position;
                var refined = NpcSpawnHeightRules.Refine(parentWorld, pos.ZoneId, pos.X, pos.Y, pos.Z);
                if (Physics.Static.NpcGroundResolver.IsUsableHeight(refined))
                    npcSpawner.Position.Z = refined;
            }
        }

        npc.Transform.ApplyWorldSpawnPosition(npcSpawner.Position);
        if (npc.Transform == null)
        {
            Logger.Error($"Can't spawn npc {MemberId} from spawnerId {NpcSpawnerTemplateId}. Transform is null.");
            return null;
        }

        if (settleOnGround)
        {
            var worldManager = WorldManager.Instance;
            var resolver = worldManager.NpcGroundResolver;
            var template = npc.ParentWorld.Template;
            var local = npc.Transform.Local;
            var position = local.Position;
            var zoneKey = worldManager.GetZoneId(template, position.X, position.Y);
            spawnZone = zoneKey;
            worldManager.StaticGeometry.GetZone(template, zoneKey);
            // A zone whose geometry is still being read in the background answers with an empty index; the NPC keeps the
            // spawner's height then, exactly as the soft-degradation rule of the layer asks (plan P4).
            if (worldManager.StaticGeometry != null && worldManager.StaticGeometry.IsZoneReady(template, zoneKey))
            {
                var settledZ = resolver.SettledZ(template, zoneKey, position.X, position.Y, position.Z,
                    NpcWorldFooting.TryGetStreamSpawnHeight(npc, position),
                    WorldManager.NavHeightForSpawn(template, position.X, position.Y, position.Z));
                // Ноль — не высота: юнит на Z = 0 был бы под землёй. Тогда остаётся высота спавнера.
                if (Physics.Static.NpcGroundResolver.IsUsableHeight(settledZ) && MathF.Abs(settledZ - position.Z) > 0.01f)
                    local.SetPosition(position.X, position.Y, settledZ);
                else if (!Physics.Static.NpcGroundResolver.IsUsableHeight(settledZ))
                    Logger.Warn("NPC {0} (template {1}) at {2:F1},{3:F1}: no usable ground (spawn {4:F2}, ground {5:F2})",
                        npc.ObjId, MemberId, position.X, position.Y, position.Z, settledZ);
            }
            else deferGrounding = true;
        }

        npc.Transform.InstanceId = npc.Transform.InstanceId;

        npc.Spawner = npcSpawner;

        if (npc.Ai != null)
        {
            npc.Ai.HomePosition = npc.Transform.World.Position;
            npc.Ai.IdlePosition = npc.Ai.HomePosition;
            if (!string.IsNullOrWhiteSpace(npcSpawner.FollowPath)
                && !npc.Ai.TryLoadFollowPath(npcSpawner))
            {
                var hint = npcSpawner.FollowPath.StartsWith("AIPath_", StringComparison.OrdinalIgnoreCase)
                    ? " (expected in level_design/zone/*/editor/npc.xml + client .g spawn)"
                    : " (set AAEMU_LEGACY_DISK_PATH=1 only for old Data/Path names)";
                Logger.Warn("Failed to load follow path {0} for NPC {1} ({2}){3}",
                    npcSpawner.FollowPath, npc.TemplateId, npc.ObjId, hint);
            }
            npc.Ai.GoToSpawn();
        }
        npc.Spawner.RespawnTime = (int)Random.Shared.Next(npc.Spawner.Template.SpawnDelayMin, npc.Spawner.Template.SpawnDelayMax);
        npc.Spawn();
        if (!deferGrounding)
            NpcFootingRules.TryBindUnseenFloor(npc);
        if (deferGrounding) WorldManager.Instance.NpcSpawnGrounding?.Track(npc, spawnZone);

        var world = WorldManager.Instance.GetWorld(npc.Transform.InstanceId);
        world.Events.OnUnitSpawn(world, new OnUnitSpawnArgs { Npc = npc });
        npc.Simulation = new Simulation(npc);
        if (npc.Ai != null && PathFollowRules.ShouldMirrorPathInSimulation(npcSpawner.FollowPath))
            npc.Simulation.SyncFromAiPath(npc.Ai, npcSpawner.FollowPath);

        npcs.Add(npc);
        return npcs;
    }

    /// <summary>
    /// Internal Spawn NpcGroup function for Spawn
    /// </summary>
    /// <param name="npcSpawner"></param>
    /// <param name="ownerId"></param>
    /// <returns></returns>
    private List<Npc> SpawnNpcGroup(NpcSpawner npcSpawner, uint ownerId = 0)
    {
        return SpawnNpc(npcSpawner, ownerId);
    }
}
