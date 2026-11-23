using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class BuffPersistenceTests
{
    private static readonly DateTime SavedAt = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task RestoresSelfCastUsingNewOwnerAndObjectId()
    {
        var oldOwner = CreateCharacter(10, 100);
        var newOwner = CreateCharacter(10, 900);
        var template = CreateTemplate();
        template.RemoveOnSourceDead = true;
        var row = Capture(oldOwner, oldOwner, template);

        var buff = row.CreateBuff(newOwner, template, null, _ => throw new InvalidOperationException(), SavedAt);

        await Assert.That(buff.Caster).IsSameReferenceAs(newOwner);
        await Assert.That(buff.SkillCaster.ObjId).IsEqualTo(900u);
    }

    [Test]
    public async Task RestoresTypedCharacterSourceAcrossItsOwnRelog()
    {
        var owner = CreateCharacter(10, 100);
        var priorCaster = CreateCharacter(20, 200);
        var currentCaster = CreateCharacter(20, 800);
        var template = CreateTemplate();
        var row = Capture(owner, priorCaster, template);

        var buff = row.CreateBuff(owner, template, null, id => id == 20 ? currentCaster : null, SavedAt);

        await Assert.That(buff.Caster).IsSameReferenceAs(currentCaster);
        await Assert.That(buff.SkillCaster.ObjId).IsEqualTo(800u);
    }

    [Test]
    public async Task LegacyCasterIdCollidingWithOwnerDoesNotBecomeSelfCast()
    {
        var owner = CreateCharacter(10, 100);
        var template = CreateTemplate();
        var row = Capture(owner, owner, template) with { SourceKind = PersistedBuffSourceKind.Legacy };

        var buff = row.CreateBuff(owner, template, null, _ => throw new InvalidOperationException(), SavedAt);

        await Assert.That(buff.Caster).IsNull();
        await Assert.That(buff.SkillCaster.ObjId).IsEqualTo(0u);
    }

    [Test]
    public async Task RuntimeNpcIdCannotResolveToACharacterWithTheSameId()
    {
        var owner = CreateCharacter(10, 100);
        var npc = new Unit { Id = 20, ObjId = 200, Hp = 100 };
        var template = CreateTemplate();
        var row = Capture(owner, npc, template);

        var buff = row.CreateBuff(owner, template, null, _ => throw new InvalidOperationException(), SavedAt);

        await Assert.That(buff.Caster).IsNull();
    }

    [Test]
    public async Task SourceDeathBuffRestoresWhileTheOriginalCasterLifeIsStillActive()
    {
        var owner = CreateCharacter(10, 100);
        var caster = CreateCharacter(20, 200);
        var template = CreateTemplate();
        template.RemoveOnSourceDead = true;
        var row = Capture(owner, caster, template);

        var buff = row.CreateBuff(owner, template, null, _ => caster, SavedAt);

        await Assert.That(buff.Caster).IsSameReferenceAs(caster);
    }

    [Test]
    public async Task SourceDeathBuffDoesNotSurviveCasterDeathAndResurrectionWhileOwnerIsAway()
    {
        var owner = CreateCharacter(10, 100);
        var caster = CreateCharacter(20, 200);
        var template = CreateTemplate();
        template.RemoveOnSourceDead = true;
        var row = Capture(owner, caster, template);
        caster.Events.RaiseDeath(caster, new OnDeathArgs { Victim = caster });

        var buff = row.CreateBuff(owner, template, null, _ => caster, SavedAt);

        await Assert.That(buff).IsNull();
    }

    [Test]
    public async Task UnavailableOrdinaryCasterKeepsItsIdentityForTheNextRelog()
    {
        var owner = CreateCharacter(10, 100);
        var caster = CreateCharacter(20, 200);
        var template = CreateTemplate();
        var row = Capture(owner, caster, template);
        var buff = row.CreateBuff(owner, template, null, _ => null, SavedAt);
        buff.State = EffectState.Acting;

        var nextRow = PersistedBuff.Capture(buff, owner.Id, SavedAt)!.Value;
        var currentCaster = CreateCharacter(20, 800);
        var restoredAgain = nextRow.CreateBuff(owner, template, null, id => id == 20 ? currentCaster : null, SavedAt);

        await Assert.That(buff.Caster).IsNull();
        await Assert.That(restoredAgain.Caster).IsSameReferenceAs(currentCaster);
    }

    [Test]
    public async Task RestoredBuffReleasesItsCasterWhenThatSessionEnds()
    {
        var owner = CreateCharacter(10, 100);
        var caster = CreateCharacter(20, 200);
        var template = CreateTemplate();
        var row = Capture(owner, caster, template);
        var buff = row.CreateBuff(owner, template, null, _ => caster, SavedAt);
        buff.PrepareRestoredSource();
        buff.State = EffectState.Acting;

        caster.Buffs.CancelAllEffectTasks();

        await Assert.That(buff.Caster).IsNull();
        await Assert.That(buff.SkillCaster.ObjId).IsEqualTo(0u);
        await Assert.That(buff.State).IsEqualTo(EffectState.Acting);
    }

    [Test]
    public async Task RestoresZeroChargeStacksSkillAndOriginalNormalSaveEligibility()
    {
        var owner = CreateCharacter(10, 100);
        var skill = new SkillTemplate { Id = 42 };
        var template = CreateTemplate();
        var row = Capture(owner, owner, template) with { SkillId = 42, Charge = 0, Stack = 4 };

        var restoredAt = SavedAt.AddSeconds(60);
        var buff = row.CreateBuff(owner, template, skill, _ => null, restoredAt);
        buff.State = EffectState.Acting;
        buff.InitializeCharge();
        var savedAgain = PersistedBuff.Capture(buff, owner.Id, restoredAt);

        await Assert.That(buff.Skill.Template.Id).IsEqualTo(42u);
        await Assert.That(buff.Charge).IsEqualTo(0);
        await Assert.That(buff.Stack).IsEqualTo(4);
        await Assert.That(buff.Duration).IsEqualTo(20000);
        await Assert.That(savedAgain!.Value.Duration).IsEqualTo(80000);
    }

    [Test]
    public async Task LongOfflineIntervalExpiresRealTimeBuff()
    {
        var owner = CreateCharacter(10, 100);
        var template = CreateTemplate();
        var row = Capture(owner, owner, template);

        var buff = row.CreateBuff(owner, template, null, _ => null, SavedAt.AddDays(365));

        await Assert.That(buff).IsNull();
    }

    private static Character CreateCharacter(uint id, uint objId) => new(null) { Id = id, ObjId = objId, Hp = 100 };

    private static BuffTemplate CreateTemplate() => new()
    {
        Id = 101,
        SaveRuleId = (uint)BuffSaveRuleType.Normal,
        Duration = 80000,
        RealTime = true,
        InitMinCharge = 3,
        InitMaxCharge = 3
    };

    private static PersistedBuff Capture(Character owner, Unit caster, BuffTemplate template)
    {
        var original = new Buff(owner, caster, new SkillCasterUnit(caster.ObjId), template, null, SavedAt)
        {
            State = EffectState.Acting,
            Duration = 80000
        };

        return PersistedBuff.Capture(original, owner.Id, SavedAt)!.Value;
    }
}
