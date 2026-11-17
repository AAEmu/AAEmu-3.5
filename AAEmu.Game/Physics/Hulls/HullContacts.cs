#nullable enable

using System.Numerics;

using AAEmu.Game.Models.Game.Models;
using AAEmu.Geometry;

namespace AAEmu.Game.Physics.Hulls;

/// <summary>
/// A hull's collision shape: its slave box's footprint (x starboard, y bow, centre relative to the pivot), how deep its
/// keel reaches under the water and how high its sides stand over it.
/// </summary>
public readonly record struct HullShape(float HalfWidth, float HalfLength, float CentreX, float CentreY, float KeelDepth, float Freeboard)
{
    /// <summary>The Harpoon Clipper's box (slave 14), the one hull with a measured keel.</summary>
    public static readonly Vector3 ReferenceCentre = new(0.019f, 2.949f, 11.434f);
    public static readonly Vector3 ReferenceSize = new(8.257f, 21.151f, 25.033f);

    /// <summary>
    /// How far under its pivot (the reported z) a hull's keel reaches. 5869 (Harpoon Clipper, pivot 99.63 over an ocean
    /// at 100) touched the seabed where it lies at 98.83–98.93: 0.78 m. Other hulls: scaled by beam (a guess until another
    /// model grounds in a trace). Measured from the pivot, not the water: 6013 (Eznan Cutter) rides with its pivot 0.45 m
    /// over the water and passed over a 98.35 m bed that a water-relative keel scaled by beam (1.7 m) would have hit.
    /// </summary>
    public const float ReferenceKeelDepth = 0.78f;

    /// <summary>
    /// How high the sides stand over the water. 6013 (Eznan Cutter, 12 m beam) struck a pier 3.5 m over the water; scaled by
    /// beam that is 2.4 m for the Clipper.
    /// </summary>
    public const float ReferenceFreeboard = 2.4f;

    public float Length => 2 * HalfLength;
    public float Beam => 2 * HalfWidth;

    public static HullShape For(ShipModelV1? ship, Vector3 authoredSize = default) => FromAuthoredSize(authoredSize);

    /// <summary>The slave OBB, or the upstream reference when that data is absent. The mass box is a different shape.</summary>
    public static HullShape FromAuthoredSize(Vector3 size)
    {
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0f || size.Y <= 0f)
        {
            size = ReferenceSize;
        }

        // The footprint is centred on the pivot: native's contacts lie half the box's length from the pivot (6013's bow at
        // 21.8–23.1 m of a 22.8 m half length, 5869's at 9.6 of 10.6), not from the box's centre (obb_pos y 5.7 / 2.9 m
        // forward: the masts' and sails' extent).
        var scale = size.X / ReferenceSize.X;
        return new HullShape(size.X / 2, size.Y / 2, 0f, 0f, ReferenceKeelDepth * scale, ReferenceFreeboard * scale);
    }
}

public enum HullContactKind : byte
{
    Terrain,
    Brush,
    Doodad,
    Hull
}

/// <summary>
/// One contact of a hull: where (continent x/y, z), the push-out direction (horizontal, unit), how deep, against what.
/// <see cref="OtherBcId"/> is the other hull for <see cref="HullContactKind.Hull"/>.
/// </summary>
public readonly record struct HullContact(Vector2 Point, float Z, Vector2 Normal, float Penetration, HullContactKind Kind, uint OtherBcId = 0);

