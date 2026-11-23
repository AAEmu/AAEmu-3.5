using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

public class AggroResetTests
{
    [Test]
    public async Task ResetSelectedComponentsKeepsOtherThreat()
    {
        var target = new Unit { ObjId = 1 };
        var attacker = new Character(null) { ObjId = 2 };
        var aggro = new Aggro(attacker);
        aggro.AddAggro(AggroKind.Damage, 100);
        aggro.AddAggro(AggroKind.Heal, 100);
        aggro.AddAggro(AggroKind.Etc, 20);
        target.AggroTable.TryAdd(attacker.ObjId, aggro);
        attacker.IsInAggroListOf.Add(target.ObjId, target);

        new AggroReset().Execute(target, null, target, null, null, null, null, DateTime.UtcNow, 0, 1, 1, 0);

        await Assert.That(aggro.DamageAggro).IsEqualTo(100);
        await Assert.That(aggro.HealAggro).IsEqualTo(0);
        await Assert.That(aggro.TotalAggro).IsEqualTo(100);
        await Assert.That(attacker.IsInAggroListOf.ContainsKey(target.ObjId)).IsTrue();
    }

    [Test]
    public async Task ResetAllRemovesThreatAndReverseIndex()
    {
        var target = new Unit { ObjId = 1 };
        var attacker = new Character(null) { ObjId = 2 };
        var aggro = new Aggro(attacker);
        aggro.AddAggro(AggroKind.Damage, 100);
        target.AggroTable.TryAdd(attacker.ObjId, aggro);
        attacker.IsInAggroListOf.Add(target.ObjId, target);

        new AggroReset().Execute(target, null, target, null, null, null, null, DateTime.UtcNow, 0, 0, 0, 0);

        await Assert.That(target.AggroTable.IsEmpty).IsTrue();
        await Assert.That(attacker.IsInAggroListOf.ContainsKey(target.ObjId)).IsFalse();
    }

    [Test]
    public async Task NonzeroValueReplacesOnlyTheSelectedComponent()
    {
        var target = new Unit { ObjId = 1 };
        var attacker = new Unit { ObjId = 2 };
        var aggro = new Aggro(attacker);
        aggro.AddAggro(AggroKind.Damage, 100);
        aggro.AddAggro(AggroKind.Heal, 100);
        target.AggroTable.TryAdd(attacker.ObjId, aggro);

        new AggroReset().Execute(target, null, target, null, null, null, null, DateTime.UtcNow, 1, 0, 0, 7);

        await Assert.That(aggro.DamageAggro).IsEqualTo(7);
        await Assert.That(aggro.HealAggro).IsEqualTo(60);
        await Assert.That(aggro.TotalAggro).IsEqualTo(67);
    }
}
