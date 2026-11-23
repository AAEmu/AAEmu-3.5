using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class WorldMessageEffect : EffectTemplate
{
    private const int AllFactions = 0;
    private const int SameTopFaction = 1;

    public int FactionScopeId { get; set; }
    public string IconKey { get; set; }
    public bool KillHero { get; set; }
    public int KillStreakCount { get; set; }
    public string Message { get; set; }
    public bool ZoneGroupOnly { get; set; }
    public bool ZoneGroupWarState { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (caster?.Transform == null || string.IsNullOrWhiteSpace(Message) ||
            FactionScopeId is not (AllFactions or SameTopFaction))
            return;

        // These rows are selected by hero and kill-streak activity, which this branch does not track.
        if (KillHero || KillStreakCount > 0)
            return;

        var zones = ZoneGroupOnly || ZoneGroupWarState ? ZoneManager.Instance : null;
        if (!TryResolveAudienceZone(caster, zones, out var zoneGroup))
            return;

        // The client supplies the localized text and icon for this effect ID.
        var packet = new SCWorldMessagePacket(Id, caster.Transform.World.Position, (uint)(caster.Faction?.Id ?? 0),
            caster.Name, (uint)(target?.Faction?.Id ?? 0), target?.Name);
        if (!ZoneGroupOnly && FactionScopeId == AllFactions)
        {
            WorldManager.Instance.BroadcastPacketToServer(packet);
            return;
        }

        var characters = ZoneGroupOnly ? caster.ParentWorld?.GetAllCharacters() : WorldManager.Instance.GetAllCharacters();
        if (characters == null)
            return;

        foreach (var character in characters)
        {
            if (IncludesRecipient(caster, character, zoneGroup, zones))
                character.SendPacket(packet);
        }
    }

    internal bool TryResolveAudienceZone(BaseUnit caster, IZoneManager zones, out uint zoneGroup)
    {
        zoneGroup = 0;
        if (!ZoneGroupOnly && !ZoneGroupWarState)
            return true;

        zoneGroup = zones.GetZoneByKey(caster.Transform.ZoneId)?.GroupId ?? 0;
        if (zoneGroup == 0)
            return false;
        if (!ZoneGroupWarState)
            return true;

        foreach (var conflict in zones.GetConflicts())
        {
            if (conflict.ZoneGroupId == zoneGroup)
                return conflict.CurrentZoneState == ZoneConflictType.War;
        }

        return false;
    }

    internal bool IncludesRecipient(BaseUnit caster, Character recipient, uint zoneGroup, IZoneManager zones)
    {
        if (ZoneGroupOnly && zones.GetZoneByKey(recipient.Transform.ZoneId)?.GroupId != zoneGroup)
            return false;
        if (FactionScopeId == AllFactions)
            return true;

        var casterTopFaction = GetTopFaction(caster.Faction);
        return casterTopFaction != FactionsEnum.Invalid && casterTopFaction == GetTopFaction(recipient.Faction);
    }

    private static FactionsEnum GetTopFaction(SystemFaction faction) =>
        faction is { MotherId: not FactionsEnum.Invalid } ? faction.MotherId : faction?.Id ?? FactionsEnum.Invalid;
}
