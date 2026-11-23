using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSFireCastingSkillPacket() : GamePacket(CSOffsets.CSFireCastingSkillPacket, 5)
{
    public override void Read(PacketStream stream)
    {
        var unitId = stream.ReadBc();
        var skillTlId = stream.ReadUInt16();
        var plotTlId = stream.ReadUInt16();

        var character = Connection?.ActiveChar;
        if (character == null || character.ObjId != unitId)
            return;

        var now = Environment.TickCount64;
        var state = character.ActivePlotState;
        if (state != null && ReferenceEquals(state.Caster, character) && state.TryRequestCastingUse(plotTlId, now))
            return;

        if (character.SkillTask is CastTask task)
            task.TryFireEarly(character, skillTlId, now);
    }
}
