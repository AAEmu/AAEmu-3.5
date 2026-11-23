using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public sealed class MemoryEmber : Item
{
    private const int BindingSize = sizeof(uint) + sizeof(long) + sizeof(float) * 4;
    private const int TerritoryBindingSize = BindingSize + sizeof(uint) * 4;
    private uint _houseId;
    private long _housePlaceTicks;
    private uint _territoryHouseTemplate;
    private uint _territoryOwner;
    private uint _territoryZone;
    private uint _territoryGroup;

    internal Vector3 ReturnPosition { get; private set; }
    internal float ReturnRotation { get; private set; }

    public MemoryEmber()
    {
    }

    public MemoryEmber(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
    }

    internal static bool IsTemplate(uint templateId) => templateId is 36917 or 53790 or TerritoryMemoryEmber.ItemId;

    internal static House GetFireplaceHouse(Doodad fireplace)
    {
        var manager = HousingManager.Instance;
        var house = fireplace.ParentObj as House ?? manager.GetHouseById(fireplace.OwnerDbId);
        return house is { Id: > 0, CurrentStep: -1, ParentWorld: not null } &&
               ReferenceEquals(house.ParentWorld, fireplace.ParentWorld) &&
               ReferenceEquals(manager.GetHouseById(house.Id), house) ? house : null;
    }

    internal void BindToHouse(House house, Vector3 position, float yaw)
    {
        if (TemplateId != TerritoryMemoryEmber.ItemId)
            Bind(house, position, yaw);
    }

    internal void BindToTerritory(House house, uint zone, uint group, Vector3 position, float yaw)
    {
        if (TemplateId != TerritoryMemoryEmber.ItemId || house is not { OwnerId: > 0 } || zone == 0 ||
            !TerritoryMemoryEmber.IsSourceHouse(house.TemplateId) || !TerritoryMemoryEmber.IsTerritory(group) ||
            !Bind(house, position, yaw))
            return;

        _territoryHouseTemplate = house.TemplateId;
        _territoryOwner = house.OwnerId;
        _territoryZone = zone;
        _territoryGroup = group;
    }

    private bool Bind(House house, Vector3 position, float yaw)
    {
        if (house is not { Id: > 0 } || house.PlaceDate == DateTime.MinValue || !IsFinite(position, yaw))
            return false;

        _houseId = house.Id;
        _housePlaceTicks = house.PlaceDate.Ticks;
        ReturnPosition = position;
        ReturnRotation = yaw;
        IsDirty = true;
        return true;
    }

    internal bool MatchesHouse(House house) => _houseId != 0 && house?.Id == _houseId &&
        Math.Abs(house.PlaceDate.Ticks - _housePlaceTicks) < TimeSpan.TicksPerSecond;

    internal bool MatchesTerritory(House house, uint zone, uint group) =>
        _territoryZone != 0 && zone == _territoryZone && group == _territoryGroup && MatchesHouse(house) &&
        house.TemplateId == _territoryHouseTemplate && house.OwnerId == _territoryOwner;

    internal static bool IsExclusiveHousePoint(House target, Vector3 position)
    {
        if (target.Region == null || !Contains(target, position))
            return false;

        foreach (var house in WorldManager.GetAround<House>(target))
        {
            if (!ReferenceEquals(house, target) && Contains(house, position))
                return false;
        }

        return true;

        static bool Contains(House house, Vector3 point)
        {
            if (house.Template is not { GardenRadius: > 0 } template)
                return false;

            var center = house.Transform.World.Position;
            return MathF.Abs(point.X - center.X) < template.GardenRadius && MathF.Abs(point.Y - center.Y) < template.GardenRadius;
        }
    }

    internal House GetDestinationHouse(Character character, DateTime now)
    {
        if (TemplateId == TerritoryMemoryEmber.ItemId || !IsUsable(character, now))
            return null;

        var house = HousingManager.Instance.GetHouseById(_houseId);
        return MatchesHouse(house) && house is { CurrentStep: -1, ParentWorld: not null } &&
               house.OwnerId == character.Id ? house : null;
    }

    internal House GetDestinationTerritory(Character character, DateTime now, out uint zone)
    {
        zone = _territoryZone;
        if (TemplateId != TerritoryMemoryEmber.ItemId || zone == 0 || !IsUsable(character, now))
            return null;

        var house = HousingManager.Instance.GetHouseById(_houseId);
        var territory = ZoneManager.Instance.GetZoneByKey(zone)?.GroupId ?? 0;
        return MatchesTerritory(house, zone, territory) && TerritoryMemoryEmber.CanUseHouse(character, house) &&
            (ZoneManager.Instance.GetZoneByKey(house.Transform.ZoneId)?.GroupId ?? 0) == territory ? house : null;
    }

    private bool IsUsable(Character character, DateTime now) =>
        character.Inventory?.Bag is { } bag && ReferenceEquals(HoldingContainer, bag) &&
        OwnerId == character.Id && Count > 0 && _houseId != 0 &&
        (ExpirationTime == DateTime.MinValue || ExpirationTime > now);

    internal void ReadBinding(PacketStream stream)
    {
        var isTerritory = TemplateId == TerritoryMemoryEmber.ItemId;
        if (stream.LeftBytes != (isTerritory ? TerritoryBindingSize : BindingSize))
            return;

        var houseId = stream.ReadUInt32();
        var ticks = stream.ReadInt64();
        var position = new Vector3(stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle());
        var yaw = stream.ReadSingle();
        if (houseId == 0 || ticks <= 0 || ticks > DateTime.MaxValue.Ticks || !IsFinite(position, yaw))
            return;

        if (isTerritory)
        {
            var houseTemplate = stream.ReadUInt32();
            var owner = stream.ReadUInt32();
            var zone = stream.ReadUInt32();
            var group = stream.ReadUInt32();
            if (owner == 0 || zone == 0 || !TerritoryMemoryEmber.IsSourceHouse(houseTemplate) || !TerritoryMemoryEmber.IsTerritory(group))
                return;

            _territoryHouseTemplate = houseTemplate;
            _territoryOwner = owner;
            _territoryZone = zone;
            _territoryGroup = group;
        }

        _houseId = houseId;
        _housePlaceTicks = ticks;
        ReturnPosition = position;
        ReturnRotation = yaw;
    }

    internal void WriteBinding(PacketStream stream)
    {
        if (_houseId == 0)
            return;

        stream.Write(_houseId);
        stream.Write(_housePlaceTicks);
        stream.Write(ReturnPosition.X);
        stream.Write(ReturnPosition.Y);
        stream.Write(ReturnPosition.Z);
        stream.Write(ReturnRotation);
        if (TemplateId == TerritoryMemoryEmber.ItemId)
        {
            stream.Write(_territoryHouseTemplate);
            stream.Write(_territoryOwner);
            stream.Write(_territoryZone);
            stream.Write(_territoryGroup);
        }
    }

    private static bool IsFinite(Vector3 position, float yaw) => float.IsFinite(position.X) &&
        float.IsFinite(position.Y) && float.IsFinite(position.Z) && float.IsFinite(yaw);
}