/// <summary>
/// Ship collisions (plan slice 7 follow-up). Contacts against the terrain under the keel, the zone's static brushes and
/// ship-blocking doodads at the hull's sides, and other hulls; each resolved by an inelastic impulse at the contact
/// point (the box's moment of inertia: on 5869's hit it gives native's yaw kick within 25 %) with Coulomb friction, then
/// a push out of the obstacle. What native showed (191, 2026-11-05): 5869 ran its port bow onto a shelving shore at
/// 11.4 m/s (6.8 m/s one frame later, the turn rate doubled, then pinned while the engine pushed); 6013 glanced off a
/// pier (10.4 → 2.9 m/s) and then hit it head on (7.8 → 0.1 m/s).
/// </summary>
public static class HullContacts
{
    /// <summary>Spacing of the points sampled along the hull's outline.</summary>
    public const float SampleSpacing = 2f;
    /// <summary>Most a single step pushes a hull out of an obstacle.</summary>
    public const float MaxPushPerStep = 0.5f;
    /// <summary>
    /// Friction against the seabed and against walls and hulls. Swept on the two native hits (0.6/0.3, 1/0.6, 1/1, 2/1):
    /// 1/0.6 ends 2.5 m and 0.07 m from native 3 s after the hit (5869 slides free and speeds up again below it: native's
    /// stayed wedged; 6013 is thrown 9 m off above it).
    /// </summary>
    public const float TerrainFriction = 1.0f;
    public const float WallFriction = 0.6f;

    /// <summary>The bow (−sin yaw, cos yaw) and starboard (cos yaw, sin yaw) of a hull.</summary>
    public static (Vector2 Bow, Vector2 Starboard) Axes(float yaw) => (new(-MathF.Sin(yaw), MathF.Cos(yaw)), new(MathF.Cos(yaw), MathF.Sin(yaw)));

    /// <summary>A body-frame point (x starboard, y bow) in continent x/y.</summary>
    public static Vector2 ToWorld(HullBody body, float bx, float by)
    {
        var (bow, stb) = Axes(body.Yaw);
        return new Vector2((float)body.X, (float)body.Y) + stb * bx + bow * by;
    }

    /// <summary>The outline's sample points in the body frame: both sides, bow and stern edges, and the keel line.</summary>
    public static List<Vector2> Outline(HullShape shape)
    {
        var points = new List<Vector2>();
        var along = Math.Max(1, (int)MathF.Ceiling(shape.Length / SampleSpacing));
        var across = Math.Max(1, (int)MathF.Ceiling(shape.Beam / SampleSpacing));
        for (var i = 0; i <= along; i++)
        {
            var y = shape.CentreY - shape.HalfLength + shape.Length * i / along;
            points.Add(new Vector2(shape.CentreX - shape.HalfWidth, y));
            points.Add(new Vector2(shape.CentreX + shape.HalfWidth, y));
            points.Add(new Vector2(shape.CentreX, y));
        }

        for (var j = 1; j < across; j++)
        {
            var x = shape.CentreX - shape.HalfWidth + shape.Beam * j / across;
            points.Add(new Vector2(x, shape.CentreY + shape.HalfLength));
            points.Add(new Vector2(x, shape.CentreY - shape.HalfLength));
        }

        return points;
    }

    /// <summary>
    /// The hull's contacts with the static world: terrain over its keel line, and brush or doodad walls met by a ray from
    /// the box's centre line to each outline point at keel, waterline and freeboard heights.
    /// </summary>
    public static void FindStatic(HullBody body, HullShape shape, IReadOnlyList<Vector2> outline, Func<float, float, float?>? terrain,
        StaticCollision collision, StaticCollision? doodads, List<HullContact> into, Func<float, float, StaticCollision?>? staticAt = null)
    {
        var water = body.WaterLevel;
        var keel = body.Z - shape.KeelDepth;
        if (terrain != null)
        {
            foreach (var local in outline)
            {
                var p = ToWorld(body, local.X, local.Y);
                var height = terrain(p.X, p.Y);
                if (height is not { } h || !float.IsFinite(h) || h <= keel)
                    continue;
                into.Add(new HullContact(p, h, TerrainNormal(terrain, p, body, h), h - keel, HullContactKind.Terrain));
            }
        }

        if (collision.Instances == 0 && (doodads == null || doodads.Instances == 0) && staticAt == null)
            return;
        // The sides from just over the waterline to the gunwale; the bottom is the terrain check's (a ray at keel height
        // met an underwater ledge 5869 sailed over on native).
        ReadOnlySpan<float> heights = [water + 0.2f, water + shape.Freeboard * 0.5f, water + shape.Freeboard];
        foreach (var local in outline)
        {
            // Bow and stern points look along the hull (from amidships), side points across it (from the keel line);
            // the corners both ways. A ray from inside an obstacle meets nothing, so each must start inside the hull.
            var onEnd = MathF.Abs(MathF.Abs(local.Y - shape.CentreY) - shape.HalfLength) < 0.01f;
            var onSide = local.X != shape.CentreX;
            if (onEnd)
                Rays(body, new Vector2(local.X, shape.CentreY), local, heights, collision, doodads, into, staticAt);
            if (onSide)
                Rays(body, new Vector2(shape.CentreX, local.Y), local, heights, collision, doodads, into, staticAt);
        }
    }

