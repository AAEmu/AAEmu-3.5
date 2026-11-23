using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Models.Game.Char;

public partial class Character
{
    private readonly Lock _experienceLock = new();

    private void SetHeirExperience(uint experience)
    {
        HeirExp = experience;
        HeirLevel = ExperienceManager.Instance.GetHeirLevelFromExp(experience);
    }

    internal bool TryLevelUpHeir()
    {
        var manager = ExperienceManager.Instance;
        if (manager.GetHeirLevelUpRequirement(Level, HeirExp) == null)
            return false;

        // The periodic save visits item stacks before characters. Both sides of the upgrade belong to one save.
        lock (SaveManager.Instance.SaveLock)
        lock (_experienceLock)
        {
            var requirement = manager.GetHeirLevelUpRequirement(Level, HeirExp);
            if (requirement == null || requirement.ReqItemCount < 0)
                return false;

            if (requirement.ReqItemCount > 0)
            {
                var itemId = (uint)requirement.ReqItemId;
                if (requirement.ReqItemId <= 0 || Inventory?.Bag.TryConsumeExact(
                    ItemTaskType.SkillReagents, itemId, requirement.ReqItemCount) != true)
                    return false;
            }

            SetHeirExperience(requirement.ReqTotalExp);
            Expedition?.OnCharacterRefresh(this);
            BroadcastPacket(new SCHeirLevelUpPacket(ObjId), true);
            return true;
        }
    }
}
