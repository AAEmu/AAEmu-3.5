#nullable enable
using System.Collections.Concurrent;
using System.Numerics;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Physics.Static;

/// <summary>Finishes spawn grounding on a server tick after the zone's asynchronous geometry build.</summary>
public sealed class NpcSpawnGrounding(StaticGeometryManager geometry, NpcGroundResolver resolver)
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private const int WorkPerTick = 512;
    private const float PositionToleranceSquared = 0.0001f;
    private readonly ConcurrentQueue<Entry> _pending = new();
    private int _processing;

    private sealed record Entry(WeakReference<Npc> Npc, WeakReference<WorldInstance> World, Vector3 Spawn, uint Zone);

    public void Track(Npc npc, uint zone)
    {
        var world = npc.ParentWorld;
        if (world == null || zone == 0) return;
        var position = npc.Transform.World.Position;
        geometry.GetZone(world.Template, zone);
        _pending.Enqueue(new Entry(new WeakReference<Npc>(npc), new WeakReference<WorldInstance>(world), position, zone));
    }

    public void Tick()
    {
        if (Interlocked.Exchange(ref _processing, 1) != 0) return;
        try
        {
            var count = Math.Min(WorkPerTick, _pending.Count);
            for (var i = 0; i < count && _pending.TryDequeue(out var entry); i++)
            {
                try { Process(entry); }
                catch (Exception ex) { Logger.Warn(ex, "Deferred NPC grounding failed"); }
            }
        }
        finally { Volatile.Write(ref _processing, 0); }
    }

    private void Process(Entry entry)
    {
        if (!entry.Npc.TryGetTarget(out var npc) || !entry.World.TryGetTarget(out var world) ||
            !ReferenceEquals(npc.ParentWorld, world) || !npc.IsVisible || npc.IsDead || npc.CanFly || npc.IsInBattle ||
            npc.Transform.Parent != null || npc.Transform.StickyParent != null ||
            Vector3.DistanceSquared(npc.Transform.World.Position, entry.Spawn) > PositionToleranceSquared)
            return;
        var template = world.Template;
        if (!geometry.IsZoneReady(template, entry.Zone))
        {
            geometry.GetZone(template, entry.Zone);
            _pending.Enqueue(entry);
            return;
        }
        var z = resolver.SettledZ(template, entry.Zone, entry.Spawn.X, entry.Spawn.Y, entry.Spawn.Z,
            NpcWorldFooting.TryGetStreamSpawnHeight(npc, entry.Spawn),
            WorldManager.NavHeightForSpawn(template, entry.Spawn.X, entry.Spawn.Y, entry.Spawn.Z));
        if (!NpcGroundResolver.IsUsableHeight(z) || MathF.Abs(z - entry.Spawn.Z) <= 0.01f ||
            Vector3.DistanceSquared(npc.Transform.World.Position, entry.Spawn) > PositionToleranceSquared)
            return;
        npc.Transform.Local.SetPosition(entry.Spawn.X, entry.Spawn.Y, z);
        if (npc.Ai is { } ai)
        {
            if (Vector3.DistanceSquared(ai.HomePosition, entry.Spawn) <= PositionToleranceSquared) ai.HomePosition = new Vector3(entry.Spawn.X, entry.Spawn.Y, z);
            if (Vector3.DistanceSquared(ai.IdlePosition, entry.Spawn) <= PositionToleranceSquared) ai.IdlePosition = new Vector3(entry.Spawn.X, entry.Spawn.Y, z);
        }
        npc.Events.OnMovement(npc, new OnMovementArgs());
        npc.StopMovement();
        NpcFootingRules.TryBindUnseenFloor(npc);
    }
}
