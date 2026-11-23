using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.Char;

public partial class Character
{
    private readonly Lock _resurrectionLock = new();
    private (int Hp, int Mp)? _resurrectionOffer;
    private bool _resurrectionInProgress;

    internal void OfferResurrection(SkillCaster source, int hpPercent, int mpPercent)
    {
        lock (_resurrectionLock)
        {
            if (!IsDead || _resurrectionInProgress)
                return;

            _resurrectionOffer = (Math.Clamp(hpPercent, 0, 100), Math.Clamp(mpPercent, 0, 100));
            SendPacket(new SCNotifyResurrectionPacket(source));
        }
    }

    private void ClearResurrectionOffer()
    {
        lock (_resurrectionLock)
            _resurrectionOffer = null;
    }

    internal bool TryBeginResurrection(bool inPlace, out (int Hp, int Mp) recovery)
    {
        recovery = default;
        lock (_resurrectionLock)
        {
            if (!IsDead || _resurrectionInProgress)
                return false;

            recovery = inPlace ? _resurrectionOffer ?? (1, 1) : (10, 10);
            _resurrectionInProgress = true;
            return true;
        }
    }

    internal void EndResurrection(bool restoredVitals)
    {
        lock (_resurrectionLock)
        {
            if (restoredVitals)
                _resurrectionOffer = null;
            _resurrectionInProgress = false;
        }
    }
}
