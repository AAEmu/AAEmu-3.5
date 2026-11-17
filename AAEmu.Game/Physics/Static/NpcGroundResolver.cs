#nullable enable
using AAEmu.Game.Models.Game.World;
using AAEmu.Geometry;

using Geometry = AAEmu.Geometry;

namespace AAEmu.Game.Physics.Static;

/// <summary>
/// Правила опоры НПС — порт <c>GroundResolver</c> из ArcheageZones (10.x) на наши источники данных.
/// <para>
/// Порядок источников (их <c>GroundResolver.BrushOrTerrain</c>):
/// 1. подземный пол (юнит глубже <see cref="NearBandMetres"/> под картой высот) — только пол кисти, никогда рельеф над ним;
/// 2. пол кисти (<see cref="Geometry.StaticCollision.TryFloor"/>) — если он выше рельефа;
/// 3. карта высот + <see cref="GroundRestOffsetMetres"/>;
/// 4. навмеш <c>.bai</c> — где карты высот нет или она далеко от НПС, а слой навигации рядом;
/// 5. текущая высота НПС (ничего не нашли).
/// </para>
/// Отличие от их реализации: навмеш у нас — ближайший узел <c>.bai</c> (<c>AiGeoDataManager</c>), а не треугольник,
/// поэтому он остаётся последним источником и запрашивается лениво (поиск узла — линейный скан);
/// «структурные полы» (waypoints залов) не переносятся.
/// </summary>
public sealed class NpcGroundResolver(StaticGeometryManager geometry)
{
    /// <summary>Полоса «рядом» вокруг текущей высоты НПС (их <c>TerrainFloor.NearBandMetres</c>).</summary>
    public const float NearBandMetres = 3f;

    /// <summary>Насколько выше рельефа стоит наземный НПС (их <c>GroundRestOffsetMetres</c>).</summary>
    public const float GroundRestOffsetMetres = 0.06f;

    /// <summary>Насколько выше пола кисти стоит НПС (их <c>BrushRestOffsetMetres</c>).</summary>
    public const float BrushRestOffsetMetres = 0f;

    /// <summary>На какую высоту НПЦ может встать ступенькой выше себя (их <c>StepUpMetres</c>).</summary>
    public const float StepUpMetres = 0.5f;

    /// <summary>Минимальная глубина под поверхностью воды, чтобы считать НПС плывущим (их <c>SwimMinDepthMetres</c>).</summary>
    public const float SwimMinDepthMetres = 0.3f;

    /// <summary>Минимальный просвет над дном, чтобы НПЦ считался плывущим (их <c>SwimOffBedMetres</c>).</summary>
    public const float SwimOffBedMetres = 0.05f;

    /// <summary>Зазор при посадке на дно (их <c>SpawnerSystem.Clearance</c>).</summary>
    public const float ClearanceMetres = 0.4f;

    /// <summary>
    /// Годна ли высота для опоры юнита. Ноль — не высота: в клиентских данных он означает «нет данных»
    /// (карта высот квантуется от нуля), и юнит, вставший на Z = 0, оказывается под землёй.
    /// </summary>
    public static bool IsUsableHeight(float z) => z > 0f && float.IsFinite(z);

    /// <summary>Use the region containing the queried point when the NPC crosses a zone boundary.</summary>
    public static uint ZoneAt(WorldTemplate template, uint fallback, float x, float y)
    {
        var zones = template.ZoneKeyByRegions;
        if (zones == null || !float.IsFinite(x) || !float.IsFinite(y) || x < 0f || y < 0f) return fallback;
        var sx = (int)(x / AAEmu.Game.Core.Managers.World.WorldManager.REGION_SIZE);
        var sy = (int)(y / AAEmu.Game.Core.Managers.World.WorldManager.REGION_SIZE);
        if (sx >= zones.GetLength(0) || sy >= zones.GetLength(1)) return fallback;
        var key = zones[sx, sy];
        return key == 0 ? fallback : key;
    }

