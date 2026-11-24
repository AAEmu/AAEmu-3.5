using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Core.Packets;

public class ClientOpcodeTests
{
    [Test]
    [Arguments(nameof(CSOffsets.off_39CFB1AC), 0x0A6)]
    [Arguments(nameof(CSOffsets.CSRequestSysInstanceIndexPacket), 0x03E)]
    [Arguments(nameof(CSOffsets.CSLeaveTeamPacket), 0x15D)]
    [Arguments(nameof(CSOffsets.CSKickTeamMemberPacket), 0x0F5)]
    [Arguments(nameof(CSOffsets.CSDismissTeamPacket), 0x0ED)]
    [Arguments(nameof(CSOffsets.CSTeamAcceptOwnerOfferPacket), 0x072)]
    [Arguments(nameof(CSOffsets.CSCreateHousePacket), 0x04F)]
    [Arguments(nameof(CSOffsets.CSDecorateHousePacket), 0x0B0)]
    [Arguments(nameof(CSOffsets.CSRemoveAllFieldSlavesPacket), 0x101)]
    [Arguments(nameof(CSOffsets.CSLeaveTrialAudiencePacket), 0x0FA)]
    [Arguments(nameof(CSOffsets.CSInstanceLoadedPacket), 0x195)]
    [Arguments(nameof(CSOffsets.CSResetSkillsPacket), 0x1B3)]
    [Arguments(nameof(CSOffsets.CSSetForceAttackPacket), 0x1B9)]
    [Arguments(nameof(CSOffsets.CSRepairSingleEquipmentPacket), 0x146)]
    [Arguments(nameof(CSOffsets.CSRepairAllEquipmentsPacket), 0x175)]
    [Arguments(nameof(CSOffsets.CSRequestCommonFarmList), 0x14B)]
    [Arguments(nameof(CSOffsets.CSRequestPermissionToPlayCinemaForDirectingMode), 0x1A7)]
    [Arguments(nameof(CSOffsets.CSTakeAttachmentSequentially), 0x01B)]
    [Arguments(nameof(CSOffsets.CSExecuteCraft), 0x088)]
    [Arguments(nameof(CSOffsets.CSBeautyshopDataPacket), 0x0AE)]
    [Arguments(nameof(CSOffsets.CSChangeExpeditionMemberRolePacket), 0x141)]
    [Arguments(nameof(CSOffsets.CSChangeExpeditionOwnerPacket), 0x147)]
    [Arguments(nameof(CSOffsets.CSChangeExpeditionRolePolicyPacket), 0x1AA)]
    [Arguments(nameof(CSOffsets.CSCreateExpeditionPacket), 0x0B5)]
    [Arguments(nameof(CSOffsets.CSDismissExpeditionPacket), 0x00C)]
    [Arguments(nameof(CSOffsets.CSInviteToExpeditionPacket), 0x0AC)]
    [Arguments(nameof(CSOffsets.CSKickFromExpeditionPacket), 0x023)]
    [Arguments(nameof(CSOffsets.CSLeaveExpeditionPacket), 0x11E)]
    [Arguments(nameof(CSOffsets.CSRenameExpeditionPacket), 0x0CA)]
    [Arguments(nameof(CSOffsets.CSReplyExpeditionInvitationPacket), 0x02A)]
    [Arguments(nameof(CSOffsets.CSBidAuctionPacket), 0x06E)]
    [Arguments(nameof(CSOffsets.CSCancelAuctionPacket), 0x10C)]
    [Arguments(nameof(CSOffsets.CSExitBeautySalonPacket), 0x166)]
    [Arguments(nameof(CSOffsets.CSUpdateDominionTaxRatePacket), 0x08E)]
    [Arguments(nameof(CSOffsets.CSTakedownTradeItemPacket), 0x0CB)]
    [Arguments(nameof(CSOffsets.CSRequestCharBriefPacket), 0x11A)]
    [Arguments(nameof(CSOffsets.CSAllowHousingRecoverPacket), 0x01D)]
    [Arguments(nameof(CSOffsets.CSJuryVerdictPacket), 0x0D3)]
    [Arguments(nameof(CSOffsets.CSThisTimeUnpackItemPacket), 0x0E4)]
    public async Task CSOffsets_MatchClient(string packet, int opcode)
    {
        var field = typeof(CSOffsets).GetField(packet);
        await Assert.That(field?.GetRawConstantValue()).IsEqualTo((ushort)opcode);
    }

