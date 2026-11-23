using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class RecoverExpEffect : EffectTemplate
{
    public bool NeedMoney { get; set; }
    public bool NeedLaborPower { get; set; }

    public bool NeedPriest { get; set; }
    public bool Penaltied { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (target is not Character character)
            return;

        if (NeedPriest && (!ReferenceEquals(caster, character) || !HasPriestInReach(character)))
        {
            character.SendErrorMessage(ErrorMessageType.PriestCannotCreateBuff);
            return;
        }

        if (character.TryRecoverExperience(NeedMoney, NeedLaborPower, Penaltied))
            return;

        // The stock scroll is a source reagent. A cast that restored nothing must keep it.
        if (casterObj is SkillItem && source?.Skill is { } skill)
        {
            skill.Cancelled = true;
            character.ConditionChance = false;
        }
    }

    private static bool HasPriestInReach(Character character)
    {
        if (character.ParentWorld == null)
            return false;

        foreach (var npc in WorldManager.GetAround<Npc>(character, 25f))
        {
            if (npc.Template?.Priest == true && !npc.IsDead)
                return true;
        }

        return false;
    }
}
