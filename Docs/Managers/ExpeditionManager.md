# ExpeditionManager

## Назначение

`ExpeditionManager` управляет экспедициями (гильдиями): загрузкой из базы данных, созданием, приглашением, исключением, сменой владельца/ролей, роспуском и рассылкой связанных пакетов.

## Источники данных

Данные загружаются из MySQL (база `aaemu_game`) и конфигурации приложения.

| Таблица | Назначение | Ключевые поля |
|---------|------------|---------------|
| `expeditions` | Список гильдий | `id`, `mother`, `name`, `owner`, `owner_name`, `created_at` |
| `expedition_members` | Члены гильдии | `character_id`, `expedition_id`, `name`, `level`, `role`, `last_leave_time`, `ability1`, `ability2`, `ability3`, `memo` |
| `expedition_role_policies` | Права ролей | `expedition_id`, `role`, `name`, `dominion_declare`, `invite`, `expel`, `promote`, `dismiss`, `chat`, `manager_chat`, `siege_master`, `join_siege` |

Конфигурация приложения (`AppConfiguration.Expedition`) задаёт регулярное выражение для названия гильдии, стоимость создания, требования к уровню/группе и базовый набор политик ролей.

## Модели

- `Expedition` — гильдия, наследник `SystemFaction`.
- `ExpeditionMember` — член гильдии, реализует `PacketMarshaler`.
- `ExpeditionRolePolicy` — политика прав роли, реализует `PacketMarshaler`.

## Сетевые пакеты

Имена и opcodes восстановлены из IDA Pro MCP (`x2game-dev.dll`, 10.0.2.13-cn). Структуры пакетов в `x2game_dump.dll` (ArcheAge 3.5.0.3) не содержат именованных символов, поэтому для справки используются имена и opcodes из отладочной сборки.

### Клиент → Сервер (C2G)

| Пакет | Opcode | Описание |
|-------|--------|----------|
| `CSCreateExpeditionPacket` | `0x004` | Создание гильдии |
| `CSChangeExpeditionRolePolicyPacket` | `0x006` | Изменение политики роли |
| `CSChangeExpeditionMemberRolePacket` | `0x007` | Смена роли члена |
| `CSChangeExpeditionOwnerPacket` | `0x008` | Передача владельца |
| `CSReplyExpeditionInvitationPacket` | `0x00D` | Ответ на приглашение |

### Сервер → Клиент (G2C)

| Пакет | Opcode | Описание |
|-------|--------|----------|
| `SCExpeditionListPacket` | `0x00A` | Список экспедиций |
| `SCExpeditionRolePolicyListPacket` | `0x00D` | Список политик ролей |
| `SCExpeditionRolePolicyChangedPacket` | `0x00E` | Изменение политики роли |
| `SCExpeditionRoleChangedPacket` | `0x00F` | Смена роли |
| `SCExpeditionOwnerChangedPacket` | `0x010` | Смена владельца |
| `SCExpeditionCreatedPacket` | `0x015` | Гильдия создана |
| `SCExpeditionDismissedPacket` | `0x017` | Гильдия распущена |
| `SCExpeditionInvitationPacket` | `0x02D` | Приглашение в гильдию |
| `SCExpeditionMemberListPacket` | `0x023` | Список членов |
| `SCExpeditionMemberListEndPacket` | `0x024` | Конец списка членов |
| `SCSetExpeditionProtectDatePacket` | `0x022` | Защита гильдии |

### World/Unit (WZ/Proxy)

| Пакет | Opcode | Описание |
|-------|--------|----------|
| `WZFactionListPacket` | `0x013` | Список фракций |
| `WZFactionCreatedPacket` | `0x015` | Фракция создана |
| `WZUnitExpeditionChangedPacket` | `0x01A` | Изменение экспедиции юнита |
| `UnitExpeditionChangedPacket` | `0x02F` | Изменение экспедиции юнита (SC) |
| `WZExpeditionWarStatePacket` | `0x01B` | Состояние войны экспедиций |

## Проверка

- Добавлены модульные тесты `ExpeditionManagerTests`, проверяющие отображение полей таблиц на свойства моделей.
- Структуры пакетов из `x2game_dump.dll` не извлечены — в IDB отсутствуют именованные символы экспедиционных пакетов.
