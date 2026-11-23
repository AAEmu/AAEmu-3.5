using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

public class AggroCopyTests
{
    [Test]
    public async Task CopiesComponentsAndReplacesOldTargets()
    {
        var source = new Unit { ObjId = 1 };
        var destination = new Unit { ObjId = 2, Hp = 100 };
        var attacker = new Character(null) { ObjId = 3 };
        var former = new Character(null) { ObjId = 4 };
        var aggro = new Aggro(attacker);
        aggro.AddAggro(AggroKind.Damage, 100);
        aggro.AddAggro(AggroKind.Heal, 100);
        aggro.AddAggro(AggroKind.Etc, 20);
        source.AggroTable.TryAdd(attacker.ObjId, aggro);
        destination.AggroTable.TryAdd(former.ObjId, new Aggro(former));
        former.IsInAggroListOf.Add(destination.ObjId, destination);

        new AggroCopy().Execute(source, null, destination, null, null, null, null, DateTime.UtcNow, 0, 0, 0, 0);

        await Assert.That(destination.AggroTable.ContainsKey(former.ObjId)).IsFalse();
        await Assert.That(former.IsInAggroListOf.ContainsKey(destination.ObjId)).IsFalse();
        await Assert.That(destination.AggroTable[attacker.ObjId].DamageAggro).IsEqualTo(100);
        await Assert.That(destination.AggroTable[attacker.ObjId].HealAggro).IsEqualTo(60);
        await Assert.That(destination.AggroTable[attacker.ObjId].TotalAggro).IsEqualTo(180);
        await Assert.That(attacker.IsInAggroListOf.ContainsKey(destination.ObjId)).IsTrue();
    }

    [Test]
    public async Task CopyToSelfKeepsExistingAggro()
    {
        var unit = new Unit { ObjId = 1 };
        var attacker = new Unit { ObjId = 2 };
        var aggro = new Aggro(attacker);
        aggro.AddAggro(AggroKind.Damage, 100);
        unit.AggroTable.TryAdd(attacker.ObjId, aggro);

        new AggroCopy().Execute(unit, null, unit, null, null, null, null, DateTime.UtcNow, 0, 0, 0, 0);

        await Assert.That(unit.AggroTable[attacker.ObjId]).IsSameReferenceAs(aggro);
    }
}
