using System.Numerics;

using AAEmu.Game.Core.Managers.Bots.Navigation;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Physics.Static;

namespace AAEmu.Game.Models.Game.AI;

/// <summary>Опора точки из <c>npc_spawners.g</c> до появления НПС (Zones <c>SpawnerSystem</c> settle).</summary>
public static class NpcSpawnHeightRules
{
    public static float Refine(WorldInstance world, uint zoneKey, float x, float y, float hintZ)
    {
        var template = world.Template;
        if (!NpcGroundResolver.IsUsableHeight(hintZ))
            hintZ = GuessReferenceZ(template, x, y);
        var navHeight = BuildNavHeight(template, x, y, hintZ);

        if (AppConfiguration.Instance.World.StaticGeometryEnabled)
        {
            var settled = WorldManager.Instance.NpcGroundResolver.SettledZ(
                template, zoneKey, x, y, hintZ, null, navHeight);
            if (NpcGroundResolver.IsUsableHeight(settled))
                return settled;
        }

        if (template.TryGetHeight(x, y, out var terrain) && terrain > 0f)
            return terrain + NpcGroundResolver.GroundRestOffsetMetres;

        if (NavMeshGround.IsEnabled)
        {
            var nav = new NavMeshGround(template);
            if (nav.TryGroundNear(x, y, hintZ, out var tri) && NpcGroundResolver.IsUsableHeight(tri))
                return tri;
            if (nav.TryGround(x, y, out var ground) && NpcGroundResolver.IsUsableHeight(ground))
                return ground;
        }

        return NpcGroundResolver.IsUsableHeight(hintZ) ? hintZ : GuessReferenceZ(template, x, y);
    }

    private static float GuessReferenceZ(WorldTemplate template, float x, float y)
    {
        if (template.TryGetHeight(x, y, out var terrain) && terrain > 0f)
            return terrain + NpcGroundResolver.GroundRestOffsetMetres;
        if (template.GeoData is { } geo)
        {
            if (geo.TryGround(x, y, out var mesh) && NpcGroundResolver.IsUsableHeight(mesh))
                return mesh;
            if (geo.TryGroundNear(x, y, 512f, out var near) && NpcGroundResolver.IsUsableHeight(near))
                return near;
        }

        return NpcGroundResolver.GroundRestOffsetMetres + 1f;
    }

    private static Func<float?>? BuildNavHeight(WorldTemplate template, float x, float y, float reference) =>
        WorldManager.NavHeightForSpawn(template, x, y, reference);
}