    private static void Rays(HullBody body, Vector2 fromLocal, Vector2 toLocal, ReadOnlySpan<float> heights, StaticCollision collision,
        StaticCollision? doodads, List<HullContact> into, Func<float, float, StaticCollision?>? staticAt)
    {
        var from2 = ToWorld(body, fromLocal.X, fromLocal.Y);
        var to2 = ToWorld(body, toLocal.X, toLocal.Y);
        var length = Vector2.Distance(from2, to2);
        if (length < 0.01f)
            return;
        var adjacent = staticAt?.Invoke(to2.X, to2.Y);
        foreach (var z in heights)
        {
            var from = new Vector3(from2, z);
            var to = new Vector3(to2, z);
            var fraction = float.PositiveInfinity;
            var normal = Vector3.Zero;
            var kind = HullContactKind.Brush;
            NearestWall(collision, from, to, HullContactKind.Brush, ref fraction, ref normal, ref kind);
            if (adjacent != null && !ReferenceEquals(adjacent, collision))
                NearestWall(adjacent, from, to, HullContactKind.Brush, ref fraction, ref normal, ref kind);
            if (doodads != null)
                NearestWall(doodads, from, to, HullContactKind.Doodad, ref fraction, ref normal, ref kind);
            if (float.IsFinite(fraction))
                into.Add(WallContact(from, to, length, fraction, normal, kind));
        }
    }

    private static void NearestWall(StaticCollision collision, Vector3 from, Vector3 to, HullContactKind candidateKind,
        ref float bestFraction, ref Vector3 bestNormal, ref HullContactKind bestKind)
    {
        if (collision.Instances == 0 || !collision.Raycast(from, to, out var fraction, out var normal) || fraction >= bestFraction)
            return;
        bestFraction = fraction;
        bestNormal = normal;
        bestKind = candidateKind;
    }

    private static HullContact WallContact(Vector3 from, Vector3 to, float length, float fraction, Vector3 normal, HullContactKind kind)
    {
        var hit = from + (to - from) * fraction;
        var inward = new Vector2(from.X - to.X, from.Y - to.Y);
        var n = new Vector2(normal.X, normal.Y);
        // The push-out runs back along the ray unless the wall says otherwise; a wall seen from behind is flipped.
        n = n.LengthSquared() > 1e-6f ? Vector2.Normalize(n) : Vector2.Normalize(inward);
        if (Vector2.Dot(n, inward) < 0f)
            n = -n;
        return new HullContact(new Vector2(hit.X, hit.Y), hit.Z, n, (1f - fraction) * length, kind);
    }

    /// <summary>Downhill on the heightmap (away from the rising shore); toward the hull's centre where the bed is flat.</summary>
    private static Vector2 TerrainNormal(Func<float, float, float?> terrain, Vector2 p, HullBody body, float height)
    {
        const float d = 1f;
        var gx = TerrainSample(terrain, p.X + d, p.Y, height) - TerrainSample(terrain, p.X - d, p.Y, height);
        var gy = TerrainSample(terrain, p.X, p.Y + d, height) - TerrainSample(terrain, p.X, p.Y - d, height);
        var down = new Vector2(-gx, -gy);
        var lengthSquared = down.LengthSquared();
        if (float.IsFinite(lengthSquared) && lengthSquared > 1e-4f)
            return Vector2.Normalize(down);
        var toCentre = new Vector2((float)body.X, (float)body.Y) - p;
        return toCentre.LengthSquared() > 1e-6f ? Vector2.Normalize(toCentre) : Vector2.UnitY;
    }

