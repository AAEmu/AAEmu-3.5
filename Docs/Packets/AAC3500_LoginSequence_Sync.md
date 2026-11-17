# Синхронизация S2C-пакетов логин-сиквенса с клиентом AAC3500

## Цель

Привести серверные S2C-пакеты логин-сиквенса (`AAEmu.Game/Core/Packets/G2C`) в соответствие с клиентом ArcheAge 3.5.0.3 (AAC3500).

## Источники данных

- **Имена классов / поля / структуры** — бинарь 10.0 (`x2game-dev.dll`), IDA MCP `http://127.0.0.1:13338/mcp`.
- **Опкоды 3.5** — бинарь 3.5 (`x2game_dump.dll`), IDA MCP `http://127.0.0.1:13337/mcp`.
  - В 3.5 опкоды видны через глобальные имена вида `SCSomePacket_0xXXX`.
  - В 10.0 имена классов видны через RTTI-строки `.?AVSCSomePacket...`.

## Переименованные классы и константы

| Новое имя класса / константа | Предыдущее имя | Примечание |
| --- | --- | --- |
| `CharacterListPacket` | `SCCharacterListPacket` | Имя приведено к 10.0-RTTI. |
| `CharacterStatePacket` | `SCCharacterStatePacket` | Имя приведено к 10.0-RTTI. |
| `SCAccountAttributeListPacket` | `SCAccountAttributePacket` | 10.0-имя содержит `List`. |
| `SCSystemFactionListPacket` | `SCFactionListPacket` | 3.5-глобал `SCSystemFactionList_0x296`. |

## Опкоды логин-сиквенса (3.5)

```csharp
SCAccountInfoPacket                         = 0x1F9;
SCAccountAttributeConfigPacket              = 0x070;
SCAccountAttributeListPacket                = 0x0CA; // был SCAccountAttributePacket
CharacterListPacket                         = 0x076; // был SCCharacterListPacket
CharacterStatePacket                        = 0x17E; // был SCCharacterStatePacket
SCRaceCongestionPacket                      = 0x05C;
SCGetSlotCountPacket                        = 0x266;
SCAbilitySetUsableSlotCountUpdatedPacket    = 0x13B;
SCAbilitySetAllInfoPacket                   = 0x043;
SCCharacterGamePointsPacket                 = 0x1BA;
SCActionSlotsPacket                         = 0x0D8;
SCQuestsPacket                              = 0x1EB;
SCCompletedQuestsPacket                     = 0x01F;
SCActabilityPacket                          = 0x113;
SCCountUnreadMailPacket                     = 0x26F;
SCAppellationsPacket                        = 0x2C5;
SCCharacterPortalsPacket                    = 0x1C4;
SCFriendsPacket                             = 0x0E8;
SCBlockedUsersPacket                        = 0x21D;
SCMyHousePacket                             = 0x2D1;
SCConflictZoneStatePacket                   = 0x283;
SCInstanceVisitCountsPacket                 = 0x007;
SCFactionRelationListPacket                 = 0x148;
SCExpeditionRolePolicyListPacket            = 0x1AA;
SCResponseUIDataPacket                      = 0x258;
SCUnitStatePacket                           = 0x204;
SCJoinedChatChannelPacket                   = 0x222;
SCSystemFactionListPacket                   = 0x296; // был SCFactionListPacket
```

## Структурные правки пакетов

| Пакет | Что изменено | Подтверждение в 3.5 |
| --- | --- | --- |
| `SCAccountInfoPacket` | `payMethod(s32), payLocation(s32), payStart(u64), payEnd(u64), realPayTime(s64), buyPremiumCount(s32)` | Обработчик 3.5 |
| `SCRaceCongestionPacket` | 9 байт `con`, без `forbidCharCreating` | Обработчик 3.5 |
| `SCAccountAttributeConfigPacket` | 3 булевых `used` | Обработчик 3.5 |
| `SCCharacterGamePointsPacket` | 14 полей `moneyAmount` (int) | Обработчик 3.5 |
| `SCCompletedQuestsPacket` | `count, idx(u32), body(u64)` | Обработчик 3.5 |
| `SCInstanceVisitCountsPacket` | `zoneId, data, count, resetCount, permittedCount` | Обработчик 3.5 |
| `SCAbilitySetUsableSlotCountUpdatedPacket` | один байт `usableCount` | Обработчик 3.5 |
| `SCAbilitySetAllInfoPacket` | добавлены `savedHighAbilitySets` (3 байта) перед обычными и extra-типами | Обработчик 3.5 |
| `SCConflictZoneStatePacket` | добавлено поле `lock(u64)` после `endTime` | `u16 type, u8 hpws, u64 end, u64 lock` |
| `SCActionSlotsPacket` | клиент читает ровно 217 слотов по 16 байт; сервер дополняет массив до 217 | Обработчик 3.5 |
| `SCResponseUIDataPacket` | поле `type` теперь `s64` (long), а не `uint` | `Serialize_s64("type")` |
| `SCJoinedChatChannelPacket` | `(short)type, subType, (uint)factionId, name` соответствует 8-байтовой компоновке `chat(s64), name(string)` | Обработчик 10.0/3.5 |

## Затронутые файлы

