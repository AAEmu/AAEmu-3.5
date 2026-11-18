using System.Numerics;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Services;

/// <summary>
/// Заготовка под порт <c>WorldIntegration.RelayNpcSpawnToZone</c> (ArcheageServer).
/// Подключить decode <c>WZNpcState</c> сюда и вызывать <see cref="NpcWorldFooting.ApplyWorldStreamPosition"/> после создания <see cref="Npc"/>.
/// </summary>
public static class WorldNpcRelay
{
    /// <summary>Вызывается, когда появится World-authored spawn (до или сразу после <c>NpcManager.Create</c>).</summary>
    public static void OnWorldAnnouncedPosition(Npc npc, Vector3 worldPosition, bool settleNow = true) =>
        NpcWorldFooting.ApplyWorldStreamPosition(npc, worldPosition, settleNow);
}