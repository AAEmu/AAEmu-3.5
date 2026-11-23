using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ChangeTargetTests
{
    [Test]
    public async Task EffectMakesCasterTheTargetsSelectedUnitAndBroadcastsIt()
    {
        var caster = new TestUnit { ObjId = 100 };
        var target = new TestUnit { ObjId = 200, CurrentTarget = new TestUnit { ObjId = 300 } };
        var effect = new SpecialEffect { SpecialEffectTypeId = SpecialType.ChangeTarget };

        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), target, new SkillCastUnitTarget(target.ObjId),
            new CastSkill(1, 1), new EffectSource(), null, DateTime.UtcNow);

        await Assert.That(target.CurrentTarget).IsSameReferenceAs(caster);
        await Assert.That(target.LastPacket).IsTypeOf<SCTargetChangedPacket>();
        var bytes = target.LastPacket.Write(new PacketStream()).GetBytes();
        var reader = new PacketStream(bytes);
        await Assert.That(reader.ReadBc()).IsEqualTo(target.ObjId);
        await Assert.That(reader.ReadBc()).IsEqualTo(caster.ObjId);
    }

    private sealed class TestUnit : Unit
    {
        public GamePacket LastPacket { get; private set; }

        public override void BroadcastPacket(GamePacket packet, bool self) => LastPacket = packet;
    }
}