    [Test]
    [Arguments(nameof(SCOffsets.SCTeamRemoteMembersExPacket), 0x0C0)]
    [Arguments(nameof(SCOffsets.SCGmDumpExploredRegionPacket), 0x02C)]
    [Arguments(nameof(SCOffsets.SCExploredRegionsPacket), 0x1C3)]
    [Arguments(nameof(SCOffsets.SCGamePointChangedPacket), 0x24F)]
    [Arguments(nameof(SCOffsets.SCDominionTaxRatePacket), 0x1E9)]
    [Arguments(nameof(SCOffsets.SCNationalTaxRatePacket), 0x220)]
    [Arguments(nameof(SCOffsets.SCTeamBecameRaidTeamPacket), 0x2D9)]
    [Arguments(nameof(SCOffsets.SCChangeJuryOKCountPacket), 0x153)]
    [Arguments(nameof(SCOffsets.SCAggroTargetChanged), 0x1D9)]
    [Arguments(nameof(SCOffsets.SCSuspectGoingBotTrial), 0x00E)]
    [Arguments(nameof(SCOffsets.SCToggleBeautyshopResponsePacket), 0x02D)]
    [Arguments(nameof(SCOffsets.SCCvFCombatRelationshipPacket), 0x099)]
    [Arguments(nameof(SCOffsets.SCBuffLearnedPacket), 0x19B)]
    [Arguments(nameof(SCOffsets.SCDoodadCompleteQuestPacket), 0x11C)]
    [Arguments(nameof(SCOffsets.SCDoodadSoundPacket), 0x172)]
    [Arguments(nameof(SCOffsets.SCDoodadPhaseMsgPacket), 0x073)]
    [Arguments(nameof(SCOffsets.SCAppellationChangedPacket), 0x0A0)]
    [Arguments(nameof(SCOffsets.SCRefreshInCharacterListPacket), 0x003)]
    [Arguments(nameof(SCOffsets.SCOtherTradeItemPutupPacket), 0x069)]
    [Arguments(nameof(SCOffsets.SCOtherTradeItemTookdownPacket), 0x0DB)]
    [Arguments(nameof(SCOffsets.SCFactionSetRelationStatePacket), 0x143)]
    [Arguments(nameof(SCOffsets.SCAttachmentTakenPacket), 0x015)]
    [Arguments(nameof(SCOffsets.SCChangeJuryVerdictCountPacket), 0x231)]
    [Arguments(nameof(SCOffsets.SCTrialCancledPacket), 0x06D)]
    [Arguments(nameof(SCOffsets.SCExpeditionRoleChangedPacket), 0x177)]
    [Arguments(nameof(SCOffsets.SCResultRestrictCheckPacket), 0x15D)]
    [Arguments(nameof(SCOffsets.SCTeamMemberRoleChangedPacket), 0x173)]
    [Arguments(nameof(SCOffsets.SCLootDicePacket), 0x28E)]
    [Arguments(nameof(SCOffsets.SCSlaveStatePacket), 0x188)]
    [Arguments(nameof(SCOffsets.SCUserNoteLoadedPacket), 0x28B)]
    [Arguments(nameof(SCOffsets.SCDoodadQuestAcceptPacket), 0x268)]
    [Arguments(nameof(SCOffsets.SCLpManagedPacket), 0x1E8)]
    [Arguments(nameof(SCOffsets.SCMateStatePacket), 0x10B)]
    [Arguments(nameof(SCOffsets.SCCharBriefPacket), 0x168)]
    [Arguments(nameof(SCOffsets.SCPlaytimePacket), 0x1D4)]
    [Arguments(nameof(SCOffsets.SCTradeOkUpdatePacket), 0x13D)]
    [Arguments(nameof(SCOffsets.SCItemUccDataChangedPacket), 0x12C)]
    [Arguments(nameof(SCOffsets.SCCofferContentsUpdatePacket), 0x122)]
    [Arguments(nameof(SCOffsets.SCHousingRecoverTogglePacket), 0x090)]
    [Arguments(nameof(SCOffsets.SCUnitOfflinePacket), 0x07E)]
    [Arguments(nameof(SCOffsets.SCInvenExpandedPacket), 0x051)]
    [Arguments(nameof(SCOffsets.SCLootBagDataPacket), 0x1DD)]
    [Arguments(nameof(SCOffsets.SCGradeEnchantBroadcastPacket), 0x1B5)]
    [Arguments(nameof(SCOffsets.SCPortalInfoSavedPacket), 0x297)]
    [Arguments(nameof(SCOffsets.SCItemSocketingLunastoneResultPacket), 0x121)]
    public async Task SCOffsets_MatchClient(string packet, int opcode)
    {
        var field = typeof(SCOffsets).GetField(packet);
        await Assert.That(field?.GetRawConstantValue()).IsEqualTo((ushort)opcode);
    }

