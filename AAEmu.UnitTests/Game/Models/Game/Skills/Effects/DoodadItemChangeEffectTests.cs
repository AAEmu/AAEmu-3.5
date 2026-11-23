using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.Proxy;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

[NotInParallel]
public class DoodadItemChangeEffectTests
{
    private const string ChangerType = nameof(DoodadFuncItemChanger);
    private readonly Dictionary<uint, List<DoodadFunc>> _funcs = [];
    private readonly Dictionary<uint, List<DoodadPhaseFunc>> _phases = [];
    private readonly Dictionary<uint, DoodadPhaseFuncTemplate> _changers = [];
    private readonly Dictionary<uint, SkillTemplate> _skills = [];
    private readonly List<(FieldInfo Field, object Previous)> _singletons = [];
    private Character _player;
    private Doodad _doodad;
    private ItemContainer _bag;

    [Before(Test)]
    public void Setup()
    {
        var manager = new DoodadManager(null, null, null, null, null);
        Install(manager);
        SetField(manager, "_funcsByGroups", _funcs);
        SetField(manager, "_phaseFuncs", _phases);
        SetField(manager, "_phaseFuncTemplates",
            new Dictionary<string, Dictionary<uint, DoodadPhaseFuncTemplate>> { [ChangerType] = _changers });
        var skills = new SkillManager(null, null);
        Install(skills);
        SetField(skills, "_skills", _skills);
        var models = new ModelManager();
        SetField(models, "_modelTypes", new Dictionary<uint, ModelType>());
        Install(models);

        _player = new TestCharacter { Id = 2, ObjId = 20 };
        var world = new WorldInstance(new WorldTemplate(), 0, true, _player.Transform.InstanceId);
        _player.ParentWorld = world;
        _bag = new ItemContainer(0, SlotType.Inventory, false, null);
        _player.Inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        SetField(_player.Inventory, "<Bag>k__BackingField", _bag);
        _doodad = new TestDoodad { ObjId = 0x03020A, ParentWorld = world, IsVisible = true, OwnerType = DoodadOwnerType.System };
        world.AddObject(_doodad);
    }

    [After(Test)]
    public void TearDown()
    {
        foreach (var (field, previous) in _singletons)
            field.SetValue(null, previous);
    }

    [Test]
    public async Task PlantingPacketHasLevelFiveAndDoesNotReplaceLevelTwoSetGameType()
    {
        var network = (GameNetwork)Activator.CreateInstance(typeof(GameNetwork), nonPublic: true)!;
        var handler = GetField<GameNetwork>("_handler").GetValue(network);
        var registrations = (ConcurrentDictionary<byte, ConcurrentDictionary<uint, Type>>)
            GetField<GameProtocolHandler>("_packets").GetValue(handler)!;

        await Assert.That(registrations[5][0x00F]).IsEqualTo(typeof(CSItemChangerPacket));
        await Assert.That(registrations[2][0x00F]).IsEqualTo(typeof(SetGameTypePacket));
    }

    [Test]
    public async Task NativePlantingRequestDecodesTheDoodadAndOptionAndRejectsAnotherOwnersPlot()
    {
        AddOption(24993, 1, 15659, 50, 29236, 24994, permission: 1);
        AddOption(24993, 2, 15664, 50, 29237, 25890, permission: 1);
        _doodad.OwnerType = DoodadOwnerType.Character;
        _doodad.OwnerId = 17;
        _doodad.FuncGroupId = 24993;
        var seed = Give(1, 15664, 51);
        _skills[29236] = CreateSkill(29236, idx: 0);
        _skills[29237] = CreateSkill(29237, idx: 1);
        var stream = new PacketStream([0x0A, 0x02, 0x03, 0x01, 0x00, 0x00, 0x00]);
        var packet = new CSItemChangerPacket { Connection = new GameConnection(null) { ActiveChar = _player } };

        packet.Decode(stream);

        await Assert.That(packet.DoodadObjId).IsEqualTo(0x03020Au);
        await Assert.That(packet.SelectionIndex).IsEqualTo(1);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
        await Assert.That(_doodad.FuncGroupId).IsEqualTo(24993u);
        await Assert.That(seed.Count).IsEqualTo(51);
        _doodad.OwnerId = _player.Id;
        var result = _doodad.ResolveItemChangeSkill(_player, packet.SelectionIndex, out var skill);
        await Assert.That(result).IsEqualTo(SkillResult.Success);
        await Assert.That(skill.Id).IsEqualTo(29237u);
    }

    [Test]
    public async Task NormalSkillAuthorityOffersTheMatchingOptionFromTheCurrentPhase()
    {
        AddOption(24993, 1, 15659, 50, 29236, 24994);
        _doodad.FuncGroupId = 24993;
        var skill = CreateSkill(29236, idx: 0);
        var authority = new SkillAuthority(new SkillAuthorityGameData());
        var target = new SkillCastDoodadTarget { ObjId = _doodad.ObjId };

        var result = authority.Check(_player, new SkillCasterUnit(_player.ObjId), target, skill);

        await Assert.That(result).IsEqualTo(SkillResult.Success);
        _doodad.FuncGroupId = 24994;
        result = authority.Check(_player, new SkillCasterUnit(_player.ObjId), target, skill);
        await Assert.That(result).IsEqualTo(SkillResult.InvalidSkill);
    }