    /// <summary>
    /// Высота земли под (x, y) для НПС, который сейчас на <paramref name="reference"/>.
    /// </summary>
    /// <param name="navHeight">Поставщик высоты навмеша; вызывается при отсутствии или большом удалении рельефа.</param>
    public float Ground(WorldTemplate template, uint zoneKey, float x, float y, float reference, Func<float?>? navHeight = null)
    {
        zoneKey = ZoneAt(template, zoneKey, x, y);
        var floor = BrushOrTerrain(template, zoneKey, x, y, reference, navHeight, out _);
        // Плывущий НПЦ сохраняет свою высоту (натив так и делает) — земля под ним не подтягивает.
        if (Afloat(template, zoneKey, x, y, reference))
            return reference;
        return floor;
    }

    /// <summary>Пол кисти, иначе рельеф, иначе навмеш.</summary>
    public float BrushOrTerrain(WorldTemplate template, uint zoneKey, float x, float y, float reference, Func<float?>? navHeight, out bool onBrush)
    {
        onBrush = false;
        var onTerrain = TerrainHeight(template, x, y, out var terrain);
        // Юнит намного ниже карты высот (пещера, зал под холмом): пол под ним, но никогда рельеф над ним.
        if (onTerrain && terrain - reference > NearBandMetres)
            return UndergroundFloor(template, zoneKey, x, y, reference, navHeight, out onBrush);

        // Без карты высот полосы под кистью нет: тогда нижняя граница — полоса вокруг юнита.
        var below = onTerrain ? terrain : reference - NearBandMetres;
        if (geometry.TryFloor(template, zoneKey, x, y, reference + StepUpMetres, below, out var floor))
        {
            floor += BrushRestOffsetMetres;
            if (IsUsableHeight(floor) && (!onTerrain || floor > terrain))
            {
                onBrush = true;
                return floor;
            }
        }

        if (onTerrain)
        {
            if (MathF.Abs(terrain - reference) > NearBandMetres && navHeight is { } nearbyProvider &&
                nearbyProvider() is { } nearby && IsUsableHeight(nearby) && MathF.Abs(nearby - reference) <= NearBandMetres)
                return nearby;
            return terrain;
        }
        // Навмеш спрашивается последним и лениво: поиск узла в .bai — линейный скан по узлам ячейки.
        if (navHeight is { } provider && provider() is { } mesh && IsUsableHeight(mesh))
            return mesh;
        return reference;
    }

    /// <summary>
    /// Пол под юнитом, который намного ниже карты высот: первая кисть под ним, иначе ближайший слой навмеша,
    /// иначе его собственная высота.
    /// </summary>
    public float UndergroundFloor(WorldTemplate template, uint zoneKey, float x, float y, float reference, Func<float?>? navHeight, out bool onBrush)
    {
        onBrush = geometry.TryFloor(template, zoneKey, x, y, reference + StepUpMetres, reference - NearBandMetres, out var floor);
        if (onBrush && IsUsableHeight(floor))
            return floor + BrushRestOffsetMetres;
        if (navHeight is { } provider && provider() is { } mesh && IsUsableHeight(mesh) && MathF.Abs(mesh - reference) <= NearBandMetres)
            return mesh;
        return reference;
    }

    /// <summary>Твёрдый пол под пловцом: кисть, иначе рельеф (без навмеша — его треугольники моря не пол).</summary>
    public float SolidFloor(WorldTemplate template, uint zoneKey, float x, float y, float reference)
    {
        zoneKey = ZoneAt(template, zoneKey, x, y);
        var onTerrain = TerrainHeight(template, x, y, out var terrain);
        var below = onTerrain ? terrain - GroundRestOffsetMetres : reference - 200f;
        if (geometry.TryFloor(template, zoneKey, x, y, reference + StepUpMetres, below, out var floor) && (!onTerrain || floor > terrain))
            return floor + BrushRestOffsetMetres;
        return onTerrain ? terrain : float.NegativeInfinity;
    }

    /// <summary>
    /// Плывёт ли НПЦ: под поверхностью воды на нужную глубину и выше дна.
    /// </summary>
    /// <remarks>
    /// Порядок проверок важен для горячего пути: сперва дешёвая проверка «есть ли здесь вода» (по сетке объёмов),
    /// и только потом луч до дна. Наземный НПЦ не делает ни одного рейкаста на эту проверку.
    /// </remarks>
    public bool Afloat(WorldTemplate template, uint zoneKey, float x, float y, float reference)
    {
        zoneKey = ZoneAt(template, zoneKey, x, y);
        if (!geometry.TryWaterSurface(template, zoneKey, x, y, reference, out var surface))
            return false;
        if (surface - reference < SwimMinDepthMetres)
            return false;
        var floor = SolidFloor(template, zoneKey, x, y, reference);
        return reference > floor + SwimOffBedMetres;
    }

