using System.Globalization;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

[NotInParallel]
public class DeathExperienceTests
{
    private readonly List<(FieldInfo Field, object Before)> _singletons = [];

    [Before(Test)]
    public void Setup()
    {
        var experience = new ExperienceManager();
        var levels = Mock.Of<IExperienceLevelTemplateLoader>();
        levels.Load().Returns(new[]
        {
            new ExperienceLevelTemplate { Level = 1, TotalExp = 0 },
            new ExperienceLevelTemplate { Level = 2, TotalExp = 100 },
            new ExperienceLevelTemplate { Level = 3, TotalExp = 10_000 }
        });
        experience.Load(levels.Object, 3, 3);
        Install(experience);

        var formulas = new FormulaManager();
        typeof(FormulaManager).GetProperty(nameof(FormulaManager.CalculationEngine))!.SetValue(formulas,
            new Jace.CalculationEngine(new Jace.JaceOptions { CultureInfo = CultureInfo.InvariantCulture }));
        Install(formulas);
        typeof(FormulaManager).GetField("_formulas", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(formulas,
            new Dictionary<uint, Formula>
            {
                [(uint)FormulaKind.PenaltyExp] = new("experience / 20"),
                [(uint)FormulaKind.RecoverableExp] = new("experience * 4 / 100")
            });
    }

    [After(Test)]
    public void Cleanup()
    {
        foreach (var (field, before) in _singletons)
            field.SetValue(null, before);
    }

    [Test]
    public async Task PveDeathRecordsBothPoolsAndPrayerThenScrollCannotRestoreTheSameXpTwice()
    {
        var character = AtExperience(1100);
        character.ApplyDeathExperiencePenalty(new Npc(), KillReason.Damage);

        await Assert.That(character.Experience).IsEqualTo(1050);
        await Assert.That(character.RecoverableExp).IsEqualTo(40);
        await Assert.That(character.PenaltiedExp).IsEqualTo(50);

        await Assert.That(character.TryRecoverExperience(false, false, fullPenalty: false)).IsTrue();
        await Assert.That(character.RecoverableExp).IsEqualTo(0);
        await Assert.That(character.PenaltiedExp).IsEqualTo(10);
        await Assert.That(character.Experience).IsEqualTo(1090);

        await Assert.That(character.TryRecoverExperience(false, false, fullPenalty: true)).IsTrue();
        await Assert.That(character.TryRecoverExperience(false, false, fullPenalty: true)).IsFalse();
        await Assert.That(character.Experience).IsEqualTo(1100);
    }

    [Test]
    public async Task FullRecoveryHonorsLegacyRowsAndClearsThePrizeForPrayer()
    {
        var character = AtExperience(1100);
        character.RecoverableExp = 35;

        await Assert.That(character.TryRecoverExperience(false, false, fullPenalty: true)).IsTrue();
        await Assert.That(character.TryRecoverExperience(false, false, fullPenalty: false)).IsFalse();
        await Assert.That(character.Experience).IsEqualTo(1135);
    }

    [Test]
    public async Task PvpAndGmKillsSkipPenaltyAndADeathOnTheLevelFloorCannotOverdraw()
    {
        var character = AtExperience(1100);
        character.ApplyDeathExperiencePenalty(new Character(null), KillReason.Damage);
        character.ApplyDeathExperiencePenalty(new Npc(), KillReason.Gm);
        await Assert.That(character.Experience).IsEqualTo(1100);
        await Assert.That(character.PenaltiedExp).IsEqualTo(0);

        var atFloor = AtExperience(100);
        atFloor.ApplyDeathExperiencePenalty(new Npc(), KillReason.Damage);
        await Assert.That(atFloor.Experience).IsEqualTo(100);
        await Assert.That(atFloor.RecoverableExp).IsEqualTo(0);
    }

    [Test]
    public async Task AuthoredDeathModifiersApplyToTheirOwnPools()
    {
        var character = AtExperience(1100);
        character.AddBonus(1, new Bonus
        {
            Template = new BonusTemplate { Attribute = UnitAttribute.PenaltyExpMul, ModifierType = UnitModifierType.Value },
            Value = -20
        });
        character.AddBonus(2, new Bonus
        {
            Template = new BonusTemplate { Attribute = UnitAttribute.RecoverableExpMul, ModifierType = UnitModifierType.Value },
            Value = -50
        });

        character.ApplyDeathExperiencePenalty(new Npc(), KillReason.Damage);

        await Assert.That(character.Experience).IsEqualTo(1060);
        await Assert.That(character.PenaltiedExp).IsEqualTo(40);
        await Assert.That(character.RecoverableExp).IsEqualTo(20);
    }

    [Test]
    public async Task RecoveryPacketHasTwoAbsolutePoolsAndTheReasonAfterTheObjectId()
    {
        var written = new SCRecoverableExpPacket(0x123456, 20, 40, 2).Write(new PacketStream());
        var reader = new PacketStream(written.GetBytes());

        await Assert.That(reader.ReadBc()).IsEqualTo(0x123456u);
        await Assert.That(reader.ReadInt32()).IsEqualTo(20);
        await Assert.That(reader.ReadInt32()).IsEqualTo(40);
        await Assert.That(reader.ReadInt32()).IsEqualTo(2);
        await Assert.That(reader.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task EarlierNonlethalHitCannotRunDeathAgainAfterAnotherHitLands()
    {
        var unit = new DeathObserver { Hp = 0 };
        unit.PostUpdateCurrentHp(null, 100, 20);
        unit.PostUpdateCurrentHp(null, 20, 0);

        await Assert.That(unit.Deaths).IsEqualTo(1);
    }

    private sealed class DeathObserver : Unit
    {
        public int Deaths { get; private set; }
        public override void DoDie(BaseUnit killer, KillReason killReason) => Deaths++;
    }

    private static Character AtExperience(int amount)
    {
        var character = new Character(null) { Level = 2 };
        typeof(Character).GetProperty(nameof(Character.Experience))!.SetValue(character, amount);
        return character;
    }

    private void Install<T>(T service) where T : class
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _singletons.Add((field, field.GetValue(null)));
        field.SetValue(null, service);
    }
}