    [Test]
    [Arguments(5, 0x146, typeof(CSRepairSingleEquipmentPacket))]
    [Arguments(5, 0x175, typeof(CSRepairAllEquipmentsPacket))]
    [Arguments(5, 0x1B9, typeof(CSSetForceAttackPacket))]
    [Arguments(5, 0x04F, typeof(CSCreateHousePacket))]
    [Arguments(5, 0x0B0, typeof(CSDecorateHousePacket))]
    [Arguments(5, 0x0FA, typeof(CSLeaveTrialAudiencePacket))]
    [Arguments(5, 0x15D, typeof(CSLeaveTeamPacket))]
    [Arguments(5, 0x0F5, typeof(CSKickTeamMemberPacket))]
    [Arguments(5, 0x0ED, typeof(CSDismissTeamPacket))]
    [Arguments(5, 0x1B3, typeof(CSResetSkillsPacket))]
    [Arguments(1, 0x01B, typeof(CSTakeAttachmentSequentially))]
    [Arguments(1, 0x1A7, typeof(CSRequestPermissionToPlayCinemaForDirectingMode))]
    [Arguments(5, 0x195, typeof(CSInstanceLoadedPacket))]
    [Arguments(1, 0x088, typeof(CSExecuteCraft))]
    [Arguments(5, 0x14B, typeof(CSRequestCommonFarmList))]
    [Arguments(5, 0x0AE, typeof(CSBeautyshopDataPacket))]
    [Arguments(5, 0x0B5, typeof(CSCreateExpeditionPacket))]
    [Arguments(5, 0x1AA, typeof(CSChangeExpeditionRolePolicyPacket))]
    [Arguments(5, 0x141, typeof(CSChangeExpeditionMemberRolePacket))]
    [Arguments(5, 0x147, typeof(CSChangeExpeditionOwnerPacket))]
    [Arguments(5, 0x0CA, typeof(CSRenameExpeditionPacket))]
    [Arguments(5, 0x00C, typeof(CSDismissExpeditionPacket))]
    [Arguments(5, 0x0AC, typeof(CSInviteToExpeditionPacket))]
    [Arguments(5, 0x02A, typeof(CSReplyExpeditionInvitationPacket))]
    [Arguments(5, 0x11E, typeof(CSLeaveExpeditionPacket))]
    [Arguments(5, 0x023, typeof(CSKickFromExpeditionPacket))]
    [Arguments(5, 0x08E, typeof(CSUpdateDominionTaxRatePacket))]
    [Arguments(5, 0x06E, typeof(CSBidAuctionPacket))]
    [Arguments(5, 0x10C, typeof(CSCancelAuctionPacket))]
    [Arguments(5, 0x0CB, typeof(CSTakedownTradeItemPacket))]
    [Arguments(5, 0x166, typeof(CSExitBeautySalonPacket))]
    [Arguments(5, 0x11A, typeof(CSRequestCharBriefPacket))]
    [Arguments(5, 0x01D, typeof(CSAllowHousingRecoverPacket))]
    [Arguments(5, 0x0D3, typeof(CSJuryVerdictPacket))]
    [Arguments(5, 0x0E4, typeof(CSThisTimeUnpackItemPacket))]
    public async Task ClientOpcode_DispatchesToExpectedHandler(int level, int opcode, Type packet)
    {
        GetLevelHandlers((byte)level).TryGetValue((uint)opcode, out var actual);

        await Assert.That(actual).IsEqualTo(packet);
        var instance = (GamePacket)Activator.CreateInstance(actual);
        await Assert.That(instance.TypeId).IsEqualTo((ushort)opcode);
        await Assert.That(instance.Level).IsEqualTo((byte)level);
    }

