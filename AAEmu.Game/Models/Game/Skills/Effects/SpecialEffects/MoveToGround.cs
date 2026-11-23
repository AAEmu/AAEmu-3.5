using AAEmu.Game.Models.Game.Char;
using System.Numerics;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class MoveToGround : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.MoveToGround;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is not Unit { ParentWorld: { } world, DisabledSetPosition: false } unit ||
            unit is not (Character or Npc))
            return;

        var currentPosition = unit.Transform.World.Position;
        if (!TryGetLanding(world, currentPosition, target, out var landing) ||
            Vector3.DistanceSquared(currentPosition, landing) < 0.0001f)
            return;

        if (unit is Character { IsRiding: true } rider &&
            world.MateManager?.GetIsMounted(rider.ObjId, out var seat) is { } mount)
            world.MateManager.UnMountMate(rider, mount.TlId, seat, AttachUnitReason.MountMateForward);
        if (unit.Transform.Parent != null)
            return;
        if (unit.Transform.StickyParent != null)
            unit.Transform.StickyParent = null;

        var blink = new SCBlinkUnitPacket(unit.ObjId, 0f, 0f, landing.X, landing.Y, landing.Z);
        if (unit is Character character)
        {
            var rotation = character.Transform.Local.Rotation;
            character.SetPosition(landing.X, landing.Y, landing.Z, rotation.X, rotation.Y, rotation.Z);
            character.SendPacket(blink);
        }
        else if (unit is Npc npc)
        {
            npc.Transform.Local.SetPosition(landing.X, landing.Y, landing.Z);
            npc.BroadcastPacket(blink, false);
            npc.StopMovement();
            npc.CheckMovedPosition(currentPosition);
        }
    }

    private static bool TryGetLanding(WorldInstance world, Vector3 currentPosition, BaseUnit target, out Vector3 landing)
    {
        // Plot location targets carry the landing XY. Unit targets do not replace the caster's XY.
        var hasMarker = target is { ObjId: uint.MaxValue, Transform: not null };
        landing = hasMarker ? target.Transform.World.Position : currentPosition;
        if (!float.IsFinite(landing.X) || !float.IsFinite(landing.Y) || !float.IsFinite(landing.Z) ||
            (hasMarker && target.Transform.InstanceId != world.Id))
            return false;

        var terrain = world.Template;
        if (terrain.GeoData?.TryGroundNear(landing.X, landing.Y, landing.Z, out var height) == true)
        {
            landing.Z = height;
            return true;
        }

        if (!terrain.TryGetHeight(landing.X, landing.Y, out height))
            return hasMarker;

        if (world.Water is { } water && water.IsWater(new Vector3(landing.X, landing.Y, height + 0.1f), out _))
            height = Math.Max(height, water.GetWaterSurface(landing, out _));
        landing.Z = height;
        return true;
    }
}
