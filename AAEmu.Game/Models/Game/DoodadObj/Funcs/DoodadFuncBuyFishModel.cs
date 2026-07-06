using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncBuyFishModel : DoodadPhaseFuncTemplate
{
    // doodad_phase_funcs
    // TODO: no DB column, stale
    public string Name { get; set; }
    public string Model { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace("DoodadFuncBuyFishModel");
        return false;
    }
}
