#nullable enable
using System.Numerics;

using AAEmu.Game.Models;
using AAEmu.Geometry;

namespace AAEmu.Game.Physics.Static;

/// <summary>
/// Стены останавливают идущих — порт <c>WalkerCollision</c> из ArcheageZones (10.x) на наш индекс статики.
/// <para>
/// Их замеры по 578 нативным трассам (17,7 млн шагов НПЦ вне боя): шаг пересекал кисть на высоте 1,2 м над юнитом
/// 91 раз (0,5 на 100 000 шагов), на 0,6 м — 570 раз (3,2). Нога, кончившаяся медленно, имеет кисть в метре впереди
/// в 27 раз чаще ноги, кончившейся на своей скорости; такая нога встаёт в 0,40–0,74 м от стены (радиус тела),
/// скользит вдоль неё, пока скорость не угаснет, и замирает.
/// </para>
/// <para>
/// Правило: наземный шаг (обход, строй, возврат домой; не погоня, не бегство после боя, не патруль, не плавание)
/// проверяется двумя лучами по кистям зоны — на <see cref="KneeMetres"/> и <see cref="ChestMetres"/> над ногами.
/// Стена (треугольник круче 60°) обрывает шаг за радиус тела до себя, остаток шага скользит вдоль стены; скольжение
/// во вторую стену останавливается там. Выключатель: <c>World.WalkerCollisionEnabled</c> (и
/// <c>AAEMU_WALKER_COLLISION=0</c>).
/// </para>
/// </summary>
public static class WalkerCollision
{
    /// <summary>Включено ли правило: конфиг мира плюс переменная окружения (решается один раз).</summary>
    public static bool Enabled
    {
        get
        {
            if (s_enabled is { } cached)
                return cached;
            // The configuration may not be built yet (tests, offline probes): then the rule is on by default.
            var value = Environment.GetEnvironmentVariable("AAEMU_WALKER_COLLISION") != "0"
                        && (AppConfiguration.Instance?.World?.WalkerCollisionEnabled ?? true);
            s_enabled = value;
            return value;
        }
    }

    private static bool? s_enabled;

    /// <summary>
    /// Нижний луч: чуть выше высоты шага (<see cref="NpcGroundResolver.StepUpMetres"/>), поэтому ступень, на которую
    /// юнит поднимается, стеной не считается.
    /// </summary>
    public const float KneeMetres = 0.6f;

    /// <summary>Верхний луч: стены, чей низ нижний луч пропускает (склон к стене, нависание на высоте корпуса).</summary>
    public const float ChestMetres = 1.2f;

    /// <summary>Треугольник, смотрящий вверх меньше чем на это (|n.z| / |n|), — стена; круче — пол, им занимается опора.</summary>
    public const float WallMaxUpNormal = 0.5f;

    /// <summary>Границы радиуса тела: радиус модели, зажатый (натив встаёт в 0,40–0,74 м от стены, p25–p75).</summary>
    public const float MinRadiusMetres = 0.3f;
    public const float MaxRadiusMetres = 0.6f;

    /// <summary>Радиус тела идущего: радиус актёрской модели, зажатый в <see cref="MinRadiusMetres"/>..<see cref="MaxRadiusMetres"/>.</summary>
    public static float Radius(float actorRadius) => Math.Clamp(actorRadius, MinRadiusMetres, MaxRadiusMetres);

    /// <summary>
    /// Конец наземного шага из <paramref name="from"/> к <paramref name="to"/> (движение плоское; высоты — забота
    /// вызывающего): <paramref name="to"/>, если на пути ничего нет, иначе точка за радиус тела до первой стены плюс
    /// остаток шага, скользнувший вдоль неё. <paramref name="blocked"/> говорит, укоротила ли шаг стена.
    /// </summary>
    public static Vector3 Clip(StaticCollision collision, Vector3 from, Vector3 to, float radius, out bool blocked)
    {
        blocked = false;
        var move = new Vector2(to.X - from.X, to.Y - from.Y);
        var length = move.Length();
        if (length < 1e-4f)
            return to;
        if (!FirstWall(collision, from, move, radius, out var free, out var normal))
            return to;
        blocked = true;
        var direction = move / length;
        var advance = Math.Clamp(free, 0f, length);
        var at = new Vector2(from.X, from.Y) + direction * advance;
        var rest = move - direction * advance;
        var slide = rest - Vector2.Dot(rest, normal) * normal;
        var slideLength = slide.Length();
        if (slideLength > 1e-4f)
        {
            var at3 = new Vector3(at, from.Z);
            if (FirstWall(collision, at3, slide, radius, out var free2, out _))
                at += slide / slideLength * Math.Clamp(free2, 0f, slideLength);
            else
                at += slide;
        }

        return new Vector3(at, to.Z);
    }

