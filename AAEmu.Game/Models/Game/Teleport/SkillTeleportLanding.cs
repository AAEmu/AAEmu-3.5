using System.Numerics;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.Teleport;

internal static class SkillTeleportLanding
{
    internal static bool CanLand(Character character, WorldInstance world, uint zoneId, Vector3 position, float yaw) =>
        character.ParentWorld != null && world?.Template != null && zoneId != 0 && float.IsFinite(position.X) && float.IsFinite(position.Y) &&
        float.IsFinite(position.Z) && float.IsFinite(yaw) &&
        ZoneManager.Instance.GetZoneByKey(zoneId) is { } zone && (!zone.Closed || character.AccessLevel >= 100) &&
        WorldManager.Instance.GetZoneId(world.Template, position.X, position.Y) == zoneId;

    internal static void Apply(Character character, WorldInstance world, uint zoneId, Vector3 position, float yaw, TeleportReason reason)
    {
        character.ForceDismount();
        character.DisabledSetPosition = true;
        character.SendPacket(new SCLoadInstancePacket(world.Template.Id, zoneId, position.X, position.Y, position.Z, 0f, 0f, yaw));
        character.Transform = new Transform(character, null, zoneId, world.Id, position.X, position.Y, position.Z, yaw);
        character.SendPacket(new SCTeleportUnitPacket(reason, 0, position.X, position.Y, position.Z, yaw));
    }
}
