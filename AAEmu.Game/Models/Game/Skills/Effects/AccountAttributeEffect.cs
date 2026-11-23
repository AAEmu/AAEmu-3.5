using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Account;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class AccountAttributeEffect : EffectTemplate
{
    public uint KindId { get; set; }
    public uint KindValue { get; set; }
    public bool BindWorld { get; set; }
    public bool IsAdd { get; set; }
    public uint Count { get; set; }
    public uint Time { get; set; }

    public override bool OnActionTime => false;

    internal bool TryGetAuctionPostingChange(out bool canPost)
    {
        canPost = IsAdd;
        return KindId == AuctionPostingAccess.Kind && KindValue == AuctionPostingAccess.Listing &&
               !BindWorld && Count == 0 && Time == 0;
    }

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (caster is not Character character || source?.Skill is not { Cancelled: false } skill)
            return;

        if (!TryGetAuctionPostingChange(out var canPost) ||
            !ReferenceEquals(target, character) ||
            character.Connection is not { } connection || connection.AccountId != character.AccountId ||
            !ReferenceEquals(connection.ActiveChar, character) ||
            casterObj is not SkillItem item || !item.ResolveSourceItem(character) ||
            item.SkillSourceItem is not { Count: > 0, SlotType: SlotType.Inventory } sourceItem ||
            sourceItem.Template is not { UseSkillId: > 0 } itemTemplate || itemTemplate.UseSkillId != skill.Template?.Id)
        {
            skill.Cancelled = true;
            character.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            return;
        }

        var result = AccountManager.Instance.ChangeAuctionPostingAccess(connection, canPost);
        if (result != ErrorMessageType.NoErrorMessage)
        {
            skill.Cancelled = true;
            character.SendErrorMessage(result);
        }
    }
}
