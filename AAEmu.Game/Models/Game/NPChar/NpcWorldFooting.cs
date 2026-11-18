using System.Numerics;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI;
using AAEmu.Game.Physics.Static;

namespace AAEmu.Game.Models.Game.NPChar;

/// <summary>
/// Точка входа для будущего <c>WZNpcState</c> / World relay: высота World до Zone AI.
/// Вызывать при создании NPC миром (summon, plot, скрипт), когда появится WorldIntegration в AAC3500.
/// </summary>
public static class NpcWorldFooting
{
    /// <summary>Z из World relay для <see cref="NpcGroundResolver.SettledZ"/>, если анонс рядом с точкой спавна.</summary>
    public static float? TryGetStreamSpawnHeight(Npc npc, Vector3 spawnPosition)
    {
        if (!WorldSpawnHeightRules.Enabled || npc.WorldStreamPosition is not { } announced)
            return null;
        if (MathF.Abs(announced.X - spawnPosition.X) > WorldSpawnHeightRules.MaxMetres
            || MathF.Abs(announced.Y - spawnPosition.Y) > WorldSpawnHeightRules.MaxMetres
            || MathF.Abs(announced.Z - spawnPosition.Z) > WorldSpawnHeightRules.MaxMetres)
            return null;
        return announced.Z;
    }

    /// <summary>Записывает анонс World и при необходимости подтягивает Z по P4.</summary>
    public static void ApplyWorldStreamPosition(Npc npc, Vector3 announced, bool settleNow = true)
    {
        if (!WorldSpawnHeightRules.Enabled || npc.CanFly)
        {
            npc.WorldStreamPosition = announced;
            return;
        }

        npc.WorldStreamPosition = announced;
        if (!settleNow || npc.ParentWorld?.Template is not { } template)
            return;

        var wm = WorldManager.Instance;
        if (wm.NpcGroundResolver is not { } resolver || wm.StaticGeometry is not { } geometry)
            return;

        var pos = npc.Transform.Local.Position;
        if (MathF.Abs(announced.X - pos.X) > WorldSpawnHeightRules.MaxMetres
            || MathF.Abs(announced.Y - pos.Y) > WorldSpawnHeightRules.MaxMetres)
            return;

        var zoneKey = wm.GetZoneId(template, pos.X, pos.Y);
        if (!geometry.IsZoneReady(template, zoneKey))
            return;

        var settledZ = resolver.SettledZ(template, zoneKey, pos.X, pos.Y, pos.Z, TryGetStreamSpawnHeight(npc, pos),
            WorldManager.NavHeightForSpawn(template, pos.X, pos.Y, pos.Z));
        if (!NpcGroundResolver.IsUsableHeight(settledZ) || MathF.Abs(settledZ - pos.Z) <= 0.01f)
        {
            NpcFootingRules.TryBindUnseenFloor(npc);
            return;
        }

        npc.Transform.Local.SetPosition(pos.X, pos.Y, settledZ);
        if (npc.Ai is { } ai)
        {
            var grounded = new Vector3(pos.X, pos.Y, settledZ);
            if (Vector3.DistanceSquared(ai.HomePosition, pos) <= 1f)
                ai.HomePosition = grounded;
            if (Vector3.DistanceSquared(ai.IdlePosition, pos) <= 1f)
                ai.IdlePosition = grounded;
        }

        NpcFootingRules.TryBindUnseenFloor(npc);
    }
}