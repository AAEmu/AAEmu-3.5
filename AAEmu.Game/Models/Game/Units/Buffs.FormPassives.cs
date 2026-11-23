using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.Units;

public partial class Buffs
{
    private void ApplyFormPassives(Buff form)
    {
        if (form.FormPassiveSource != null || form.Owner is not Character { Abilities: { } abilities } owner ||
            FormPassiveGameData.Instance.GetGrantedPassives(form.Template.Id) is not { } passiveIds)
            return;

        var skills = SkillManager.Instance;
        foreach (var passiveId in passiveIds)
        {
            var passive = skills.GetPassiveBuffTemplate(passiveId);
            if (passive == null || !CharacterAbilities.IsSpecialAbility(passive.AbilityId))
                continue;

            var level = abilities.GetLearnedSpecialAbilityLevel(passive.AbilityId);
            if (level == 0 || level < passive.Level || skills.GetBuffTemplate(passive.BuffId) is not { } template)
                continue;

            AddBuff(new Buff(owner, owner, new SkillCasterUnit(owner.ObjId), template, null, DateTime.UtcNow)
            {
                Passive = true,
                AbLevel = (ushort)level,
                FormPassiveId = passiveId,
                FormPassiveSource = form
            });
        }
    }

    // Invoked while holding the collection lock after detaching the ended form.
    private List<Buff> DetachFormPassives(Buff source)
    {
        if (source.GrantedFormPassives is not { } grants)
            return null;

        source.GrantedFormPassives = null;
        List<Buff> removed = null;
        foreach (var grant in grants)
        {
            if (grant.IsEnded())
                continue;

            Buff replacement = null;
            foreach (var other in _effects)
            {
                if (!other.IsEnded() && FormPassiveGameData.Instance.GetGrantedPassives(other.Template.Id)?.Contains(grant.FormPassiveId) == true)
                {
                    replacement = other;
                    break;
                }
            }

            if (replacement != null)
            {
                grant.FormPassiveSource = replacement;
                (replacement.GrantedFormPassives ??= []).Add(grant);
            }
            else if (grant.TryBeginDispel())
                (removed ??= []).Add(grant);
        }

        return removed;
    }
}
