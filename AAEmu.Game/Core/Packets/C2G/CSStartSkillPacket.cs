using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Physics.Debug;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStartSkillPacket() : GamePacket(CSOffsets.CSStartSkillPacket, 5)
{
    private static readonly SkillAuthority s_authority = new(SkillAuthorityGameData.Instance);

    public override void Read(PacketStream stream)
    {
        var player = Connection.ActiveChar;
        if (player == null)
            return;

        // Preserve the existing pacing of incoming skill requests.
        Thread.Sleep(100);
        if (!ReferenceEquals(Connection.ActiveChar, player))
            return;

        var skillId = stream.ReadUInt32();

        var skillCasterType = stream.ReadByte();
        var skillCaster = SkillCaster.GetByType((SkillCasterType)skillCasterType);
        skillCaster.Read(stream);

        var skillCastTargetType = stream.ReadByte();
        var skillCastTarget = SkillCastTarget.GetByType((SkillCastTargetType)skillCastTargetType);
        skillCastTarget.Read(stream);

        var flag = stream.ReadByte();
        var skillObject = SkillObject.ReadFrom(stream, flag);
        var flagType = (int)skillObject.Flag;

        HarpoonMechanicsDebug.LogCsStartSkillIfHarpoon(skillId, flag, flagType, skillCaster, skillCastTarget, skillObject);

        player.LastPacketActivityTime = DateTime.UtcNow;
        Logger.Info("StartSkill: Id {0}, flag {1}, caster={2}, target={3}", skillId, flag, skillCaster.ObjId, skillCastTarget.ObjId);

        var skillManager = SkillManager.Instance;
        var template = skillManager.GetSkillTemplate(skillId);
        var isDefaultSkill = skillManager.IsDefaultSkill(skillId);
        var mount = skillCaster is SkillCasterMount ? player.ParentWorld?.GetBaseUnit(skillCaster.ObjId) : null;
        var result = s_authority.Check(player, skillCaster, skillCastTarget, template, mount, isDefaultSkill,
            out var mountSkillId, out var mountSeat);
        if (result != SkillResult.Success)
        {
            Refuse(player, skillId, skillCaster, skillCastTarget, skillObject, result);
            return;
        }

        if (skillCaster is SkillCasterUnit && player.IsAutoAttack && skillId == player.AutoAttackTask?.Skill?.Template?.Id)
            return;

        if (skillCaster is SkillItem)
        {
            foreach (var effect in template.Effects)
            {
                if (effect.Template is MoveToLocationEffect recall && recall.CheckDestination(player, skillCaster, skillObject) is { } error)
                {
                    player.SendErrorMessage(error);
                    Refuse(player, skillId, skillCaster, skillCastTarget, skillObject, SkillResult.InvalidLocation);
                    return;
                }
            }
        }

        var owner = skillCaster is SkillCasterUnit && !isDefaultSkill &&
                    (player.Skills?.IsLearned(skillId) == true || SkillAuthorityGameData.Instance.TryGetHeirSkill(skillId, out _))
            ? player
            : null;
        var skill = new Skill(template, owner);
        result = skill.Use(mount ?? player, skillCaster, skillCastTarget, skillObject, false, out var errorValue);
        if (result != SkillResult.Success)
        {
            Refuse(player, skillId, skillCaster, skillCastTarget, skillObject, result, errorValue);
            return;
        }

        if (mount is Slave slave)
        {
            if (skillId == HarpoonMechanicsDebug.ShipLaunchHarpoonSkillId)
                ShipHarpoonRopeController.OnLaunchSucceeded(slave, skillCastTarget, player);
            else if (skillId == HarpoonMechanicsDebug.ShipCutHarpoonRopeSkillId)
                ShipHarpoonRopeController.OnCutRope(slave, player);
        }

        if (mountSkillId != 0)
        {
            var riderSkill = MateGameData.Instance.GetAttachedSkillByMountSkillId(mountSkillId, mountSeat);
            if (riderSkill == 0)
                return;

            result = player.UseSkill(riderSkill, player.CurrentTarget as Unit ?? player);
            if (result != SkillResult.Success)
                Refuse(player, skillId, skillCaster, skillCastTarget, skillObject, result);
        }
        else if (skillCaster is SkillCasterUnit && skillId is 2 or 3 or 4)
        {
            player.IsAutoAttack = true;
            player.StartAutoSkill(skill);
        }
    }

    private static void Refuse(Character player, uint skillId, SkillCaster caster, SkillCastTarget target,
        SkillObject skillObject, SkillResult result, uint errorValue = 0)
    {
        player.SendPacket(new SCSkillStartedPacket(skillId, 0, caster, target, null, skillObject)
            .SetSkillResult(result)
            .SetResultUInt(errorValue));
    }
}
