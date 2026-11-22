#nullable enable

using System.Numerics;

using AAEmu.Geometry;

namespace AAEmu.Game.Physics.Hulls;

/// <summary>Flotation surfaces in the hull's local frame.</summary>
public sealed class HullBuoyancy
{
    private readonly Surface[] _boxes = [];
    private readonly Tube? _tube;
    private readonly Surface? _keel;

    internal double Capacity { get; }

    internal HullBuoyancy(List<Surface> boxes)
    {
        _boxes = boxes.ToArray();
        foreach (var box in _boxes)
            Capacity += box.DisplacedVolume(float.PositiveInfinity);
    }

    private HullBuoyancy(Tube tube)
    {
        _tube = tube;
        Capacity = tube.DisplacedVolume(double.PositiveInfinity);
    }

    private HullBuoyancy(HullBuoyancy floats, Surface keel)
    {
        _boxes = floats._boxes;
        _tube = floats._tube;
        _keel = keel;
        Capacity = floats.Capacity + keel.DisplacedVolume(float.PositiveInfinity);
    }

    internal HullBuoyancy WithKeel(Vector3 centre, Vector3 half) => new(this, Surface.FromBox(centre, half));

    internal static HullBuoyancy? FromTube(float radius, float halfLength, float offsetZ)
    {
        if (!float.IsFinite(radius) || radius <= 0f || !float.IsFinite(halfLength) || halfLength <= 0f ||
            !float.IsFinite(offsetZ) || offsetZ == 0f) return null;
        return new HullBuoyancy(new Tube(radius, halfLength, offsetZ));
    }

    internal double DisplacedVolume(float localWaterLevel)
    {
        var volume = (_tube?.DisplacedVolume(localWaterLevel) ?? 0d) + (_keel?.DisplacedVolume(localWaterLevel) ?? 0d);
        foreach (var box in _boxes)
            volume += box.DisplacedVolume(localWaterLevel);
        return volume;
    }

    /// <summary>Clips the float surfaces against a plane with unit normal <paramref name="up"/>, in hull coordinates.</summary>
    internal Submersion Measure(Vector3 up, float level, Vector3 massCentre, Vector3 velocity, Vector3 angularVelocity)
    {
        var result = default(Submersion);
        foreach (var box in _boxes)
            result += box.Measure(up, level, massCentre, velocity, angularVelocity);
        if (_tube is { } tube)
            result += tube.Measure(up, level, massCentre, velocity, angularVelocity);
        if (_keel is { } keel)
            result += keel.Measure(up, level, massCentre, velocity, angularVelocity);
        return result;
    }

    internal readonly record struct Submersion(double Volume, Vector3 FirstMoment, Vector3 Drag, Vector3 DragTorque)
    {
        public static Submersion operator +(Submersion a, Submersion b) =>
            new(a.Volume + b.Volume, a.FirstMoment + b.FirstMoment, a.Drag + b.Drag, a.DragTorque + b.DragTorque);
    }

    private sealed class Tube(float radius, float halfLength, float offsetZ)
    {
        private readonly Surface _surface = CylinderSurface(radius, halfLength, offsetZ);
        // Gauss-Legendre nodes and weights on (-1, 1). Only the partially submerged segment needs quadrature.
        private static ReadOnlySpan<double> Nodes => [-0.9602898565, -0.7966664774, -0.5255324099, -0.1834346425,
            0.1834346425, 0.5255324099, 0.7966664774, 0.9602898565];
        private static ReadOnlySpan<double> Weights => [0.1012285363, 0.2223810345, 0.3137066459, 0.3626837834,
            0.3626837834, 0.3137066459, 0.2223810345, 0.1012285363];

        internal double DisplacedVolume(double level)
        {
            var height = (level - offsetZ) / radius;
            return 2d * halfLength * Disk(height).Area;
        }

