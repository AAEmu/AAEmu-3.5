using System.Numerics;

using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crime;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;

namespace AAEmu.Game.Models.Game.DoodadObj;

public partial class Doodad
{
    // The evidence pickup skill (11672) has a four-meter reach.
    private const float CrimeReportRange = 4f;
    private bool _crimeReportClaimed;

    internal readonly record struct CrimeReportEvidence(uint Phase, int NextPhase, uint Criminal, uint Victim,
        uint ItemTemplate, CrimeKind Kind, short Points, DateTime PlantedAt, uint ZoneId, Vector3 Position);

    internal CrimeReportEvidence? TryClaimCrimeReport(Character reporter, IDoodadManager doodadManager)
    {
        lock (_phaseLock)
        {
            if (_crimeReportClaimed || _deleted || !IsVisible || Despawn > DateTime.MinValue ||
                OwnerType != DoodadOwnerType.Character || OwnerId == 0 || OwnerId == reporter.Id ||
                ParentWorld is not { } world || !ReferenceEquals(reporter.ParentWorld, world) ||
                !ReferenceEquals(world.GetDoodad(ObjId), this) ||
                reporter.Transform.InstanceId != Transform.InstanceId)
                return null;

            if (!(reporter.GetDistanceTo(this, includeZAxis: true) <= CrimeReportRange))
                return null;

            foreach (var currentFunc in CurrentFuncs)
            {
                if (currentFunc.FuncType != nameof(DoodadFuncEvidenceItemLoot) ||
                    currentFunc.NextPhase <= 0 || currentFunc.NextPhase == FuncGroupId ||
                    doodadManager.GetFuncTemplate(currentFunc.FuncId, currentFunc.FuncType) is not DoodadFuncEvidenceItemLoot loot)
                    continue;

                _crimeReportClaimed = true;
                return new CrimeReportEvidence(FuncGroupId, currentFunc.NextPhase, OwnerId, (uint)Data,
                    ItemTemplateId, (CrimeKind)loot.CrimeKindId, loot.CrimeValue, PlantTime, Transform.ZoneId, Transform.World.Position);
            }
        }

        return null;
    }

    internal void ReleaseCrimeReportClaim()
    {
        lock (_phaseLock)
            _crimeReportClaimed = false;
    }

    internal void AdvanceReportedEvidence(Character reporter, CrimeReportEvidence evidence)
    {
        lock (_phaseLock)
        {
            if (_deleted || FuncGroupId != evidence.Phase)
                return;

            ListGroupId.Clear();
            ToNextPhase = false;
            DoChangePhase(reporter, evidence.NextPhase);
        }
    }
}
