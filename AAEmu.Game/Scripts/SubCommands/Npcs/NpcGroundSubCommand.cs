using System.Drawing;
using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Bots.Navigation;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.AI;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Physics.Static;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Npcs;

/// <summary>Диагностика слоя высоты под НПС (порт P4) для отладки движения.</summary>
public class NpcGroundSubCommand : SubCommandBase
{
    public NpcGroundSubCommand()
    {
        Title = "[Npc Ground]";
        Description = "Terrain vs layered ground under an NPC or at x,y";
        CallPrefix = $"{CommandManager.CommandPrefix}npc ground";
        AddParameter(new StringSubCommandParameter("target", "target", true, "target", "id"));
        AddParameter(new NumericSubCommandParameter<uint>("ObjId", "object id", false));
        AddParameter(new NumericSubCommandParameter<float>("x", "x", false, "x"));
        AddParameter(new NumericSubCommandParameter<float>("y", "y", false, "y"));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        var ch = (Character)character;
        Npc npc = null;
        if (parameters.TryGetValue("ObjId", out var npcObjId))
            npc = ch.ParentWorld.GetNpc(npcObjId);
        else if (ch.CurrentTarget is Npc targeted)
            npc = targeted;

        var x = parameters.ContainsKey("x") ? GetOptionalParameterValue(parameters, "x", 0f) : npc?.Transform.Local.Position.X ?? ch.Transform.Local.Position.X;
        var y = parameters.ContainsKey("y") ? GetOptionalParameterValue(parameters, "y", 0f) : npc?.Transform.Local.Position.Y ?? ch.Transform.Local.Position.Y;
        var referenceZ = npc?.Transform.Local.Position.Z ?? ch.Transform.Local.Position.Z;
        var zoneId = npc?.Transform.ZoneId ?? ch.Transform.ZoneId;

        if (npc != null)
        {
            var aiState = "no Ai";
            var pathState = "path=n/a";
            if (npc.Ai is { } ai)
            {
                aiState = $"Ai={ai.GetCurrentBehavior()?.GetType().Name ?? "none"}";
                var follow = npc.Spawner?.FollowPath;
                if (!string.IsNullOrWhiteSpace(follow))
                {
                    var pt = npc.Spawner?.PathPointNo ?? 0;
                    var designer = follow.StartsWith("AIPath_", StringComparison.OrdinalIgnoreCase) ? "npc.xml" : "legacy/.path";
                    var anchor = npc.Spawner is { HasDesignerPlacementFrame: true } sp
                        ? $" anchor=({sp.PlacementAnchorLocal.X:F1},{sp.PlacementAnchorLocal.Y:F1})"
                        : string.Empty;
                    var placement = npc.Spawner is { HasDesignerPlacementFrame: true } ? " client.g" : string.Empty;
                    var legMps = PathFollowRules.LegMetresPerSecond(ai, ai.PathHandler.AiPathSpeed);
                    pathState =
                        $"followPath={follow} src={designer}{placement} pointNo={pt}{anchor} pathMul={ai.PathHandler.AiPathSpeed:F2} legMps={legMps:F2} loop={ai.PathHandler.AiPathLooping} points={ai.PathHandler.AiPathPoints.Count} queue={ai.PathHandler.AiPathPointsRemaining.Count} ready={ai.PathHandler.HasPathMovementData()}";
                }
            }

            var spawnData = npc.Spawner is { HasDesignerPlacementFrame: true }
                ? "spawn=client.g"
                : npc.Spawner != null
                    ? "spawn=npc_spawns.json"
                    : "spawn=unknown";
            var followName = npc.Spawner?.FollowPath;
            var designerPath = !string.IsNullOrWhiteSpace(followName)
                && followName.StartsWith("AIPath_", StringComparison.OrdinalIgnoreCase);
            var pipeline = npc.Spawner switch
            {
                { HasDesignerPlacementFrame: true } when designerPath => "pipeline=client.g→npc.xml→AiPathHandler",
                { HasDesignerPlacementFrame: true } => "pipeline=client.g",
                not null when designerPath => "pipeline=npc_spawns.json→npc.xml→AiPathHandler",
                not null => "pipeline=npc_spawns.json",
                _ => "pipeline=unknown"
            };
            var runtimeSpawn = npc.Spawner switch
            {
                { ZonePopulationManaged: true } => "runtime=ZoneNpcSpawnOrchestrator",
                not null when ZoneNpcSpawnRules.AuthoritativeFromClientG
                    && npc.Spawner.HasDesignerPlacementFrame => "runtime=legacy-radius",
                not null => "runtime=legacy-radius",
                _ => "runtime=unknown"
            };
            SendMessage(messageOutput,
                $"NPC objId={npc.ObjId} template={npc.TemplateId} zone={zoneId} spawnerId={npc.Spawner?.SpawnerId} {spawnData} {pipeline} {runtimeSpawn} {aiState} {pathState} Z={referenceZ:F3} worldStream={npc.WorldStreamPosition?.Z.ToString("F3") ?? "none"} unseenFloor={npc.UnseenFloorZ?.ToString("F3") ?? "none"} walkerClip={npc.AppliesWalkerCollision()} blocked={npc.LastWalkerStepBlocked} step={npc.LastWalkerStepMeters:F3}m stuck={npc.IsWalkerStuck()} chase={npc.Ai?.CombatApproachMoving == true} patrol={npc.IsInPatrol} returnRouted={npc.ReturningOnPlannedRoute}");
        }

        var world = WorldManager.Instance;
        world.TryGetHeight(zoneId, x, y, referenceZ, out var terrain);
        SendMessage(messageOutput, $"({x:F1},{y:F1}) zone={zoneId}: terrain={terrain:F3} usable={NpcGroundResolver.IsUsableHeight(terrain)}");

        var template = world.GetWorldTemplateByZoneKey(zoneId);
        if (template == null || world.NpcGroundResolver is not { } resolver)
        {
            SendColorMessage(messageOutput, Color.Orange, "Static geometry layer unavailable.");
            return;
        }

        Func<float?>? navHeight = null;
        if (npc?.ParentWorld?.Template?.GeoData is { } geo)
        {
            navHeight = () =>
            {
                try
                {
                    if (geo.TryGroundNear(x, y, referenceZ, out var near))
                        return near;
                    var h = geo.GetHeight(new Vector3(x, y, referenceZ));
                    return h > 0f ? h : null;
                }
                catch
                {
                    return null;
                }
            };
        }

        var navNearText = "n/a";
        var triGroundText = "n/a";
        var walkableText = "n/a";
        var structureText = "n/a";
        if ((npc?.ParentWorld ?? ch.ParentWorld)?.Template?.GeoData is { } geoProbe)
        {
            if (geoProbe.TryGroundNear(x, y, referenceZ, out var navNear))
                navNearText = $"{navNear:F3}";
            if (geoProbe.TryStructureFloor(x, y, referenceZ, StructureFloorRules.FlatMetres, StructureFloorRules.RiseMetres, out var structure))
                structureText = $"{structure:F3}";
        }

        if (NavMeshGround.IsEnabled && template is { })
        {
            var nav = new NavMeshGround(template);
            if (nav.TryGround(x, y, out var tri))
                triGroundText = $"{tri:F3}";
            walkableText = nav.IsWalkable(x, y) ? "yes" : "no";
        }

        var layered = resolver.Ground(template, zoneId, x, y, referenceZ, navHeight);
        var zoneReady = world.StaticGeometry?.IsZoneReady(template, zoneId) ?? false;
        var referenceHeight = npc?.Ai != null
            ? world.GetReferenceHeight(npc.Ai, x, y, referenceZ, zoneId)
            : layered;
        SendMessage(messageOutput,
            $"navNear={navNearText}, triGround={triGroundText}, meshWalkable={walkableText}, structureFloor={structureText}, layered={layered:F3}, referenceHeight={referenceHeight:F3}, staticZoneReady={zoneReady}");
    }
}