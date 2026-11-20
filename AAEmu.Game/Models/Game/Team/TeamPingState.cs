using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.Team;

/// <summary>Shared body of CSSetPingPos (after its team ID) and SCTeamPingPos.</summary>
public sealed class TeamPingState
{
    public const byte MapPingType = 1;
    private const byte LinePingType = 4;
    private const int SlotCount = 6;
    private const int PositionSize = 9;
    private const int MaxLinePoints = 20;

    private readonly record struct Ping(bool Active, Vector3 Position, uint InstanceId);

    private readonly Ping[] _pings;
    private readonly Vector3[] _linePoints;

    public static TeamPingState Empty { get; } = new(MapPingType, new Ping[SlotCount], []);
    public byte ChangedType { get; }

    private TeamPingState(byte changedType, Ping[] pings, Vector3[] linePoints)
    {
        ChangedType = changedType;
        _pings = pings;
        _linePoints = linePoints;
    }

    public static TeamPingState FromMapPosition(WorldSpawnPosition position, uint instanceId = 0)
    {
        if (position is null or { X: 0, Y: 0 })
            return Empty;

        var pings = new Ping[SlotCount];
        pings[MapPingType] = new Ping(true, position.AsPositionVector(), instanceId);
        return new TeamPingState(MapPingType, pings, []);
    }

    public WorldSpawnPosition GetMapPosition()
    {
        var ping = _pings[MapPingType];
        if (!ping.Active)
            return new WorldSpawnPosition();

        return new WorldSpawnPosition { X = ping.Position.X, Y = ping.Position.Y, Z = ping.Position.Z };
    }

    public TeamPingState WithUpdate(TeamPingState update)
    {
        var pings = (Ping[])_pings.Clone();
        pings[update.ChangedType] = update._pings[update.ChangedType];
        var linePoints = update.ChangedType == LinePingType ? update._linePoints : _linePoints;
        return new TeamPingState(update.ChangedType, pings, linePoints);
    }

    public static TeamPingState Read(PacketStream stream)
    {
        // Type, flags, six packed positions, two PISC headers with six IDs, and line count.
        const int minSize = 2 + SlotCount * PositionSize + 2 + SlotCount + 1;
        if (stream.Count - stream.Pos < minSize)
            return null;

        var changedType = stream.ReadByte();
        if (changedType is < MapPingType or >= SlotCount)
            return null;

        var flags = stream.ReadByte();
        var pings = new Ping[SlotCount];
        for (var i = 0; i < SlotCount; i++)
        {
            var position = ReadPosition(stream);
            pings[i] = new Ping((flags & (1 << i)) != 0, position, 0);
        }

        if (!ReadInstanceIds(stream, pings.AsSpan(0, 4)) ||
            !ReadInstanceIds(stream, pings.AsSpan(4, 2)) ||
            stream.Pos == stream.Count)
            return null;

        var lineCount = stream.ReadByte();
        // The decrypted level-5 frame can include trailing block padding.
        if (lineCount > MaxLinePoints || stream.Count - stream.Pos < lineCount * PositionSize)
            return null;

        var linePoints = new Vector3[lineCount];
        for (var i = 0; i < linePoints.Length; i++)
            linePoints[i] = ReadPosition(stream);

        return new TeamPingState(changedType, pings, linePoints);
    }

    public PacketStream Write(PacketStream stream)
    {
        var flags = 0;
        for (var i = 0; i < SlotCount; i++)
        {
            if (_pings[i].Active)
                flags |= 1 << i;
        }

        stream.Write(ChangedType);
        stream.Write((byte)flags);
        foreach (var ping in _pings)
            stream.WritePosition(ping.Position);

        stream.WritePisc(GetInstanceId(0), GetInstanceId(1), GetInstanceId(2), GetInstanceId(3));
        stream.WritePisc(GetInstanceId(4), GetInstanceId(5));
        stream.Write((byte)_linePoints.Length);
        foreach (var point in _linePoints)
            stream.WritePosition(point);

        return stream;
    }

    private uint GetInstanceId(int slot) => _pings[slot].Active ? _pings[slot].InstanceId : 0;

    private static Vector3 ReadPosition(PacketStream stream)
    {
        var (x, y, z) = stream.ReadPosition();
        return new Vector3(x, y, z);
    }

    private static bool ReadInstanceIds(PacketStream stream, Span<Ping> pings)
    {
        // PacketStream.ReadPisc returns zero for missing bytes, so check the block before reading.
        if (stream.Pos >= stream.Count)
            return false;

        var header = stream[stream.Pos];
        var size = 1;
        for (var i = 0; i < pings.Length; i++)
            size += 1 + ((header >> (i * 2)) & 3);

        if (stream.Count - stream.Pos < size)
            return false;

        var ids = stream.ReadPisc(pings.Length);
        for (var i = 0; i < pings.Length; i++)
            pings[i] = pings[i] with { InstanceId = (uint)ids[i] };

        return true;
    }
}
