using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using System.Reflection;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

[NotInParallel]
public class ChangeSkillActiveTypeTests
{
    [Test]
    public async Task EffectChangesOnlyTheCastersSkillAndCanRestoreItsOtherAuthoredState()
    {
        var caster = new Character(null);
        var other = new Character(null);
        var action = new ChangeSkillActiveType();

        action.Execute(caster, null, other, null, null, null, null, DateTime.UtcNow, 31202, 1, 0, 0);
        await Assert.That(caster.SkillActiveTypes.TryGet(31202, out var active) && active == 1).IsTrue();
        await Assert.That(other.SkillActiveTypes.TryGet(31202, out _)).IsFalse();

        action.Execute(caster, null, caster, null, null, null, null, DateTime.UtcNow, 31202, 2, 0, 0);
        await Assert.That(caster.SkillActiveTypes.TryGet(31202, out active) && active == 2).IsTrue();
    }

    [Test]
    public async Task InvalidEffectValuesDoNotCreateEntries()
    {
        var caster = new Character(null);
        var action = new ChangeSkillActiveType();
        action.Execute(caster, null, caster, null, null, null, null, DateTime.UtcNow, -1, 1, 0, 0);
        action.Execute(caster, null, caster, null, null, null, null, DateTime.UtcNow, 31202, 256, 0, 0);
        await Assert.That(caster.SkillActiveTypes.Capture().Count).IsEqualTo(0);
    }

    [Test]
    public async Task UpdatePacketContainsTheSkillAndOneByteOfState()
    {
        var packet = new SCUpdateSkillActiveTypePacket(31202, 2);
        await Assert.That(packet.TypeId).IsEqualTo((ushort)0x034);
        var reader = new PacketStream(packet.Write(new PacketStream()).GetBytes());
        await Assert.That(reader.ReadUInt32()).IsEqualTo(31202u);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)2);
        await Assert.That(reader.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task LoginListContainsCountAndSkillStatePairs()
    {
        var entries = new[] { new KeyValuePair<uint, byte>(31202, 1), new KeyValuePair<uint, byte>(31203, 3) };
        var packet = new SCListSkillActiveTypePacket(entries);
        await Assert.That(packet.TypeId).IsEqualTo((ushort)0x1FC);
        var reader = new PacketStream(packet.Write(new PacketStream()).GetBytes());
        await Assert.That(reader.ReadUInt32()).IsEqualTo(2u);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(31202u);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)1);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(31203u);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)3);
        await Assert.That(reader.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task HiddenDefaultSkillBecomesUsableOnlyAfterItsActivationEffect()
    {
        var manager = new SkillManager(null, null);
        typeof(SkillManager).GetField("_defaultSkills", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, new Dictionary<uint, DefaultSkill>
            {
                [31202] = new() { SkillId = 31202, SkillActiveTypeId = 3 }
            });
        var singleton = typeof(Singleton<SkillManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = singleton.GetValue(null);
        singleton.SetValue(null, manager);
        try
        {
            var owner = new Character(null) { ObjId = 77 };
            var skill = new SkillTemplate { Id = 31202, AbilityId = AbilityType.General };
            var caster = new SkillCasterUnit(owner.ObjId);
            var authority = new SkillAuthority(new SkillAuthorityGameData());

            await Assert.That(authority.Check(owner, caster, null, skill, isDefaultSkill: true)).IsEqualTo(SkillResult.InvalidSkill);
            new ChangeSkillActiveType().Execute(owner, caster, owner, null, null, null, null, DateTime.UtcNow, 31202, 1, 0, 0);
            await Assert.That(authority.Check(owner, caster, null, skill, isDefaultSkill: true)).IsEqualTo(SkillResult.Success);
        }
        finally
        {
            singleton.SetValue(null, previous);
        }
    }

    [Test]
    public async Task AuthoredUnlockAlsoAuthorizesAGeneralSkillOutsideDefaultSkills()
    {
        var owner = new Character(null) { ObjId = 77 };
        var skill = new SkillTemplate { Id = 31202, AbilityId = AbilityType.General };
        var caster = new SkillCasterUnit(owner.ObjId);
        var authority = new SkillAuthority(new SkillAuthorityGameData());

        await Assert.That(authority.Check(owner, caster, null, skill)).IsEqualTo(SkillResult.InvalidSkill);
        new ChangeSkillActiveType().Execute(owner, caster, owner, null, null, null, null, DateTime.UtcNow, 31202, 1, 0, 0);
        await Assert.That(authority.Check(owner, caster, null, skill)).IsEqualTo(SkillResult.Success);
        new ChangeSkillActiveType().Execute(owner, caster, owner, null, null, null, null, DateTime.UtcNow, 31202, 2, 0, 0);
        await Assert.That(authority.Check(owner, caster, null, skill)).IsEqualTo(SkillResult.InvalidSkill);
    }
}