    private static float TerrainSample(Func<float, float, float?> terrain, float x, float y, float fallback) =>
        terrain(x, y) is { } height && float.IsFinite(height) ? height : fallback;

    /// <summary>The box's moment of inertia about the vertical axis.</summary>
    public static float Inertia(HullShape shape, float mass) => mass * (shape.Length * shape.Length + shape.Beam * shape.Beam) / 12f;

    /// <summary>
    /// Resolves one static contact on <paramref name="body"/>: removes the velocity of the contact point into the obstacle
    /// (inelastic), applies friction along it, pushes the hull out. Returns the speed into the obstacle it removed (the
    /// report's impact), 0 when the point was already moving away.
    /// </summary>
    public static float Resolve(HullBody body, HullShape shape, float mass, HullContact contact, bool pushOut = true)
    {
        var centre = new Vector2((float)body.X, (float)body.Y);
        var r = contact.Point - centre;
        var n = contact.Normal;
        var inertia = Inertia(shape, mass);
        var v = new Vector2(body.VelocityX, body.VelocityY);
        var w = body.YawRate;
        var pointVelocity = v + w * new Vector2(-r.Y, r.X);
        var vn = Vector2.Dot(pointVelocity, n);
        var removed = 0f;
        if (vn < 0f)
        {
            var rn = Cross(r, n);
            var j = -vn / (1f / mass + rn * rn / inertia);
            var t = new Vector2(-n.Y, n.X);
            var rt = Cross(r, t);
            var vt = Vector2.Dot(pointVelocity, t);
            var friction = contact.Kind == HullContactKind.Terrain ? TerrainFriction : WallFriction;
            var jt = Math.Clamp(-vt / (1f / mass + rt * rt / inertia), -friction * j, friction * j);
            v += (n * j + t * jt) / mass;
            w += (rn * j + rt * jt) / inertia;
            body.SetWorldVelocity(v.X, v.Y);
            body.YawRate = Math.Clamp(w, -HullBody.MaxYawRate, HullBody.MaxYawRate);
            removed = -vn;
        }

        if (pushOut)
        {
            var push = n * MathF.Min(contact.Penetration, MaxPushPerStep);
            body.X += push.X;
            body.Y += push.Y;
        }

        return removed;
    }

    /// <summary>
    /// The deepest point of <paramref name="a"/>'s outline inside <paramref name="b"/>'s footprint, as a contact on a whose
    /// push-out is b's nearest side's outward normal; null when the footprints do not touch.
    /// </summary>
    public static HullContact? FindPair(HullBody a, HullShape shapeA, IReadOnlyList<Vector2> outlineA, HullBody b, HullShape shapeB, uint bBcId)
    {
        var (bowB, stbB) = Axes(b.Yaw);
        var centreB = ToWorld(b, shapeB.CentreX, shapeB.CentreY);
        var reach = shapeA.HalfLength + shapeB.HalfLength + shapeA.HalfWidth + shapeB.HalfWidth;
        if (Vector2.DistanceSquared(ToWorld(a, shapeA.CentreX, shapeA.CentreY), centreB) > reach * reach)
            return null;
        HullContact? best = null;
        foreach (var local in outlineA)
        {
            var p = ToWorld(a, local.X, local.Y);
            var d = p - centreB;
            var along = Vector2.Dot(d, bowB);
            var across = Vector2.Dot(d, stbB);
            var inAlong = shapeB.HalfLength - MathF.Abs(along);
            var inAcross = shapeB.HalfWidth - MathF.Abs(across);
            if (inAlong <= 0f || inAcross <= 0f)
                continue;
            var (depth, normal) = inAcross < inAlong
                ? (inAcross, stbB * MathF.Sign(across == 0f ? 1f : across))
                : (inAlong, bowB * MathF.Sign(along == 0f ? 1f : along));
            if (best == null || depth > best.Value.Penetration)
                best = new HullContact(p, b.WaterLevel, normal, depth, HullContactKind.Hull, bBcId);
        }

        return best;
    }

