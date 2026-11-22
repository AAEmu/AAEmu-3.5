using System.Numerics;

namespace AAEmu.Geometry;

/// <summary>An axis-aligned bounding box in metres.</summary>
public readonly record struct GeometryBounds(Vector3 Min, Vector3 Max)
{
    public GeometryBounds Union(GeometryBounds other) =>
        new(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));

    public GeometryBounds Transform(Matrix4x4 transform)
    {
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) != 0 ? Max.X : Min.X, (i & 2) != 0 ? Max.Y : Min.Y, (i & 4) != 0 ? Max.Z : Min.Z);
            var p = Vector3.Transform(corner, transform);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return new(min, max);
    }
}