    /// <summary>
    /// Есть ли стена между двумя точками на высоте <paramref name="height"/> (план P6): горизонтальный луч по индексу
    /// статики. НПЦ не проходит сквозь стены, если вызывающая сторона проверяет шаг этим методом.
    /// </summary>
    /// <param name="height">Абсолютная высота луча в метрах (обычно пол юнита + 1 м — середина корпуса).</param>
    public bool IsBlocked(WorldTemplate template, uint zoneKey, float fromX, float fromY, float toX, float toY, float height)
        => IsBlocked(template, zoneKey, fromX, fromY, height, toX, toY, height);

    /// <summary>
    /// Есть ли стена между двумя точками на разных высотах: короткий луч по индексу статики (движение по склону или
    /// спуск по лестнице идёт под углом, а не в горизонтальной плоскости).
    /// </summary>
    public bool IsBlocked(WorldTemplate template, uint zoneKey, float fromX, float fromY, float fromZ, float toX, float toY, float toZ)
    {
        var from = new System.Numerics.Vector3(fromX, fromY, fromZ);
        var to = new System.Numerics.Vector3(toX, toY, toZ);
        // Only vertical surfaces stop a walk: the floor normal filter would also reject walls, so it is left at 0.
        return geometry.Raycast(template, zoneKey, from, to, out _, out _, floorOnly: false);
    }

    /// <summary>
    /// Свободен ли путь для юнита ростом <paramref name="bodyHeightMetres"/>: луч на уровне середины корпуса между
    /// старой и новой точкой. Удобная обёртка для шага НПЦ — сама точка опоры здесь не вычисляется.
    /// </summary>
    public bool IsClearPath(WorldTemplate template, uint zoneKey, System.Numerics.Vector3 from, System.Numerics.Vector3 to, float bodyHeightMetres)
    {
        // A step shorter than a hand span cannot be blocked in any way that matters, and skipping the walk keeps the
        // AI tick free of thousands of rays when units barely move.
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (dx * dx + dy * dy < 0.0625f)
            return true;
        // The probe follows the step vertically as well (floor height as the game computes it): a walk down a stair is
        // not a horizontal line, and probing it at the old height makes the NPC climb onto the next step's riser.
        return !IsBlocked(template, zoneKey, from.X, from.Y, from.Z + bodyHeightMetres, to.X, to.Y, to.Z + bodyHeightMetres);
    }

    /// <summary>
    /// Конец наземного шага по стенам зоны (<see cref="WalkerCollision"/>): шаг обрывается за радиус тела до первой
    /// стены и скользит вдоль неё. <paramref name="blocked"/> говорит, укоротила ли шаг стена; пустой или ещё не
    /// собранный индекс зоны шаг не трогает (мягкая деградация слоя).
    /// </summary>
    /// <param name="probe">Кэш «рядом нет кистей» этого идущего — хранит вызывающая сторона (у каждого НПЦ свой).</param>
    public System.Numerics.Vector3 ClipWalk(WorldTemplate template, uint zoneKey, System.Numerics.Vector3 from,
        System.Numerics.Vector3 to, float radius, WalkerCollision.Probe? probe, out bool blocked)
    {
        blocked = false;
        if (!WalkerCollision.Enabled)
            return to;
        var sourceZone = ZoneAt(template, zoneKey, from.X, from.Y);
        var targetZone = ZoneAt(template, zoneKey, to.X, to.Y);
        var collision = geometry.GetZone(template, sourceZone);
        if (collision.Instances != 0 && (probe == null || !probe.Clear(collision, from, to, radius)))
        {
            to = WalkerCollision.Clip(collision, from, to, radius, out var hit);
            blocked = hit;
        }
        if (targetZone != sourceZone)
        {
            var target = geometry.GetZone(template, targetZone);
            if (!ReferenceEquals(target, collision) && target.Instances != 0 && (probe == null || !probe.Clear(target, from, to, radius)))
            {
                to = WalkerCollision.Clip(target, from, to, radius, out var hit);
                blocked |= hit;
            }
        }
        return to;
    }

