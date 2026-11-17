using System.Numerics;

namespace AAEmu.Geometry.Source;

/// <summary>Water body from the client data (object type 11 in an object file).</summary>
public enum WaterVolumeType : int
{
    Unknown = 0
}

/// <summary>
/// One water volume record of an object file (object type 11), read as the client stores it: a segment
/// (<c>StartPos</c>→<c>EndPos</c>) with a contour polygon, surface height and depth.
/// <para>
/// This is the geometry package's own reader, so that <see cref="BrushPlacementFile"/> and
/// <see cref="ZoneWater"/> do not depend on the game's prefab model. Field offsets are the ones the
/// game's <c>ObjectDataType11Water</c> has always used, kept verbatim.
/// </para>
/// </summary>
public sealed class WaterVolumeRecord
{
    private const int StartOfVariableData = 0x7B;

    public int VolumeType { get; private set; }
    public ulong VolumeId { get; private set; }
    public Vector3 StartPos { get; private set; } = Vector3.Zero;
    public Vector3 EndPos { get; private set; } = Vector3.Zero;

    /// <summary>Segments inside the body.</summary>
    public List<Vector3> ShapePoints { get; } = [];

    /// <summary>Points forming the outer shape of the body.</summary>
    public List<Vector3> PhysicsContourPoints { get; } = [];

    public float Depth { get; private set; }
    public float Speed { get; private set; }
    public float SurfaceHeight { get; private set; }
    public int MaterialId { get; private set; }

    /// <summary>The volume's surface plane: its normal and the distance along it (the client's <c>FogPlane*</c> fields).</summary>
    public Vector3 PlaneNormal { get; private set; } = new(0f, 0f, 1f);

    /// <summary>The distance of the surface plane from the origin (the client's <c>FogPlaneD</c>).</summary>
    public float PlaneDistance { get; private set; }

    /// <summary>The volume's surface (the higher end of the segment).</summary>
    public float Surface => SurfaceHeight;

    /// <summary>
    /// Reads one record at <paramref name="offset"/>; returns the bytes it spans (0 when the data is not a
    /// water volume).
    /// </summary>
    public int ReadData(byte[] blockData, int offset)
    {
        if (blockData.Length < offset + StartOfVariableData || BitConverter.ToInt32(blockData, offset) != 11)
            return 0;

        StartPos = ReadVector3(blockData, offset + 0x04);
        EndPos = ReadVector3(blockData, offset + 0x10);
        VolumeType = blockData[offset + 0x2B];
        VolumeId = BitConverter.ToUInt64(blockData, offset + 0x2F);
        MaterialId = BitConverter.ToInt32(blockData, offset + 0x37);
        PlaneNormal = ReadVector3(blockData, offset + 0x4B);
        PlaneDistance = BitConverter.ToSingle(blockData, offset + 0x57);
        var shapeCount = BitConverter.ToInt32(blockData, offset + 0x6B);
        Depth = BitConverter.ToSingle(blockData, offset + 0x6F);
        Speed = BitConverter.ToSingle(blockData, offset + 0x73);
        var contourCount = BitConverter.ToInt32(blockData, offset + 0x77);

        SurfaceHeight = Math.Max(EndPos.Z, StartPos.Z);

        var total = StartOfVariableData + 12 * Math.Max(0, shapeCount) + 12 * Math.Max(0, contourCount);
        if (offset + total > blockData.Length)
            return blockData.Length - offset;

        ShapePoints.Clear();
        PhysicsContourPoints.Clear();
        for (var i = 0; i < shapeCount; i++)
            ShapePoints.Add(ReadVector3(blockData, offset + StartOfVariableData + i * 12));
        var contourStart = StartOfVariableData + shapeCount * 12;
        for (var i = 0; i < contourCount; i++)
            PhysicsContourPoints.Add(ReadVector3(blockData, offset + contourStart + i * 12));

        // A zero-point volume (a bare ocean marker) still consumes the fixed header.
        return shapeCount <= 0 && contourCount <= 0 ? StartOfVariableData : total;
    }

    private static Vector3 ReadVector3(byte[] data, int offset) =>
        new(BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), BitConverter.ToSingle(data, offset + 8));
}