    /// <summary>
    /// Первая стена, которую тело встречает, двигаясь на <paramref name="move"/> из <paramref name="from"/>: насколько
    /// оно может пройти (попадание минус радиус, отрицательно, если уже ближе) и плоская нормаль стены к идущему.
    /// </summary>
    public static bool FirstWall(StaticCollision collision, Vector3 from, Vector2 move, float radius, out float free, out Vector2 normal)
    {
        free = float.MaxValue;
        normal = default;
        var length = move.Length();
        if (length < 1e-4f)
            return false;
        var direction = move / length;
        var reach = length + radius;
        var found = false;
        foreach (var height in Heights)
        {
            var a = from + new Vector3(0f, 0f, height);
            var b = a + new Vector3(direction * reach, 0f);
            if (!collision.Raycast(a, b, out var fraction, out var n))
                continue;
            var nl = n.Length();
            if (nl <= 0f || MathF.Abs(n.Z) >= WallMaxUpNormal * nl)
                continue; // пол или пандус: это забота опоры
            var flat = new Vector2(n.X, n.Y);
            if (flat.LengthSquared() < 1e-8f)
                continue;
            flat = Vector2.Normalize(flat);
            if (Vector2.Dot(flat, direction) > 0f)
                flat = -flat;
            // Радиус откладывается поперёк стены, а не по движению: при косом подходе тело иначе подходит ближе.
            var distance = fraction * reach - radius / MathF.Max(-Vector2.Dot(flat, direction), 0.25f);
            if (distance < free)
            {
                free = distance;
                normal = flat;
                found = true;
            }
        }

        return found;
    }

    private static readonly float[] Heights = [KneeMetres, ChestMetres];

    /// <summary>Половина стороны «рядом ничего нет» идущего (<see cref="Probe"/>).</summary>
    public const float ProbeMetres = 3f;

    /// <summary>Насколько высота юнита может уйти от высоты замера, прежде чем замер повторится.</summary>
    public const float ProbeRiseMetres = 0.4f;

    /// <summary>
    /// Экономия одного идущего: два луча на каждый шаг — почти вся цена правила, поэтому идущий сначала спрашивает,
    /// есть ли вообще кисть в <see cref="ProbeMetres"/> на высоте лучей, и пока её нет, лучи не пускает, пока не
    /// выйдет из этого чистого квадрата (или не уйдёт по высоте на <see cref="ProbeRiseMetres"/>).
    /// </summary>
    public sealed class Probe
    {
        private Vector2 _center;
        private float _z;
        private bool _valid;
        private WeakReference<StaticCollision>? _collision;

        /// <summary>Верно, когда шаг из <paramref name="from"/> в <paramref name="to"/> заведомо не встречает кисти.</summary>
        public bool Clear(StaticCollision collision, Vector3 from, Vector3 to, float radius)
        {
            var reach = Vector2.Distance(new Vector2(from.X, from.Y), new Vector2(to.X, to.Y)) + radius;
            var here = new Vector2(from.X, from.Y);
            if (_valid && _collision != null && _collision.TryGetTarget(out var previous) && ReferenceEquals(previous, collision)
                       && MathF.Abs(from.Z - _z) <= ProbeRiseMetres
                       && MathF.Abs(here.X - _center.X) + reach <= ProbeMetres
                       && MathF.Abs(here.Y - _center.Y) + reach <= ProbeMetres)
                return true;
            var min = new Vector3(from.X - ProbeMetres, from.Y - ProbeMetres, from.Z + KneeMetres - ProbeRiseMetres - 0.05f);
            var max = new Vector3(from.X + ProbeMetres, from.Y + ProbeMetres, from.Z + ChestMetres + ProbeRiseMetres + 0.05f);
            _valid = !collision.AnyBoxOverlaps(min, max);
            if (!_valid)
                return false;
            if (_collision == null) _collision = new WeakReference<StaticCollision>(collision);
            else _collision.SetTarget(collision);
            _center = here;
            _z = from.Z;
            return reach <= ProbeMetres;
        }
    }
}
