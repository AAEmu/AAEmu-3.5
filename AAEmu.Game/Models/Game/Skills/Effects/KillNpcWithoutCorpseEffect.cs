using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class KillNpcWithoutCorpseEffect : EffectTemplate
{
    public uint NpcId { get; set; }
    public float Radius { get; set; }
    public bool GiveExp { get; set; }
    public bool Vanish { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        var origin = target ?? caster;
        if (origin?.ParentWorld is not { SpawnManager: { } } world)
            return;

        TryRemove(target as Npc, world, caster, target);
        if (Vanish && !ReferenceEquals(caster, target))
            TryRemove(caster as Npc, world, caster, target);

        if (NpcId == 0 || Radius <= 0 || origin.Region == null)
            return;
        foreach (var unit in world.GetUnitsWithinRadius(origin, Radius))
            TryRemove(unit as Npc, world, caster, target);
    }

    private void TryRemove(Npc npc, WorldInstance world, BaseUnit caster, BaseUnit target)
    {
        if (npc == null || npc.IsDead)
            return;

        if (NpcId == 0 ? !Vanish || !ReferenceEquals(caster, npc) : npc.TemplateId != NpcId)
            return;

        if (!world.SpawnManager.TryQueueEffectDespawn(npc, scheduleRespawn: true) || !GiveExp)
            return;

        var character = caster?.GetOwnerCharacter() ?? npc.CurrentAggroTarget?.GetOwnerCharacter() ??
            target?.GetOwnerCharacter();
        if (character == null)
            return;
        var experience = npc.KillExp;
        if (experience > 0)
            character.AddExp(experience, true);
    }
}
