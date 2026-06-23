namespace AAEmu.Game.Models.Game.Items.Actions;

// TODO at X2::GameClient::ApplyItemTaskToSelf
public enum ItemTaskType : byte
{
    // updated to version 3.5.0.3 rev 342464
    Invalid,					// invalid
    Destroy,					// destroy
    AboxDestroy,				// abox-destroy
    Repair,						// repair
    DurabilityLoss,				// durability-loss
    SwapItems,					// swap-items
    Split,						// split
    SplitCofferItems,			// split-coffer-items
    SwapCofferItems,			// swap-coffer-items
    Loot,						// loot
    LootAll,					// loot-all
    Gm,							// gm
    GameRuleReset,				// gamerule-reset
    ConsumeSkillSource,			// 303-consume-skill-source
    DoodadCreate,				// 303-doodad-create
    DoodadRemove,				// doodad-remove
    DoodadItemChanger,			// doodad-item_changer
    DoodadInteraction,			// doodad-interaction
    DoodadCattleFeed,			// doodad-cattle-feed
    //UpgradeSkill = 19,        // UpgradeSkill = 19,
    AbilityChange,				// ability-change
    AbilityReset,				// ability-reset
    //BuyPriestBuff = 22,       // BuyPriestBuff = 22,
    //Teleport = 23,            // Teleport = 23,
    CapturePet = 21,            // capture-pet
    RecoverDoodadItem = 23,	   	// 508-recover-doodad-item
    MateCreate,					// mate-create
    CraftActSaved,				// craft-act-saved
    CraftPaySaved,				// craft-pay-saved
    CraftPickupProduct,			// craft-pickup-product
    CraftCancel,				// craft-cancel
    HouseCreation,				// 303-house-creation
    HouseDeposit,				// house-deposit
    HouseBuilding,				// house-building
    PickupBloodstain,			// pickup-bloodstain
    AutoLootDoodadItem,			// 316-autoloot-doodad-item
    QuestStart,					// 401-quest-start
    QuestComplete,				// 402-quest-complete
    QuestSupplyItems,			// 405-quest-supply-items
    QuestRemoveSupplies,		// 402-403-404-405-quest-remove-supplies
    SkillReagents = 39, 		// skill-reagents
    SkillEffectConsumption = 40,	// skill-effect-consumption
    SkillEffectGainItem = 41,	// skill-effect-gain-item
    SkillEffectGainItemWithPos,	// skill-effect-gain-item-with-pos
    SkillEffectSiegeTicket,		// skill-effect-siege-ticket
    SkillEffectExpToItem,		// skill-effect-exp-to-item
    Auction,					// auction
    Mail = 46,					// mail +
    Trade,						// trade
    EnchantMagical,				// enchant-magical
    EnchantPhysical,			// enchant-physical
    GetCoinByItem,				// get-coin-by-item
    GetItemByCoin,				// get-item-by-coin
    StoreSell = 52,				// 315-store-sell +
    StoreBuy = 53,				// 313-314-store-buy +
    TodReward,					// tod-reward
    GainItemWithUcc,			// gain-item-with-ucc
    ImprintUcc,					// imprint-ucc
    RepairPets,					// repair-pets
    MateDeath = 58,				// mate-death +
    Shipyard,					// 303-shipyard
    SkillsReset,				// skills-reset
    DropBackpack,				// drop-backpack
    UseRelic,					// use-relic
    UseIndependenceRelic,		// use-independence-relic
    Conversion,					// 304-conversion
    Seize,						// seize
    ReturnSeized,				// return-seized
    DemoDressOff,				// demo-dress-off
    DemoDressOn,				// demo-dress-on
    DemoClearBag,				// demo-clear-bag
    DemoFillBag,				// demo-fill-bag
    SlaveDeath,					// slave-death
    ExpeditionCreation,			// expedition-creation
    DeclareExpeditionWar,		// declare-expedition-war
    RecruitmentDecMoney,		// recruitment-dec-money
    RepairSlaves,				// repair-slaves
    ExpandBag,					// expand-bag
    ExpandBank,					// expand-bank
    //RenewEquipment = 77,	// RenewEquipment = 77,
    LifespanExpiration,			// lifespan-expiration
    RecoverExp,					// recover-exp
    SpawnerUpdate,				// spawner_update
    UpdateSummonSlaveItem,		// update-summon-slave-item
    UpdateSummonMateItem,		// update-summon-mate-item
    DepositMoney,				// deposit-money
    WithdrawMoney,				// withdraw-money
    DeliverItemToOthers,		// deliver-item-to-others
    SetSlavePosition,			// set-slave-position
    SetBountyMoney,				// set-bounty-money
    PayBountyMoney,				// pay-bounty-money
    ConvertFish,				// convert-fish
    Fishing,					// fishing
    SellHouse,					// sell-house
    BuyHouse,					// buy-house
    SaveMusicNotes,				// save-music-notes
    ItemLock,					// item-lock
    ItemUnlock,					// item-unlock
    ItemUnlockExcess,			// item-unlockexcess
    GradeEnchant,				// grade-enchant
    RechargeRndAttrUnitModifier,	// recharge-rnd-attr-unit-modifier
    RechargeBuff,				// recharge-buff
    Socketing,		    		// socketing
    Evolving = 102,				// evolving
    Smelting,					// smelting
    Dyeing,						// dyeing
    ConsumeIndunTicket,			// consume-indun-ticket
    ExpandExpert,				// expand-expert
    Exchange,					// exchange
    SellBackpack,				// sell-backpack
    SellSpecialty,				// sell-specialty
    BuySpecialty,				// buy-specialty
    AskMould,					// ask-mould
    TakeMould,					// take-mould
    FactionDeclareHostile,		// faction-declare-hostile
    EditCosmetic,				// edit-cosmetic
    ChangeAutoUseAaPoint,		// change-auto-use-aa-point
    ConvertItemLook = 115,		// convert_item_look
    RevertItemLook = 116,	    // revert_item_look
    ChangeExpertLimit,			// change-expert-limit
    Skinize,					// sknize
    ItemTaskThistimeUnpack,		// item-task-thistime-unpack
    BuyPremiumService,			// buy-premium-service-ingameshop
    BuyAaPoint,					// buy-aa-point-ingameshop
    TakeScheduleItem,			// take-schedule-item
    ScaleCap = 125,				// scale-cap
    ScaleCapReset,			    // scale-cap-reset
    HousePayTax,				// house-pay-tax
    BuyItemIngameshop,		    // buy-item-ingameshop
    ExchangeCashFromItem,	    // exchange-cash-from-item
    RepairSlaveEquipment,		// repair-slave-equipment
    RechargeSkill,				// recharge-skill
    AchievementSupplyItems,	    // achievement-supply-items
    TodayAssignmentUnlock,	    // today-assignment-unlock
    HouseRebuild,				// house-rebuild
    ItemTaskResurrectionInPlace,// item-task-resurrection-in-place
    TodayAssignmentSupplyItems,	// today-assignment-supply-items
    ItemTaskBattleCoin,			// item-task-battle-coin
    GiveRewardItem,				// give-reward-item
    ExchangeAapointFromCash,	// exchange-aapoint-from-cash
    SpendItemFromChat,			// spend-item-from-chat
    ItemTaskRemoveHeroReward,	// item-task-remove-hero-reward
    ExpeditionSummon,			// expedition-summon
    MateRevive,					// mate-revive
    ExpandAbilitySetSlot,		// expand-ability-set-slot
    QuestCompleteBalance,		// 402-quest-complete-balance
    ExpandDecoLimit,			// expand-deco-limit
    HouseDemolish,				// house-demolish
    SiegeAuction,				// siege-auction
    SelectiveItem,				// selective-item
    NationRequestFriend,		// nation_request_friend
    DoodadOneshotPlace,			// doodad-oneshot-place
    NationDelegate,             // nation-delegate
    RenameFaction,              // rename-faction
    ConsumHighAbilityLevelUp,   // consum_high_ability_level_up
    RenameFaction2,             // rename-faction
    BlessUthstinInitStats,		// bless-uthstin-init-stats
    FamilyJoin,					// family-join
    FamilyLeave,				// family-leave
    FamilyKick,					// family-kick
    FamilyIncMember,			// family-inc-member
    FamilyChangeName,			// family-change-name
    HeirSkillReset,				// heir-skill-reset
    SlaveFollow,				// slave-follow
}