    /// <summary>
    /// Станс, который диктует вода под НПС (пороги <see cref="StanceRules"/>: Swim в толще воды, Settle у дна
    /// на мелководье, иначе <see cref="WaterStance.Unchanged"/>).
    /// </summary>
    /// <remarks>
    /// <see cref="SolidFloor"/> отдаёт высоту опоры (рельеф уже с <see cref="GroundRestOffsetMetres"/>),
    /// поэтому разница между <paramref name="z"/> и опорой равна нулю у стоящего на дне юнита.
    /// Наличие опоры передаётся отдельно от величины зазора под юнитом.
    /// </remarks>
    public WaterStance WaterStanceAt(WorldTemplate template, uint zoneKey, float x, float y, float z)
    {
        zoneKey = ZoneAt(template, zoneKey, x, y);
        if (!geometry.TryWaterSurface(template, zoneKey, x, y, z, out var surface))
            return WaterStance.Unchanged;
        // Наземный НПЦ у дна не плавает, а «садится» — считаем это сразу, без луча до дна.
        if (surface - z < StanceRules.MinDepthMetres)
            return WaterStance.Unchanged;
        var floor = SolidFloor(template, zoneKey, x, y, z);
        var floorKnown = float.IsFinite(floor);
        var heightAboveFloor = floorKnown ? z - floor : 0f;
        return StanceRules.Resolve(surface - z, heightAboveFloor, floorKnown);
    }

    /// <summary>
    /// Поверхность воды (озёра/реки/моря) над точкой; <c>false</c>, если точки нет в объёме воды.
    /// </summary>
    public bool TryWaterSurface(WorldTemplate template, uint zoneKey, float x, float y, float z, out float surface)
        => geometry.TryWaterSurface(template, ZoneAt(template, zoneKey, x, y), x, y, z, out surface);

    /// <summary>Высота рельефа в точке (+ <see cref="GroundRestOffsetMetres"/>); <c>false</c>, если данных нет.</summary>
    public bool TerrainHeight(WorldTemplate template, float x, float y, out float z)
    {
        z = 0f;
        // The client's heightmap uses zero for "no data" (as the zone host's does): a cell without heights must not
        // read as sea level, or the unit walks at Z = 0, under the ground.
        if (!template.TryGetHeight(x, y, out var h) || h <= 0f)
            return false;
        z = h + GroundRestOffsetMetres;
        return true;
    }

    /// <summary>
    /// Высота, на которой НПЦ должен осесть при спавне: пол под точкой, но не телепорт через cliff.
    /// </summary>
    public float SettledZ(WorldTemplate template, uint zoneKey, float spawnX, float spawnY, float spawnZ, float? worldZ = null, Func<float?>? navHeight = null)
    {
        // Понижение самой точки спавна (Clearance) — не высота: от неё рельеф уходит из полосы «рядом» и опора
        // отвергается, хотя она есть. Поэтому отсчёт идёт от точки спавна.
        var reference = spawnZ;
        // Неважно, что говорит правило «опора выше рельефа»: если кисть под точкой есть, НПЦ садится на неё.
        // Ось луча — на высоту шага выше точки спавна: кисть стоит на полу, а точка спавна обычно на её поверхности.
        var below = reference - NearBandMetres;
        var onBrush = geometry.TryFloor(template, zoneKey, spawnX, spawnY, reference + StepUpMetres + BrushRestOffsetMetres, below, out var brush);
        var floor = onBrush
            ? brush + BrushRestOffsetMetres
            : BrushOrTerrain(template, zoneKey, spawnX, spawnY, reference, navHeight, out _);
        // Осесть можно только на настоящую опору: пол кисти или рельеф, и только в пределах полосы «рядом» — НПЦ не
        // телепортируется наверх через скалу и не опускается туда, где опоры нет ("ничего не нашли" отдаёт саму
        // точку отсчёта).
        if (!onBrush && !TerrainHeight(template, spawnX, spawnY, out _))
            return spawnZ;
        if (MathF.Abs(floor - spawnZ) > NearBandMetres)
            return spawnZ;
        // Мир уже опустил НПЦ ниже пола под точкой — не поднимаем его обратно (но и не опускаем ниже полосы).
        if (worldZ is { } w && w < floor)
            return MathF.Max(w, MathF.Max(floor, spawnZ - NearBandMetres));
        return floor;
    }
}
