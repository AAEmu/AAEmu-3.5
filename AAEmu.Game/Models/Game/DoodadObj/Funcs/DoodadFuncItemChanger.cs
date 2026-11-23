using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public sealed class DoodadFuncItemChanger : DoodadPhaseFuncTemplate
{
    public int NextPhase { get; init; }
    public uint ItemId { get; init; }
    public int ItemCount { get; init; }
    public uint SkillId { get; init; }

    // A phase lists choices. The matching skill selects and applies one choice.
    public override bool Use(BaseUnit caster, Doodad owner) => false;
}