        internal Submersion Measure(Vector3 up, float level, Vector3 massCentre, Vector3 velocity, Vector3 angularVelocity)
        {
            var radial = new Vector3(up.X, 0f, up.Z);
            var verticalRadius = radius * radial.Length();
            var axial = (double)up.Y;
            var depth = (double)level - offsetZ * up.Z;
            if (depth <= -verticalRadius - Math.Abs(axial) * halfLength) return default;

            var fullDisk = Math.PI * radius * radius;
            var volume = 0d;
            var axialMoment = 0d;
            var radialMoment = 0d;
            if (verticalRadius < 1e-6d)
            {
                var wetEnd = Math.Clamp(depth / Math.Abs(axial), -halfLength, halfLength);
                volume = (wetEnd + halfLength) * fullDisk;
                axialMoment = Math.Sign(axial) * 0.5d * (wetEnd - halfLength) * volume;
            }
            else if (Math.Abs(axial) < 1e-6d)
            {
                var disk = Disk(depth / verticalRadius);
                volume = disk.Area * (2d * halfLength);
                radialMoment = disk.Moment * (2d * halfLength);
            }
            else
            {
                // Integrate along the end of the axis that slopes up towards the waterplane.
                var slope = Math.Abs(axial);
                var fullEnd = Math.Clamp((depth - verticalRadius) / slope, -halfLength, halfLength);
                var wetEnd = Math.Clamp((depth + verticalRadius) / slope, -halfLength, halfLength);
                volume = (fullEnd + halfLength) * fullDisk;
                axialMoment = 0.5d * (fullEnd - halfLength) * (fullEnd + halfLength) * fullDisk;
                var midpoint = (fullEnd + wetEnd) * 0.5d;
                var halfSpan = (wetEnd - fullEnd) * 0.5d;
                for (var i = 0; i < Nodes.Length && halfSpan > 0d; i++)
                {
                    var y = midpoint + Nodes[i] * halfSpan;
                    var disk = Disk((depth - slope * y) / verticalRadius);
                    var weight = Weights[i] * halfSpan;
                    volume += disk.Area * weight;
                    axialMoment += disk.Area * weight * y;
                    radialMoment += disk.Moment * weight;
                }
                axialMoment *= Math.Sign(axial);
            }
            var moment = (new Vector3(0f, 0f, offsetZ) - massCentre) * (float)volume;
            moment.Y += (float)axialMoment;
            if (radialMoment != 0d)
                moment += Vector3.Normalize(radial) * (float)radialMoment;
            var wet = velocity != Vector3.Zero || angularVelocity != Vector3.Zero
                ? _surface.Measure(up, level, massCentre, velocity, angularVelocity) : default;
            return wet with { Volume = volume, FirstMoment = moment };
        }

        private (double Area, double Moment) Disk(double height)
        {
            if (height <= -1d) return default;
            var scale = (double)radius * radius;
            if (height >= 1d) return (Math.PI * scale, 0d);
            // r575 CCylinderGeom (RVA 0x143230) reduces horizontal displacement to a circular segment.
            var chord = Math.Sqrt(1d - height * height);
            return (Math.Max(0d, Math.Acos(-height) + height * chord) * scale, -2d / 3d * scale * radius * chord * chord * chord);
        }

        private static Surface CylinderSurface(float radius, float halfLength, float offsetZ)
        {
            const int sides = 32;
            var vertices = new Vector3[2 * sides + 2];
            vertices[^2] = new Vector3(0f, -halfLength, offsetZ);
            vertices[^1] = new Vector3(0f, halfLength, offsetZ);
            var triangles = new int[12 * sides];
            for (var i = 0; i < sides; i++)
            {
                var (sin, cos) = MathF.SinCos(i * MathF.Tau / sides);
                var lower = i * 2;
                var upper = lower + 1;
                var next = (lower + 2) % (2 * sides);
                vertices[lower] = new Vector3(radius * cos, -halfLength, offsetZ + radius * sin);
                vertices[upper] = new Vector3(radius * cos, halfLength, offsetZ + radius * sin);
                var j = i * 12;
                triangles[j] = lower;
                triangles[j + 1] = upper;
                triangles[j + 2] = next;
                triangles[j + 3] = next;
                triangles[j + 4] = upper;
                triangles[j + 5] = next + 1;
                triangles[j + 6] = vertices.Length - 2;
                triangles[j + 7] = lower;
                triangles[j + 8] = next;
                triangles[j + 9] = vertices.Length - 1;
                triangles[j + 10] = next + 1;
                triangles[j + 11] = upper;
            }
            return new Surface(vertices, triangles, offsetZ - radius, offsetZ + radius);
        }
    }

    /// <summary>A closed flotation surface, with vertices in the prefab's frame.</summary>
    internal sealed class Surface
    {
        private readonly Vector3[] _vertices;
        private readonly int[] _triangles;
        private readonly float _bottom;
        private readonly float _top;
        private readonly double _fullVolume;
        private readonly float _winding;

        internal Surface(Vector3[] vertices, int[] triangles, float bottom, float top)
        {
            _vertices = vertices;
            _triangles = triangles;
            _bottom = bottom;
            _top = top;
            _winding = 1f;
            var volume = Measure(Vector3.UnitZ, top, Vector3.Zero, Vector3.Zero, Vector3.Zero).Volume;
            _winding = volume < 0d ? -1f : 1f;
            _fullVolume = Math.Abs(volume);
        }

        internal static Surface FromBox(Vector3 centre, Vector3 half)
        {
            var vertices = new Vector3[8];
            for (var i = 0; i < vertices.Length; i++)
                vertices[i] = centre + half * new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
            return new Surface(vertices, [0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4,
                2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5], centre.Z - half.Z, centre.Z + half.Z);
        }

