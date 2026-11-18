using System.Globalization;
using System.Text.RegularExpressions;

namespace AAEmu.Game.Models.Game.AI;

/// <summary>Ключи <c>roamingMinDist</c>/<c>roamingMaxDist</c> из <c>npc_ai_params.ai_param</c> (Zones <c>NpcAiParams</c>).</summary>
public static partial class NpcRoamingParamsParser
{
    [GeneratedRegex(@"(?m)^\s*(roamingMinDist|roamingMaxDist)\s*=\s*([-+]?\d+(?:\.\d+)?)", RegexOptions.Compiled)]
    private static partial Regex RoamingDistanceRegex();

    public static (float? Min, float? Max) Parse(string? lua)
    {
        if (string.IsNullOrWhiteSpace(lua))
            return (null, null);

        float? min = null;
        float? max = null;
        foreach (Match m in RoamingDistanceRegex().Matches(lua))
        {
            var value = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (m.Groups[1].Value == "roamingMinDist")
                min = value;
            else
                max = value;
        }

        return (min, max);
    }
}