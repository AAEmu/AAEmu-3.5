# Changelog

## Unreleased

### Changed

- Updated `SkillManager.Load()` to match the current SQLite schema for `skills`, `buff_tick_effects`, `skill_controllers`, and related effect tables.
- Added mappings for previously missing effect types, including `DoodadItemChangeEffect`, `HighAbilityResourceEffect`, `LevelUpEffect`, `MoveToLocationEffect`, `PlayLogEffect`, and `WorldMessageEffect`.
- Added regression tests against the real SQLite data and updated the Russian testing progress documentation.
- Documented `AnimationManager` data sources (`anims` table schema), added `Docs/Managers/AnimationManager.md`, and added unit tests verifying the `anims` schema and `Anim` class mapping.
- Documented `AuctionManager` data source (`auction_house` MySQL table) and related auction packets, added `Docs/Managers/AuctionManager.md` and unit tests for `AuctionLot` column mapping.
- Documented `CashShopManager` data sources (`ics_skus`, `ics_shop_items`, `ics_menu` MySQL tables) and ICS packets, added `Docs/Managers/CashShopManager.md` and unit tests for `IcsSku`/`IcsItem` column mapping.
- Documented `ChatManager` (no DB tables, in-memory chat channels), added `Docs/Managers/ChatManager.md` and unit tests for `ChatChannel` properties.
- Documented `CommandManager` (no DB tables, command scripts from `Scripts/Commands`), added `Docs/Managers/CommandManager.md` and unit tests for command prefix.
- Documented `CraftManager` SQLite tables (`crafts`, `craft_products`, `craft_materials`, `craft_pack_crafts`), added `Docs/Managers/CraftManager.md` and unit tests verifying schema and class mappings.
- Documented `CrimeManager` MySQL table (`crime`), added `Docs/Managers/CrimeManager.md` and unit tests for `CrimeEvent` column mapping.

## History

### 2026-06-23

- `0b83fa0` `fix(game): исправить создание персонажа` - fixed character creation flow, item handling, skill manager loading, and related packet/state updates.

### 2026-06-21

- `18adceb` `fix(game): подключить БД от версии 3503 AAClassic` - switched the game server to the 3.5.0.3 AAClassic database layout.

### 2026-06-20

- `1720b56` `fix(game): исправить вход в лобби и создание персонажа` - fixed lobby entry and character creation path.
- `22eec2a` `update(game): обновлить информацию в пакетах` - updated packet data structures and serialized packet information.

### 2026-06-19

- `a779b1c` `feat(game): обновить ключевые пакеты и шифрование до версии 3.5` - upgraded core packets and encryption to the 3.5 protocol family.
- `59dc001` `fix(login): исправить Логин сервер` - fixed login server behavior.
- `aacbd19` `update: изменить uint на ulong для AccountId` - widened `AccountId` handling to `ulong`.
- `909aa2c` `update(game): добавить недостающих имен пакетов` - added missing packet names in the game server.

### 2026-06-18

- `beb5f5e` `Добавьте файлы проекта.` - initial project file bootstrap and repository scaffolding.
