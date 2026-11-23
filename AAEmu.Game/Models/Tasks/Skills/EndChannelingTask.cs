using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills;

public class EndChannelingTask(
    Skill skill,
    Unit caster,
    SkillCaster casterCaster,
    BaseUnit target,
    Doodad channelDoodad)
    : SkillTask(skill)
{
    private readonly Lock _stateLock = new();
    private ChannelingManaTask _manaTask;
    private int _remainingManaTicks = skill.Template.ChannelingTick > 0 && skill.Template.ChannelingMana > 0
        ? skill.Template.ChannelingTime / skill.Template.ChannelingTick
        : 0;
    private bool _finished;

    internal void Start()
    {
        lock (_stateLock)
        {
            caster.SkillTask = this;
            var tasks = TaskManager.Instance;
            if (_remainingManaTicks > 0)
            {
                var interval = TimeSpan.FromMilliseconds(Skill.Template.ChannelingTick);
                _manaTask = new ChannelingManaTask(this);
                tasks.Schedule(_manaTask, interval, interval, _remainingManaTicks);
            }

            tasks.Schedule(this, TimeSpan.FromMilliseconds(Skill.Template.ChannelingTime));
        }
    }

    public override void Execute() => Finish(interrupted: false);

    internal void Interrupt() => Finish(interrupted: true);

    private void Finish(bool interrupted)
    {
        lock (_stateLock)
        {
            if (_finished)
                return;

            _finished = true;
            var ownsSlot = caster.TryClearSkillTask(this);
            Skill.Cancelled |= interrupted || !ownsSlot || caster.IsDead;

            // TaskManager can dispatch the last mana tick and the end together. Settle any
            // remaining ticks here so their order cannot change the cost of a completed channel.
            if (!Skill.Cancelled)
            {
                while (_remainingManaTicks > 0)
                    ChargeManaTick();
            }
        }

        if (interrupted)
            Cancel();
        _manaTask?.Cancel();

        Skill.EndChanneling(caster, target, channelDoodad, casterCaster);
    }

    private void DrainMana()
    {
        lock (_stateLock)
        {
            if (_finished || _remainingManaTicks <= 0 || Skill.Cancelled || caster.IsDead ||
                !ReferenceEquals(caster.SkillTask, this))
                return;

            ChargeManaTick();
        }
    }

    private void ChargeManaTick()
    {
        _remainingManaTicks--;
        var cost = Math.Min(Skill.Template.ChannelingMana, caster.Mp);
        if (cost > 0)
            caster.ReduceCurrentMp(null, cost);
    }

    private sealed class ChannelingManaTask(EndChannelingTask channel) : Task
    {
        public override void Execute() => channel.DrainMana();
    }
}
