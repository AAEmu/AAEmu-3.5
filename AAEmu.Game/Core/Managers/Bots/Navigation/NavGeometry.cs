using System.Numerics;

namespace AAEmu.Game.Core.Managers.Bots.Navigation;

/// <summary>Flat (x/y) geometry the planner needs. Heights ride along but never decide anything here.</summary>
public static class NavGeometry
{
    /// <summary>Twice the signed area of a, b, c: above 0 when c is left of the line a to b (counter-clockwise).</summary>
    public static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    /// <summary>Flat distance between two points.</summary>
    public static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));

    public static Vector2 Xy(Vector3 v) => new(v.X, v.Y);

    /// <summary>
    /// How far <paramref name="p"/> is inside triangle a, b, c (either winding): the smallest distance to its three
    /// edge lines, negative outside. A degenerate triangle has nothing inside.
    /// </summary>
    public static float Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        var area = Cross(a, b, c);
        if (MathF.Abs(area) < 1e-9f)
            return float.NegativeInfinity;
        var sign = area > 0f ? 1f : -1f;
        return MathF.Min(EdgeSide(p, a, b, sign), MathF.Min(EdgeSide(p, b, c, sign), EdgeSide(p, c, a, sign)));
    }

    private static float EdgeSide(Vector2 p, Vector2 a, Vector2 b, float sign)
    {
        var length = Vector2.Distance(a, b);
        return length < 1e-9f ? float.PositiveInfinity : sign * Cross(a, b, p) / length;
    }

    /// <summary>The point of segment a to b closest to <paramref name="p"/>.</summary>
    public static Vector2 ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        if (lengthSquared < 1e-12f)
            return a;
        var t = Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0f, 1f);
        return a + ab * t;
    }

    /// <summary>The point of triangle a, b, c closest to <paramref name="p"/> (itself when inside).</summary>
    public static Vector2 ClosestOnTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        if (Inside(p, a, b, c) >= 0f)
            return p;
        var best = ClosestOnSegment(p, a, b);
        Closer(p, ClosestOnSegment(p, b, c), ref best);
        Closer(p, ClosestOnSegment(p, c, a), ref best);
        return best;
    }

    private static void Closer(Vector2 p, Vector2 candidate, ref Vector2 best)
    {
        if (Vector2.DistanceSquared(p, candidate) < Vector2.DistanceSquared(p, best))
            best = candidate;
    }

    /// <summary>
    /// Where segment p to q crosses segment a to b, as the fraction along a to b; false when they do not cross
    /// (parallel segments never do).
    /// </summary>
    public static bool Crossing(Vector2 p, Vector2 q, Vector2 a, Vector2 b, out float alongAb) =>
        Crossing(p, q, a, b, out _, out alongAb);

    /// <summary>Distance from <paramref name="p"/> to segment a to b.</summary>
    public static float ToSegment(Vector2 p, Vector2 a, Vector2 b) => Vector2.Distance(p, ClosestOnSegment(p, a, b));

    /// <summary><see cref="Crossing(Vector2, Vector2, Vector2, Vector2, out float)"/>, with the fraction along p to q too.</summary>
    public static bool Crossing(Vector2 p, Vector2 q, Vector2 a, Vector2 b, out float alongPq, out float alongAb)
    {
        alongPq = 0f;
        alongAb = 0f;
        var r = q - p;
        var s = b - a;
        var denominator = r.X * s.Y - r.Y * s.X;
        if (MathF.Abs(denominator) < 1e-9f)
            return false;
        var ap = a - p;
        var t = (ap.X * s.Y - ap.Y * s.X) / denominator;
        var u = (ap.X * r.Y - ap.Y * r.X) / denominator;
        if (t < 0f || t > 1f || u < 0f || u > 1f)
            return false;
        alongPq = t;
        alongAb = u;
        return true;
    }

    /// <summary>Barycentric height of (x, y) on the plane through triangle a, b, c; the average height when it is degenerate.</summary>
    public static float HeightOn(Vector2 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var area = Cross(Xy(a), Xy(b), Xy(c));
        if (MathF.Abs(area) < 1e-9f)
            return (a.Z + b.Z + c.Z) / 3f;
        var wa = Cross(Xy(b), Xy(c), p) / area;
        var wb = Cross(Xy(c), Xy(a), p) / area;
        var wc = 1f - wa - wb;
        return wa * a.Z + wb * b.Z + wc * c.Z;
    }
}