        internal static Surface? FromBoxProxy(CollisionMesh mesh, Matrix4x4 transform)
        {
            if (mesh.VertexCount != 8 || mesh.TriangleCount != 12) return null;
            var vertices = mesh.Vertices.ToArray();
            var bottom = float.PositiveInfinity;
            var top = float.NegativeInfinity;
            for (var i = 0; i < vertices.Length; i++)
            {
                var vertex = Vector3.Transform(vertices[i], transform);
                if (!HullBody.IsFinite(vertex)) return null;
                vertices[i] = vertex;
                bottom = MathF.Min(bottom, vertex.Z);
                top = MathF.Max(top, vertex.Z);
            }

            var box = new Surface(vertices, mesh.Triangles.ToArray(), bottom, top);
            return double.IsFinite(box._fullVolume) && box._fullVolume > 0d ? box : null;
        }

        internal double DisplacedVolume(float level)
        {
            if (level <= _bottom) return 0d;
            if (level >= _top) return _fullVolume;
            return Math.Min(Measure(Vector3.UnitZ, level, Vector3.Zero, Vector3.Zero, Vector3.Zero).Volume, _fullVolume);
        }

        internal Submersion Measure(Vector3 up, float level, Vector3 massCentre, Vector3 velocity, Vector3 angularVelocity)
        {
            if (up == Vector3.UnitZ && level <= _bottom) return default;
            // Tetrahedra with their origin on the surface have zero volume for the waterplane cap.
            var first = _vertices[0] - massCentre;
            var height = level - Vector3.Dot(up, massCentre);
            var origin = up == Vector3.UnitZ && level > _top ? first : first + up * (height - Vector3.Dot(up, first));
            var volume = 0d;
            var moment = Vector3.Zero;
            var drag = Vector3.Zero;
            var dragTorque = Vector3.Zero;
            var moving = velocity != Vector3.Zero || angularVelocity != Vector3.Zero;
            Span<Vector3> polygon = stackalloc Vector3[4];
            Span<Vector3> face = stackalloc Vector3[3];
            Span<Vector3> entering = stackalloc Vector3[5];
            for (var i = 0; i < _triangles.Length; i += 3)
            {
                for (var j = 0; j < face.Length; j++)
                    face[j] = _vertices[_triangles[i + j]] - massCentre;
                var count = Clip(face, -up, height, polygon);
                for (var j = 1; j + 1 < count; j++)
                {
                    var v = _winding / 6f * Vector3.Dot(polygon[0] - origin, Vector3.Cross(polygon[j] - origin, polygon[j + 1] - origin));
                    volume += v;
                    moment += (origin + polygon[0] + polygon[j] + polygon[j + 1]) * (v * 0.25f);
                }

                if (!moving || count < 3) continue;
                var normal = Vector3.Cross(face[1] - face[0], face[2] - face[0]);
                var length = normal.Length();
                if (length <= 0f) continue;
                normal *= _winding / length;
                // r575 CBoxGeom (0x13e7c0) clips the wet face again where its normal velocity becomes negative.
                var gradient = Vector3.Cross(normal, angularVelocity);
                var approach = Vector3.Dot(normal, velocity);
                var enteringCount = Clip(polygon[..count], gradient, approach, entering);
                for (var j = 1; j + 1 < enteringCount; j++)
                {
                    var a = entering[0];
                    var b = entering[j];
                    var c = entering[j + 1];
                    var area = Vector3.Cross(b - a, c - a).Length() * 0.5f;
                    // Linear pressure and its first moment integrate exactly at the three symmetric points.
                    var sum = (a + b + c) / 6f;
                    Accumulate(sum + a * 0.5f, area / 3f, normal, gradient, approach, ref drag, ref dragTorque);
                    Accumulate(sum + b * 0.5f, area / 3f, normal, gradient, approach, ref drag, ref dragTorque);
                    Accumulate(sum + c * 0.5f, area / 3f, normal, gradient, approach, ref drag, ref dragTorque);
                }
            }
            return new Submersion(volume, moment, drag, dragTorque);
        }

        private static int Clip(ReadOnlySpan<Vector3> input, Vector3 gradient, float offset, Span<Vector3> output)
        {
            var count = 0;
            var previous = input[^1];
            var previousDistance = Vector3.Dot(gradient, previous) + offset;
            foreach (var current in input)
            {
                var distance = Vector3.Dot(gradient, current) + offset;
                if ((distance > 0f) != (previousDistance > 0f))
                    output[count++] = Vector3.Lerp(previous, current, previousDistance / (previousDistance - distance));
                if (distance > 0f)
                    output[count++] = current;
                previous = current;
                previousDistance = distance;
            }
            return count;
        }

        private static void Accumulate(Vector3 point, float area, Vector3 normal, Vector3 gradient, float approach,
            ref Vector3 force, ref Vector3 torque)
        {
            var pressure = normal * (-area * MathF.Max(0f, Vector3.Dot(gradient, point) + approach));
            force += pressure;
            torque += Vector3.Cross(point, pressure);
        }
    }
}
