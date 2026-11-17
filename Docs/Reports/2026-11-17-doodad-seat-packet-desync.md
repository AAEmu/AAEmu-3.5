# Ошибки пакетов после посадки на дудад — 2026-11-17

## Симптом и подтверждённая ошибка

После посадки и вставания перестают работать взаимодействия с дудадами.
В сохранённом серверном логе присутствует цепочка:

- 02:50:30 — skill 15857, doodad template 4506 / object 19810, character object 74387.
- Наложение бафа 4645, затем SCBondDoodad (0x088).
- 02:50:33 — SCBuffRemoved (0x175), затем SCUnbondDoodad (0x27A).
- Последующих CSStartSkill в этом сеансе нет; security report продолжает приходить.
- 02:56:29 — System:Quit в клиентском логе; 02:56:30 — закрытие серверных соединений.

SCBuffRemoved содержал лишний байт `01` перед UInt32 index. Для index=1
клиентский формат читает index=257 и оставляет один непрочитанный байт.
Созданный и снимаемый баф перестают совпадать. Это воспроизведено тестом
цепочки SCBuffCreated -> SCBuffRemoved для индексов 1, 256, 0x12345678.

Пример payload для object 74387, index 1:

```text
До:    93 22 01 | 01 | 01 00 00 00
После: 93 22 01 |      01 00 00 00
```

## Проверка по бинарному клиенту

Использованы локальные базы `D:\AA-dump\3503AAC\x2game_dump.dll.i64`
и `D:\AA-dump\3503AAC\crynetwork_dump.dll.i64`.

В CryNetwork обработчик входящих игровых пакетов `sub_65E16FD` создаёт
сериализатор с vtable `0x066876E0` и проверяет точное потребление payload.
При несовпадении размера вызывает путь `sub_65E9E2A(2)`.

У этого сериализатора:

- vtable +0x14 (`Block`) -> `sub_66304B0`: возвращает 1, не читает данные;
- vtable +0x18 (`BlockEnd`) -> пустая функция;
- vtable +0x3C (`UInt32`) -> `sub_6630BE6`: читает ровно 4 байта;
- vtable +0xCC -> чтение указанного числа сырых байтов.

Следовательно, `Block(name, true)` в декомпиляции не означает presence byte
в сетевом пакете. SCBuffCreated уже следовал этому правилу; SCBuffRemoved — нет.

## Исправленные форматы

| Пакет | Источник x2game | Формат payload |
| --- | --- | --- |
| SCBuffRemoved 0x175 | 0x3993E3D0 | bc target, UInt32 index |
| SCBuffUpdated 0x12B | 0x3993E500 | bc target, UInt32 index, Int32 stack, Int32 charged |
| SCGlideBuffRemoved 0x17F | 0x3993E460 | bc target, UInt32 index, position (9 bytes) |
| SCBuffState 0x04E | reader 0x39384300, no-op serializer 0x39375AC0 | пустой payload; прежний комментарий ошибочно ссылался на SCBuffCreated |
| SCSkillStopped 0x2AB | 0x3993E370 | bc unit, UInt32 skill |
| SCSkillLearned 0x262 | 0x3993E8B0 | UInt32 skill |
| SCListSkillActiveTyps 0x1FC | 0x3991F9C0 -> 0x397F7260 | count (4 bytes), [UInt32 skill, byte activeType] |
| SCUpdateSkillActiveType 0x034 | 0x3993F4D0 | UInt32 skill, byte activeType |
| SCChatMessage, quest link | 0x39938BA0 -> 0x39B15320 | linkType=3, UInt16 start, UInt16 length, UInt32 questId |

SCBondDoodad (0x39927560 -> 0x39AE0340) и SCUnbondDoodad (0x399275C0)
совпали с текущей серверной сериализацией и не изменялись.

## Проверки

- До исправления SCBuffRemoved: все 3 теста цепочки создания/снятия бафа падают.
- Дополнительные 8 проверок других исправляемых форматов также воспроизвели ошибки до правок.
- После правок: BuffPacketTests 6/6, SkillPacketWireFormatTests 5/5.
- AAEmu.Game Debug собран; 4 предупреждения вне изменённых пакетов.
- Полный запуск AAEmu.UnitTests: 1142/1145, 3 сбоя вне изменённых пакетов:
  - BuffTests.Constructor_WithParameters_SetsPropertiesCorrectly: сравнение UInt16 AbLevel с UInt32 в TUnit;
  - BuffTests.AbLevel_SetAndGet_ReturnsCorrectValue: сравнение UInt16 AbLevel с Int32 в TUnit;
  - LoginSessionTests.InitiateEnterWorld_ServerNotActive_NoAction: ожидается отсутствие пакетов, получен ACEnterWorldDeniedPacket.
- `git diff --check` проходит.

Для сборки Game и тестов использован `-p:BuildProjectReferences=false`:
в рабочей копии параллельно добавляются исходники Geometry/ZoneSim, их общий
прогон сборки на момент проверки останавливался на отсутствующих сетевых типах.
Использованы уже собранные зависимости; Game.dll обновлён в Game/bin/Debug/net10.0.

Воспроизводимые команды (из корня репозитория):

```powershell
dotnet build AAEmu.Game/AAEmu.Game.csproj -c Debug --no-restore --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false -p:BuildProjectReferences=false
dotnet build AAEmu.UnitTests/AAEmu.UnitTests.csproj -c Debug --no-restore --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false -p:BuildProjectReferences=false
& ./AAEmu.UnitTests/bin/Debug/net10.0/AAEmu.UnitTests.exe --treenode-filter '/*/*/BuffPacketTests/*'
& ./AAEmu.UnitTests/bin/Debug/net10.0/AAEmu.UnitTests.exe --treenode-filter '/*/*/SkillPacketWireFormatTests/*'
& ./AAEmu.UnitTests/bin/Debug/net10.0/AAEmu.UnitTests.exe
```

## Игровая приёмка

Пока не выполнена. Нужны перезапуск Game, новый вход клиентом и цикл:
сесть -> встать -> взаимодействовать с другим дудадом -> повторно сесть.
Отдельно проверить глайдер, прерывание навыка и отсутствие отложенного дисконнекта.
Ошибка формата доказана; устранение исходного дисконнекта через 6 минут
по старым логам не подтверждается. Ранее добавленная NET-DIAG диагностика оставлена
для следующего игрового сеанса. Таймауты не изменялись.