    /// <summary>How fast <paramref name="a"/>'s contact point closes on <paramref name="b"/>'s along the contact's normal (m/s, positive: closing).</summary>
    public static float ClosingSpeed(HullBody a, HullBody b, HullContact contact)
    {
        var ra = contact.Point - new Vector2((float)a.X, (float)a.Y);
        var rb = contact.Point - new Vector2((float)b.X, (float)b.Y);
        var va = new Vector2(a.VelocityX, a.VelocityY) + a.YawRate * new Vector2(-ra.Y, ra.X);
        var vb = new Vector2(b.VelocityX, b.VelocityY) + b.YawRate * new Vector2(-rb.Y, rb.X);
        return -Vector2.Dot(va - vb, contact.Normal);
    }

    /// <summary>
    /// Resolves a hull-hull contact (on <paramref name="a"/>, normal out of b) between two free bodies, or against an
    /// immovable b (a hull whose simulation is off: moored). Returns the closing speed removed.
    /// </summary>
    public static float ResolvePair(HullBody a, HullShape shapeA, float massA, HullBody b, HullShape shapeB, float massB, bool bMoves, HullContact contact)
    {
        var n = contact.Normal;
        var ra = contact.Point - new Vector2((float)a.X, (float)a.Y);
        var rb = contact.Point - new Vector2((float)b.X, (float)b.Y);
        var ia = Inertia(shapeA, massA);
        var ib = Inertia(shapeB, massB);
        var va = new Vector2(a.VelocityX, a.VelocityY) + a.YawRate * new Vector2(-ra.Y, ra.X);
        var vb = bMoves ? new Vector2(b.VelocityX, b.VelocityY) + b.YawRate * new Vector2(-rb.Y, rb.X) : Vector2.Zero;
        var vn = Vector2.Dot(va - vb, n);
        var removed = 0f;
        if (vn < 0f)
        {
            var rna = Cross(ra, n);
            var rnb = Cross(rb, n);
            var denominator = 1f / massA + rna * rna / ia + (bMoves ? 1f / massB + rnb * rnb / ib : 0f);
            var j = -vn / denominator;
            var newA = new Vector2(a.VelocityX, a.VelocityY) + n * (j / massA);
            a.SetWorldVelocity(newA.X, newA.Y);
            a.YawRate = Math.Clamp(a.YawRate + rna * j / ia, -HullBody.MaxYawRate, HullBody.MaxYawRate);
            if (bMoves)
            {
                var newB = new Vector2(b.VelocityX, b.VelocityY) - n * (j / massB);
                b.SetWorldVelocity(newB.X, newB.Y);
                b.YawRate = Math.Clamp(b.YawRate - rnb * j / ib, -HullBody.MaxYawRate, HullBody.MaxYawRate);
            }

            removed = -vn;
        }

        var push = MathF.Min(contact.Penetration, MaxPushPerStep);
        var share = bMoves ? 0.5f : 1f;
        a.X += n.X * push * share;
        a.Y += n.Y * push * share;
        if (bMoves)
        {
            b.X -= n.X * push * share;
            b.Y -= n.Y * push * share;
        }

        return removed;
    }

    /// <summary>
    /// The damage part (<c>SlaveCollisionPart</c>) a contact point is on: the front or rear third of the box, else the
    /// bottom for the seabed and the side for walls. Native named 5869's port-bow grounding (9.5 m ahead of a 10.6 m half
    /// length) and 6013's bow-tip strikes Front.
    /// </summary>
    public static byte Part(HullBody body, HullShape shape, HullContact contact)
    {
        var (bow, _) = Axes(body.Yaw);
        var forward = Vector2.Dot(contact.Point - ToWorld(body, shape.CentreX, shape.CentreY), bow);
        if (forward > shape.HalfLength / 3f)
            return 0; // front
        if (forward < -shape.HalfLength / 3f)
            return 2; // rear
        return contact.Kind == HullContactKind.Terrain ? (byte)3 : (byte)1;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
