namespace AAEmu.Game.Models.Game.Items;

public enum SlotType : byte
{
    None = 0,            // None = 0 Invalid
    Equipment = 1,       // Equipment = 1,
    Inventory = 2,       // Inventory = 2, Bag
    Bank = 3,            // Bank = 3,
    Trade = 4,           // Trade = 4, Coffer
    Seized = 5,
    Auction = 6,
    Mountable = 64,
    Mountable1Bag = 65,
    Mountable2Bag = 66,
    ShortcutAction = 235,
    ContributionPoint = 236,
    PetBattleEquipment = 237,
    PetBattleCommand = 238,
    PetBattleAction = 239,
    AutoUseAaPoint = 240,
    AaPoint = 241,
    SlaveEquipment = 242,
    AbilityView = 243,
    InstantKillStreak = 244,
    LivingPoint = 245,
    ModeAction = 246,
    PetRideCommand = 247,
    PetRideAction = 248,
    Constant = 249,
    HonorPoint = 250,
    StoreGood = 251,
    EquipmentMate = 252, // EquipmentMate = 252, PetRideEquipment
    Mail = 253,          // Mail = 5, MailAttachment
    Action = 254,
    System = 0xFF        // System = 0xFF Money
}
