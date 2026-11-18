using System.Numerics;

using AAEmu.Game.Models.Game.AI.v2.Controls;

namespace AAEmu.Game.Core.Managers;

public interface IAiPathsManager : ILoadable
{
    List<AiPathPoint> LoadAiPathPoints(string aiPathFileName);

    void EnsureDesignerZoneLoaded(string worldName, uint zoneKey);

    /// <summary>Waypoints from <c>editor/npc.xml</c> laid at a zone-local spawn (continent = origin + local).</summary>
    bool TryLoadDesignerPathPoints(string worldName, uint zoneKey, string pathName, Vector3 spawnLocal, int pointNo,
        out List<AiPathPoint> points, out float speed, out bool loops, out uint resolvedZoneKey,
        IReadOnlyCollection<uint>? searchZones = null);
}
