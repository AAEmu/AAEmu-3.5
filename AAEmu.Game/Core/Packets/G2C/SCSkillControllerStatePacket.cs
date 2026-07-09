using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.Debug;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// SCSkillControllerState (0x21C) — server-to-client packet that updates a skill controller state.
/// </summary>
/// <remarks>
/// x2game client deserializer: <c>sub_39937380</c> (via <c>sub_399384A0</c>).
/// Wire layout derived from IDA:
/// <list type="bullet">
///   <item><description>BC (3-byte compressed) "objId"</description></item>
///   <item><description>byte "scType"</description></item>
///   <item><description>if <c>scType == 0</c>: float "len", bool "teared", bool "cutouted"</description></item>
/// </list>
/// </remarks>
public class SCSkillControllerStatePacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    /// <summary>Object id of the skill controller.</summary>
    public uint ObjId { get; }

    /// <summary>Skill controller type.</summary>
    public byte ScType { get; }

    /// <summary>Controller length/value.</summary>
    public float Length { get; }

    /// <summary>Rope is teared.</summary>
    public bool Teared { get; }

    /// <summary>Rope is cut out.</summary>
    public bool Cutouted { get; }

    public SCSkillControllerStatePacket(uint objId, byte scType, float length, bool teared, bool cutouted)
        : base(SCOffsets.SCSkillControllerStatePacket, 5)
    {
        ObjId = objId;
        ScType = scType;
        Length = length;
        Teared = teared;
        Cutouted = cutouted;
    }

    public override PacketStream Write(PacketStream stream)
    {
        SkillControllerPacketDebug.LogScSkillControllerState(ObjId, ScType, Length, Teared, Cutouted);

        stream.WriteBc(ObjId);
        stream.Write(ScType);

        // Клиент читает len/teared/cutouted только при scType == 0, но сервер всегда шлёт полный набор.
        stream.Write(Length);
        stream.Write(Teared);
        stream.Write(Cutouted);

        return stream;
    }

    public override string Verbose()
    {
        return $" - ObjId {ObjId}, ScType {ScType}, Len {Length}, Teared {Teared}, Cutouted {Cutouted}";
    }
}
