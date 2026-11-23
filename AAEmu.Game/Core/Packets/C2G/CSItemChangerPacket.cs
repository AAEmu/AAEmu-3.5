using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Core.Packets.C2G;

public sealed class CSItemChangerPacket() : GamePacket(CSOffsets.CSItemChangerPacket, 5)
{
    internal uint DoodadObjId { get; private set; }
    internal int SelectionIndex { get; private set; }

    public override void Read(PacketStream stream)
    {
        DoodadObjId = stream.ReadBc();
        SelectionIndex = stream.ReadInt32();
    }

    public override void Execute()
    {
        var player = Connection?.ActiveChar;
        var doodad = player?.ParentWorld?.GetDoodad(DoodadObjId);
        if (doodad == null)
            return;

        var result = doodad.ResolveItemChangeSkill(player, SelectionIndex, out var template);
        if (result != SkillResult.Success)
        {
            player.SendErrorMessage(DoodadItemChangeEffect.GetInteractionError(result));
            return;
        }

        var caster = new SkillCasterUnit(player.ObjId);
        var target = new SkillCastDoodadTarget { ObjId = DoodadObjId, Type = SkillCastTargetType.Doodad };
        var skillObject = new SkillObject();
        var skill = new Skill(template);
        result = skill.Use(player, caster, target, skillObject, false, out var errorValue);
        if (result != SkillResult.Success)
        {
            player.SendPacket(new SCSkillStartedPacket(template.Id, 0, caster, target, null, skillObject)
                .SetSkillResult(result).SetResultUInt(errorValue));
        }
    }
}
