using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// SCSkillEnded (0x1BC) — server-to-client packet that signals the end of a skill cast.
/// </summary>
/// <remarks>
/// x2game client deserializer (sub_39924AC0) reads a single UInt16 field named "tl"
/// at packet-object offset 0xC into the timeline-id member.
/// </remarks>
public class SCSkillEndedPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    /// <summary>
    /// Timeline id of the skill that ended.
    /// </summary>
    public ushort TlId { get; }

    public SCSkillEndedPacket(ushort tlId) : base(SCOffsets.SCSkillEndedPacket, 5)
    {
        TlId = tlId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(TlId);
        return stream;
    }

    public override string Verbose()
    {
        return $" - {TlId}";
    }
}