    [Test]
    public async Task SelectingTheSecondStockSeedConsumesOnlyItsItemAndChangesPhaseOnce()
    {
        AddOption(24993, 1, 15659, 50, 29236, 24994);
        AddOption(24993, 2, 15664, 50, 29237, 25890);
        _doodad.FuncGroupId = 24993;
        var potato = Give(1, 15659, 51);
        var cucumber = Give(2, 15664, 51);

        Cast(CreateSkill(29237, idx: 1));
        Cast(CreateSkill(29237, idx: 1));

        await Assert.That(_doodad.FuncGroupId).IsEqualTo(25890u);
        await Assert.That(cucumber.Count).IsEqualTo(1);
        await Assert.That(potato.Count).IsEqualTo(51);
    }

    [Test]
    public async Task InsufficientMaterialsLeaveThePlotAndTheBagUntouched()
    {
        AddOption(24993, 1, 15659, 50, 29236, 24994);
        _doodad.FuncGroupId = 24993;
        var seed = Give(1, 15659, 49);

        Cast(CreateSkill(29236, idx: 0));

        await Assert.That(_doodad.FuncGroupId).IsEqualTo(24993u);
        await Assert.That(seed.Count).IsEqualTo(49);
    }

    [Test]
    public async Task ShopStockRowsWithSharedIndexSelectByTheirIndividualSkill()
    {
        AddOption(30870, 130, 40112, 1, 32849, 30920);
        AddOption(30870, 131, 40112, 1, 32850, 30921);
        _doodad.FuncGroupId = 30870;
        var ticket = Give(1, 40112, 2);

        Cast(CreateSkill(32850, idx: 0));

        await Assert.That(_doodad.FuncGroupId).IsEqualTo(30921u);
        await Assert.That(ticket.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ASelectionFromAForeignOwnerOrOutsideCastRangeLeavesThePlotUntouched()
    {
        AddOption(24993, 1, 15659, 50, 29236, 24994, permission: 1);
        _doodad.OwnerId = 17;
        _doodad.OwnerType = DoodadOwnerType.Character;
        _doodad.FuncGroupId = 24993;
        var seed = Give(1, 15659, 51);
        var skill = CreateSkill(29236, idx: 0);

        Cast(skill);
        await Assert.That(_doodad.FuncGroupId).IsEqualTo(24993u);
        _doodad.OwnerId = _player.Id;
        _doodad.Transform.Local.SetPosition(10, 0, 0);
        Cast(skill);
        await Assert.That(_doodad.FuncGroupId).IsEqualTo(24993u);
        await Assert.That(seed.Count).IsEqualTo(51);
    }

    private void AddOption(uint group, uint option, uint item, int count, uint skill, int nextPhase, uint permission = 0)
    {
        if (!_phases.TryGetValue(group, out var options))
        {
            options = [];
            _phases.Add(group, options);
            _funcs[group] = [new DoodadFunc { FuncType = "DoodadFuncItemChangerUiOpen", GroupId = group, PermId = permission }];
        }

        options.Add(new DoodadPhaseFunc { GroupId = group, FuncId = option, FuncType = ChangerType });
        _changers.Add(option, new DoodadFuncItemChanger
        {
            Id = option, ItemId = item, ItemCount = count, SkillId = skill, NextPhase = nextPhase
        });
    }

    private static SkillTemplate CreateSkill(uint id, int idx) => new()
    {
        Id = id, MaxRange = 4, TargetType = SkillTargetType.Doodad,
        Effects = [new SkillEffect { Template = new DoodadItemChangeEffect { Idx = idx } }]
    };

    private void Cast(SkillTemplate template) => template.Effects[0].Template.Apply(
        _player, new SkillCasterUnit(_player.ObjId), _doodad, new SkillCastDoodadTarget { ObjId = _doodad.ObjId },
        null, new EffectSource(new Skill(template)), null, DateTime.UtcNow);

    private Item Give(uint id, uint template, int count)
    {
        var item = InventoryTestUtils.MockItem(id, template);
        item.Count = count;
        item.HoldingContainer = _bag;
        item.SlotType = SlotType.Inventory;
        _bag.Items.Add(item);
        return item;
    }

    private static FieldInfo GetField<T>(string name) =>
        typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static void SetField<T>(T instance, string name, object value) => GetField<T>(name).SetValue(instance, value);

    private void Install<T>(T instance) where T : class
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _singletons.Add((field, field.GetValue(null)));
        field.SetValue(null, instance);
    }

    private sealed class TestCharacter() : Character(new UnitCustomModelParams())
    {
    }

    private sealed class TestDoodad : Doodad
    {
        public override void BroadcastPacket(GamePacket packet, bool self)
        {
        }
    }
}
