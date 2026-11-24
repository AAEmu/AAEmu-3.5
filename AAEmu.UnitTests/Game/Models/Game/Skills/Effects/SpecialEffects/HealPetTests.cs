using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

[NotInParallel]
public class HealPetTests
{
    private const uint RecoverySkillId = 15220;
    private static readonly DateTime s_now = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ClientPetDetailHasTwentyBytesAndAnAbsoluteDeadlineAtOffsetSix()
    {
        var pet = new SummonMate
        {
            TemplateId = 15000,
            Id = 33,
            Count = 1,
            DetailLevel = 31,
            MateDbId = 14,
            RepairStartTime = s_now.AddMinutes(5),
            CreateTime = s_now
        };
        var writer = new PacketStream();
        pet.Write(writer);
        var reader = new PacketStream(writer.GetBytes());
        reader.ReadBytes(18);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)ItemDetailType.Mate);
        reader.ReadBytes(6);
        await Assert.That(reader.ReadInt64()).IsEqualTo(new DateTimeOffset(pet.RepairStartTime).ToUnixTimeSeconds());
        reader.ReadBytes(6);
        await Assert.That(reader.ReadInt64()).IsEqualTo(new DateTimeOffset(s_now).ToUnixTimeSeconds());
    }

    [Test]
    public async Task RecoveryUpdateAndLegacyDatabaseStoreUseTheirOwnLayouts()
    {
        var deadline = s_now.AddMinutes(5);
        var pet = new SummonMate { Id = 19, DetailLevel = 28, MateDbId = 50, RepairStartTime = deadline };
        var updateWriter = new PacketStream();
        new ItemUpdate(pet).Write(updateWriter);
        var update = new PacketStream(updateWriter.GetBytes());
        await Assert.That(update.ReadByte()).IsEqualTo((byte)ItemAction.UpdateDetail);
        await Assert.That(update.ReadByte()).IsEqualTo((byte)ItemTaskLogType.UpdateOnly);
        update.ReadBytes(10); // slot type, slot index, item ID
        await Assert.That(update.ReadInt16()).IsEqualTo((short)128);
        await Assert.That(update.ReadByte()).IsEqualTo((byte)ItemDetailType.Mate);
        update.ReadBytes(6);
        await Assert.That(update.ReadInt64()).IsEqualTo(new DateTimeOffset(deadline).ToUnixTimeSeconds());

        var persisted = new PacketStream();
        pet.WriteDetails(persisted);
        var reloaded = new SummonMate();
        reloaded.ReadDetails(new PacketStream(persisted.GetBytes()));
        await Assert.That(persisted.Count).IsEqualTo(24);
        await Assert.That(reloaded.MateDbId).IsEqualTo(50u);
        await Assert.That(reloaded.RepairStartTime).IsEqualTo(deadline);
    }

    [Test]
    public async Task DeadMateIsDismissedAndItsFiveMinuteDeadlineSurvivesRepeatedDeathNotification()
    {
        var (owner, bag, mates) = MakeOwner();
        var item = AddMate(owner, bag, mates, 20, DateTime.MinValue);
        var world = owner.ParentWorld;
        var activeMate = AddActiveMate(owner, world, item.Id, hp: 0);

        owner.Mates.RecordDeathAndDespawn(activeMate, s_now);
        owner.Mates.RecordDeathAndDespawn(activeMate, s_now.AddSeconds(10));

        await Assert.That(item.RepairStartTime).IsEqualTo(s_now.AddMinutes(5));
        await Assert.That(item.IsDirty).IsTrue();
        await Assert.That(world.GetGameObject(activeMate.ObjId)).IsNull();
        await Assert.That(world.MateManager.GetActiveMates(owner.Id).Count).IsEqualTo(0);
        await Assert.That(mates[item.Id].UpdatedAt).IsEqualTo(s_now);
    }

    [Test]
    public async Task RelogCleansUpTheMatesWorldAfterTheCharacterHasChangedInstance()
    {
        var (owner, bag, mates) = MakeOwner();
        var oldWorld = owner.ParentWorld;
        var item = AddMate(owner, bag, mates, 20, DateTime.MinValue);
        var activeMate = AddActiveMate(owner, oldWorld, item.Id, hp: 17);
        var currentWorld = new WorldInstance(new WorldTemplate(), 0, true, owner.Transform.InstanceId);
        currentWorld.MateManager = new MateManager(currentWorld);
        owner.ParentWorld = currentWorld;

        oldWorld.MateManager.RemoveAndDespawnAllActiveOwnedMates(owner);

        await Assert.That(oldWorld.GetGameObject(activeMate.ObjId)).IsNull();
        await Assert.That(oldWorld.MateManager.GetActiveMates(owner.Id).Count).IsEqualTo(0);
        await Assert.That(item.RepairStartTime).IsEqualTo(DateTime.MinValue);
        await Assert.That(mates[item.Id].Hp).IsEqualTo(17);
    }

    [Test]
    public async Task PotionClearsOnlyTheSelectedStoredPetsRecoveryAndConsumesItsSourceStack()
    {
        var (owner, bag, mates) = MakeOwner();
        var potion = AddPotion(owner, bag, 1, 2);
        var selected = AddMate(owner, bag, mates, 10, s_now.AddMinutes(4));
        var other = AddMate(owner, bag, mates, 11, s_now.AddMinutes(2));

        Cast(owner, potion, selected.Id);
        Cast(owner, potion, selected.Id);

        await Assert.That(selected.RepairStartTime).IsEqualTo(DateTime.MinValue);
        await Assert.That(mates[selected.Id].Hp).IsEqualTo(int.MaxValue);
        await Assert.That(mates[selected.Id].Mp).IsEqualTo(int.MaxValue);
        await Assert.That(other.RepairStartTime).IsEqualTo(s_now.AddMinutes(2));
        await Assert.That(mates[other.Id].Hp).IsEqualTo(0);
        await Assert.That(potion.Count).IsEqualTo(1);
        await Assert.That(selected.IsDirty).IsTrue();
    }

    [Test]
    public async Task ExpiredOrForeignTargetDoesNotConsumePotion()
    {
        var (owner, bag, mates) = MakeOwner();
        var potion = AddPotion(owner, bag, 1, 2);
        var expired = AddMate(owner, bag, mates, 10, s_now);
        var foreign = AddMate(owner, bag, mates, 11, s_now.AddMinutes(5));
        foreign.OwnerId = 800;

        Cast(owner, potion, expired.Id);
        Cast(owner, potion, foreign.Id);

        await Assert.That(potion.Count).IsEqualTo(2);
        await Assert.That(expired.RepairStartTime).IsEqualTo(s_now);
        await Assert.That(foreign.RepairStartTime).IsEqualTo(s_now.AddMinutes(5));
    }

    [Test]
    public async Task NormalEffectRoutingPreservesTheNativeItemDescriptorAndConsumesOnlyOnce()
    {
        var (owner, bag, mates) = MakeOwner();
        var potion = AddPotion(owner, bag, 1, 2);
        var selected = AddMate(owner, bag, mates, 10, DateTime.UtcNow.AddMinutes(4));
        var source = ReadPotionSource(owner, potion);
        var target = new SkillCastItemTarget { Id = selected.Id, Type = SkillCastTargetType.Item };
        var template = new SkillTemplate { Id = RecoverySkillId, TargetType = SkillTargetType.Item, TargetSelection = SkillTargetSelection.Source };
        template.Effects.Add(new SkillEffect
        {
            ApplicationMethod = SkillEffectApplicationMethod.Target, EndLevel = 99,
            Chance = 100, Front = true, Back = true, ConsumeItemCount = 1,
            Template = new SpecialEffect { SpecialEffectTypeId = SpecialType.HealPet, Value1 = 20, Value2 = 20 }
        });
        var skillManager = new SkillManager(null, null);
        SetField(skillManager, "_skillReagents", new Dictionary<uint, SkillReagent>());
        SetField(skillManager, "_skillProducts", new Dictionary<uint, SkillProduct>());
        var singleton = typeof(Singleton<SkillManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = singleton.GetValue(null);
        singleton.SetValue(null, skillManager);
        try
        {
            owner.ConditionChance = true;
            new Skill(template).ApplyEffects(owner, source, owner, target, null);
        }
        finally
        {
            singleton.SetValue(null, original);
        }

        await Assert.That(selected.RepairStartTime).IsEqualTo(DateTime.MinValue);
        await Assert.That(potion.Count).IsEqualTo(1);
    }

    private static (Character Owner, ItemContainer Bag, Dictionary<ulong, MateDb> Mates) MakeOwner()
    {
        var owner = new Character(new UnitCustomModelParams()) { Id = 29, ObjId = 3 };
        var world = new WorldInstance(new WorldTemplate(), 0, true, owner.Transform.InstanceId);
        world.MateManager = new MateManager(world);
        owner.ParentWorld = world;
        owner.Mates = new CharacterMates(owner);
        owner.Inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        var bag = new ItemContainer(0, SlotType.Inventory, false, null);
        SetField(owner.Inventory, "<Bag>k__BackingField", bag);
        SetField(owner.Inventory, "<_itemContainers>k__BackingField", new Dictionary<SlotType, ItemContainer> { [SlotType.Inventory] = bag });
        var mates = (Dictionary<ulong, MateDb>)typeof(CharacterMates).GetField("_mates", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(owner.Mates)!;
        return (owner, bag, mates);
    }

    private static Item AddPotion(Character owner, ItemContainer bag, ulong id, int count)
    {
        var potion = new Item { Id = id, TemplateId = 18649, Count = count,
            Template = new ItemTemplate { Id = 18649, UseSkillId = RecoverySkillId, UseSkillAsReagent = true } };
        AddToBag(owner, bag, potion);
        return potion;
    }

    private static SummonMate AddMate(Character owner, ItemContainer bag, Dictionary<ulong, MateDb> mates, ulong id, DateTime recovery)
    {
        var item = new SummonMate { Id = id, TemplateId = 15000, Count = 1, RepairStartTime = recovery,
            Template = new SummonMateTemplate { Id = 15000, NpcId = 3300 } };
        AddToBag(owner, bag, item);
        mates.Add(id, new MateDb { ItemId = id, Owner = owner.Id });
        item.IsDirty = false;
        return item;
    }

    private static Mate AddActiveMate(Character owner, WorldInstance world, ulong itemId, int hp)
    {
        ObjectIdManager.Instance.Initialize();
        TlIdManager.Instance.Initialize();
        var mate = new Mate
        {
            ItemId = itemId, OwnerId = owner.Id, OwnerObjId = owner.ObjId, ParentWorld = world, TemplateId = 3300,
            ObjId = ObjectIdManager.Instance.GetNextId(), TlId = (ushort)TlIdManager.Instance.GetNextId(),
            Hp = hp, Mp = 20
        };
        var active = (ConcurrentDictionary<uint, List<Mate>>)typeof(MateManager)
            .GetField("_activeMates", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world.MateManager)!;
        active[owner.Id] = [mate];
        world.AddObject(mate);
        return mate;
    }

    private static void AddToBag(Character owner, ItemContainer bag, Item item)
    {
        item.OwnerId = owner.Id;
        item.HoldingContainer = bag;
        item.SlotType = SlotType.Inventory;
        item.Slot = bag.Items.Count;
        bag.Items.Add(item);
    }

    private static void Cast(Character owner, Item potion, ulong target)
    {
        var skill = new Skill(new SkillTemplate { Id = RecoverySkillId });
        var source = ReadPotionSource(owner, potion);
        var selection = new SkillCastItemTarget { Id = target, Type = SkillCastTargetType.Item };
        new HealPet().Execute(owner, source, owner, selection, null, skill, null, s_now, 20, 20, 0, 0);
    }

    private static SkillItem ReadPotionSource(Character owner, Item potion)
    {
        var sourceBytes = new PacketStream();
        sourceBytes.WriteBc(owner.ObjId).Write(potion.Id).Write(potion.TemplateId).Write((byte)0).Write(0u);
        var source = new SkillItem { Type = SkillCasterType.Item };
        source.Read(new PacketStream(sourceBytes.GetBytes()));
        return source;
    }

    private static void SetField<T>(T instance, string name, object value) =>
        typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
}