- `AAEmu.Game/Core/Packets/G2C/SCOffsets.cs` — опкоды и переименования.
- `AAEmu.Game/Core/Packets/G2C/GameNetwork.cs` — регистрация переименованных пакетов.
- `AAEmu.Game/Core/Packets/G2C/CharacterListPacket.cs` — переименование.
- `AAEmu.Game/Core/Packets/G2C/CharacterStatePacket.cs` — переименование.
- `AAEmu.Game/Core/Packets/G2C/SCSystemFactionListPacket.cs` — переименование с `SCFactionListPacket`.
- `AAEmu.Game/Core/Packets/G2C/SCAccountAttributeListPacket.cs` — переименование с `SCAccountAttributePacket`.
- `AAEmu.Game/Core/Packets/G2C/SCConflictZoneStatePacket.cs` — добавлено поле `lock`.
- `AAEmu.Game/Core/Packets/G2C/SCActionSlotsPacket.cs` — фиксировано 217 слотов.
- `AAEmu.Game/Core/Packets/G2C/SCResponseUIDataPacket.cs` — `type` как `long`.
- `AAEmu.Game/Core/Packets/G2C/SCJoinedChatChannelPacket.cs` — проверена компоновка.
- `AAEmu.Game/Core/Packets/G2C/SCAbilitySetAllInfoPacket.cs` — высшие способности.
- `AAEmu.Game/Models/Game/Char/Character.cs` — метод `Write` подстроен под новые пакеты.
- `AAEmu.UnitTests` — исправлены вызовы `SCCharacterListPacket` → `CharacterListPacket`.

## Проверка

```bash
dotnet build
dotnet test --filter AAEmu.UnitTests
```

- Сборка `AAEmu.Game` успешна.
- `AAEmu.UnitTests`: **1095 / 1095** проходят.
- `AAEmu.Login.IntegrationTests` падают с `DockerUnavailableException` в текущем окружении; это не связано с правками пакетов.

## Проверка по десериализаторам x2game_dump.dll 3.5.0.3 (2026-07-18)

Полная сверка всех 53 пакетов сессии входа (из Server.log) с десериализаторами клиента
(IDB `x2game_dump.dll.i64`, метки `<Name>_0x<opcode>`, функции чтения через `struc_2->Read->*`).
Опкоды всех 53 пакетов совпадают. Протокол: `AAC3500-diff/docs/2026-07-18-packet-verify-login-sequence.md`.

### Исправлено

| Пакет | Было | Стало | Источник в клиенте |
| --- | --- | --- | --- |
| `SCGameRuleConfigPacket` | `type:uint`, `type2:uint`, `peaceMin:short` | `type:ushort`, `type2:ushort`, `peaceMin:int` | sub_3993BE30: `type:u16, pvp:bool, duel:bool` / `type:u16, peaceMin:i32` |
| `SCTaxItemConfig2Packet` | `type:uint` | `type:ushort` | sub_3992BB20: `count:i32, [type:u16, declareDominion:bool]` |
| `Portal.Write` (SCCharacterReturnDistricts 0x1BF, SCCharacterPortals 0x1C4) | нет поля `isFavorite` | добавлен `IsFavorite:bool` после `ZRot` | sub_397F3F40: `id:i32, name, zoneId:i32, pos:vec3, zRot:f32, isFavorite:bool` |
| `SCResultRestrictCheckPacket` | `characterId:u32, code:u8, result:u8` | только `msg:string` | sub_3991D430 (класс опкода 641=0x281) |

### Проверено, расхождений нет

SCInitialConfig, SCAccountInfo, SCChatSpamDelay, SCAccountAttributeConfig, SCLevelRestrictionConfig,
SCTaxItemConfig, SCInGameShopConfig, SCProtectFaction, SCGetSlotCount, CharacterList (+Character.Write,
EQUIP_6140), SCRaceCongestion, SCAccountAttributeList, SCResponseUIData (type=u32!), SCAccountWarned,
SCCheckRaceCongestionResponse, CharacterState, SCCharacterInvenInit/Contents, SCCharacterGamePoints (14×u32),
SCCompletedQuests (idx=u16!), SCActionSlots, SCQuests/Quest.Write, SCQuestContextUpdated, SCActability,
SCCountUnreadMail, SCConflictZoneState (lock(u64) в 3.5.0.3 НЕ читается — текущий вариант без lock верен),
SCSystemFactionList, SCFactionRelationList, SCExpeditionRolePolicyList, SCBuffCreated, SCUnitState,
SCDetailedTimeOfDay, SCDoodadsCreated (scale присутствует), SCJoinedChatChannel, SCBuffRemoved,
SCTargetChanged, SCTimeOfDay, SCSnowingEverywhere, SCUnitPoints, SCAddHouse/House.WriteAddHouse,
SCCharacterReturnDistricts, CSSelectCharacter, CSSpawnCharacter/CharacterVisualOptions, CSSetLpManageCharacter,
CSRestrictCheck, CSRequestUIData, CSPremiumServiceMsg, CSNotifyInGame(Completed).

## Что требует дальнейшей работы

1. **`SCUnitStatePacket`** — нужна декомпиляция 3.5-обработчика для точного порядка полей.
2. **`CharacterStatePacket`** — требуется уточнить сериализацию `Character.Write` после выделения `instanceId/guid/rwd/srwd`.
3. **`Quest.Write`** — проверить порядок полей квестов в `SCQuestsPacket`.
4. **`SCMyHousePacket` / `SCHouseStatePacket`** — декомпиляция `sub_39AE7450` показала, что текущий `House.Write` не совпадает с 3.5-структурой:
   - лишние поля (`CoOwnerId`, `OwnerId`, tax, `x/y/z`, прогресс стройки отправляются в этом же пакете);
   - не хватает `expandedDecoLimit` (Int32) и `isPublic` (bool);
   - порядок полей (`ht`, `type`, `type`, `owner`, `accountId`, `permission`, position, `house`, `allowRecover`, `sellPrice`, `sellToName`, `expandedDecoLimit`, `sellToPlayerId`, `isPublic`) отличается от текущего.
   - Требуется разделение/переписывание `House.Write` либо добавление отдельных методов сериализации для `SCMyHousePacket` и `SCHouseStatePacket`.
