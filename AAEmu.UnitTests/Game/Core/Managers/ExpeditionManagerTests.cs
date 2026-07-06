using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ExpeditionManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockExpId = Mock.Of<IExpeditionIdManager>();
        var mockTeam = Mock.Of<ITeamManager>();
        var mockWorld = Mock.Of<IWorldManager>();
        var mockChat = Mock.Of<IChatManager>();
        var manager = new ExpeditionManager(mockExpId.Object, mockTeam.Object, mockWorld.Object, mockChat.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockExpId);
        Mock.VerifyNoOtherCalls(mockTeam);
        Mock.VerifyNoOtherCalls(mockWorld);
        Mock.VerifyNoOtherCalls(mockChat);
    }

    [Test]
    public async Task Expedition_MapsExpeditionsColumns()
    {
        var type = typeof(Expedition);
        await Assert.That(type.GetProperty("Id")).IsNotNull();
        await Assert.That(type.GetProperty("MotherId")).IsNotNull();
        await Assert.That(type.GetProperty("Name")).IsNotNull();
        await Assert.That(type.GetProperty("OwnerId")).IsNotNull();
        await Assert.That(type.GetProperty("OwnerName")).IsNotNull();
        await Assert.That(type.GetProperty("Created")).IsNotNull();
        await Assert.That(type.GetProperty("Members")).IsNotNull();
        await Assert.That(type.GetProperty("Policies")).IsNotNull();
    }

    [Test]
    public async Task ExpeditionMember_MapsExpeditionMembersColumns()
    {
        var type = typeof(ExpeditionMember);
        await Assert.That(type.GetProperty("CharacterId")).IsNotNull();
        await Assert.That(type.GetProperty("ExpeditionId")).IsNotNull();
        await Assert.That(type.GetProperty("Name")).IsNotNull();
        await Assert.That(type.GetProperty("Level")).IsNotNull();
        await Assert.That(type.GetProperty("Role")).IsNotNull();
        await Assert.That(type.GetProperty("LastWorldLeaveTime")).IsNotNull();
        await Assert.That(type.GetProperty("Abilities")).IsNotNull();
        await Assert.That(type.GetProperty("Memo")).IsNotNull();
    }

    [Test]
    public async Task ExpeditionRolePolicy_MapsExpeditionRolePoliciesColumns()
    {
        var type = typeof(ExpeditionRolePolicy);
        await Assert.That(type.GetProperty("ExpeditionId")).IsNotNull();
        await Assert.That(type.GetProperty("Role")).IsNotNull();
        await Assert.That(type.GetProperty("Name")).IsNotNull();
        await Assert.That(type.GetProperty("DominionDeclare")).IsNotNull();
        await Assert.That(type.GetProperty("Invite")).IsNotNull();
        await Assert.That(type.GetProperty("Expel")).IsNotNull();
        await Assert.That(type.GetProperty("Promote")).IsNotNull();
        await Assert.That(type.GetProperty("Dismiss")).IsNotNull();
        await Assert.That(type.GetProperty("Chat")).IsNotNull();
        await Assert.That(type.GetProperty("ManagerChat")).IsNotNull();
        await Assert.That(type.GetProperty("SiegeMaster")).IsNotNull();
        await Assert.That(type.GetProperty("JoinSiege")).IsNotNull();
    }
}