    [Test]
    [Arguments(1)]
    [Arguments(5)]
    public async Task UnassignedOpcode_HasNoHandler(int level)
    {
        await Assert.That(GetLevelHandlers((byte)level).ContainsKey(0xFFF)).IsFalse();
    }

    private static ConcurrentDictionary<uint, Type> GetLevelHandlers(byte level)
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var handler = typeof(GameNetwork).GetField("_handler", Fields)?.GetValue(GameNetwork.Instance);
        var packets = (ConcurrentDictionary<byte, ConcurrentDictionary<uint, Type>>)typeof(GameProtocolHandler)
            .GetField("_packets", Fields)?.GetValue(handler);
        return packets[level];
    }

    [Test]
    public async Task DoodadQuestAccept_SendsObjectIdAndQuestContextToClient()
    {
        var packet = new SCDoodadQuestAcceptPacket(20737, 250);
        var stream = packet.Write(new PacketStream());
        stream.Rollback();

        await Assert.That(packet.TypeId).IsEqualTo((ushort)0x268);
        await Assert.That(stream.ReadBc()).IsEqualTo(20737u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(250u);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task RestrictCheck_SendsCharacterIdCodeAndResultToClient()
    {
        var packet = new SCResultRestrictCheckPacket(42, 5, 0);
        var stream = packet.Write(new PacketStream());
        stream.Rollback();

        await Assert.That(packet.TypeId).IsEqualTo((ushort)0x15D);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(42u);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)5);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task GradeEnchantResult_SendsEmptyBreakRewardWhenNoneWasGranted()
    {
        var item = new ItemMock(42);
        var itemSize = item.Write(new PacketStream()).Count;
        var packet = new SCGradeEnchantResultPacket(0, item, 3, 3);
        var stream = packet.Write(new PacketStream());
        stream.Rollback();

        await Assert.That(packet.TypeId).IsEqualTo((ushort)0x1CD);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        _ = stream.ReadBytes(itemSize);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)3);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)3);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.ReadBoolean()).IsFalse();
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    [Arguments(0, true)]
    [Arguments(1, true)]
    [Arguments(1, false)]
    public async Task SocketingResult_SendsOperationAndSuccessSeparately(int result, bool install)
    {
        var packet = new SCItemSocketingLunagemResultPacket((byte)result, 42, 17, install);
        var stream = packet.Write(new PacketStream());
        stream.Rollback();

        await Assert.That(packet.TypeId).IsEqualTo((ushort)0x09E);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)result);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(42ul);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(17u);
        await Assert.That(stream.ReadByte()).IsEqualTo(install ? (byte)1 : (byte)0);
        await Assert.That(stream.ReadBoolean()).IsEqualTo(result == 1);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task TrialAudience_SendsThreeByteObjectIds()
    {
        const uint AudienceId = 0xFEDCBA;
        var joined = new SCTrialAudienceJoinedPacket(12, AudienceId, "Audience");
        var left = new SCTrialAudienceLeftPacket(AudienceId, "Audience");
        var joinedBody = joined.Write(new PacketStream());
        var leftBody = left.Write(new PacketStream());
        joinedBody.Rollback();
        leftBody.Rollback();

        await Assert.That(joined.TypeId).IsEqualTo((ushort)0x1CC);
        await Assert.That(left.TypeId).IsEqualTo((ushort)0x137);
        await Assert.That(joinedBody.ReadUInt32()).IsEqualTo(12u);
        await Assert.That(joinedBody.ReadBc()).IsEqualTo(AudienceId);
        await Assert.That(leftBody.ReadBc()).IsEqualTo(AudienceId);
        await Assert.That(joinedBody.ReadString()).IsEqualTo("Audience");
        await Assert.That(leftBody.ReadString()).IsEqualTo("Audience");
        await Assert.That(joinedBody.Pos).IsEqualTo(joinedBody.Count);
        await Assert.That(leftBody.Pos).IsEqualTo(leftBody.Count);
    }
}
