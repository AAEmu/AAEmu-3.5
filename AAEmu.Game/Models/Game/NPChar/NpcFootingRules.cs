using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI;
using AAEmu.Game.Physics.Static;

namespace AAEmu.Game.Models.Game.NPChar;

/// <summary>
/// «Невидимый пол» (порт <c>NpcFooting</c> / <c>MovementRules.UnseenFloor</c> из ArcheageZones): юнит,
/// заспавненный заметно выше рельефа без пола кисти под точкой, сохраняет эту высоту при ходьбе.
/// </summary>
public static class NpcFootingRules
{
    public const float UnseenFloorMinMetres = 15f;

    public static bool Enabled { get; } = Environment.GetEnvironmentVariable("AAEMU_UNSEEN_FLOOR") != "0";

    public static bool IsUnseenFloor(float level, float? terrain, bool inWater, bool floorNear) =>
        Enabled && terrain is { } ground && level - ground > UnseenFloorMinMetres && !inWater && !floorNear;

    public static float? KeepUnseenFloor(float? unseenFloorZ, float reference, float ground)
    {
        if (unseenFloorZ is not { } level)
            return null;
        if (MathF.Abs(reference - level) > NpcGroundResolver.NearBandMetres)
            return null;
        return level - ground > NpcGroundResolver.NearBandMetres ? level : null;
    }

    /// <summary>Определяет и привязывает <see cref="Npc.UnseenFloorZ"/> после спавна.</summary>
    public static void TryBindUnseenFloor(Npc npc)
    {
        if (!Enabled || npc.CanFly || npc.Ai == null || npc.ParentWorld?.Template is not { } template)
            return;
        var pos = npc.Transform.World.Position;
        var spawnZ = npc.Spawner?.Position.Z ?? pos.Z;
        if (!WorldManager.Instance.TryGetHeight(npc.Transform.ZoneId, pos.X, pos.Y, spawnZ, out var terrain)
            || !NpcGroundResolver.IsUsableHeight(terrain))
            return;
        var resolver = WorldManager.Instance.NpcGroundResolver;
        if (resolver == null)
            return;
        var inWater = resolver.TryWaterSurface(template, npc.Transform.ZoneId, pos.X, pos.Y, spawnZ, out var surface)
                      && surface > spawnZ - 0.05f;
        var ground = resolver.Ground(template, npc.Transform.ZoneId, pos.X, pos.Y, spawnZ, null);
        var floorNear = MathF.Abs(ground - spawnZ) <= NpcGroundResolver.NearBandMetres;
        if (!floorNear && template.GeoData is { } geo)
        {
            floorNear = geo.TryGroundNear(pos.X, pos.Y, spawnZ, out var navNear)
                        && NpcGroundResolver.IsUsableHeight(navNear)
                        && MathF.Abs(navNear - spawnZ) <= NpcGroundResolver.NearBandMetres;
            if (!floorNear)
                floorNear = geo.TryStructureFloor(pos.X, pos.Y, spawnZ, StructureFloorRules.FlatMetres, StructureFloorRules.RiseMetres, out var hall)
                            && MathF.Abs(hall - spawnZ) <= NpcGroundResolver.NearBandMetres;
        }
        if (!IsUnseenFloor(spawnZ, terrain, inWater, floorNear))
            return;
        npc.BindUnseenFloor(spawnZ);
    }
}