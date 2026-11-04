# Глобальная война (GlobalWar)

Режим-кампания Star Wars SS14: Республика против Империи на 22 мирах. Этот файл для разработчика, который не видел, как режим
делали: что это, как идёт раунд, где какой код и как его запустить.

**Где код.** Всё лежит в `master`. Режим влит PR #378 «Add the GlobalWar campaign mode» (2026-11-03, merge `13da513480`),
потом шли исправления отдельными PR. Отдельной ветки с исходниками режима нет. Описание ниже сверено с `master` на `ef91a1208a`.

**Почему README здесь.** Почти вся логика режима на сервере, в этой папке, и разработчик открывает её первой. Клиент и общие
типы лежат в одноимённых папках `Content.Client/_SW/GlobalWar/` и `Content.Shared/_SW/GlobalWar/`, ссылки на них ниже.

> **Меняется сейчас.** Потоки `gw-travel`, `gw-admin` и `gw-economy` переделывают перелёты, доступ, рынок и названия планет.
> Разделы с пометкой «меняется, см. PR» описывают то, что есть на `master` сейчас. После их PR сверяйтесь с кодом.

## Что это за режим

- Пресет `SWGlobalWar` (алиас `globalwar`), в голосование не попадает (`showInVote: false`). Для 10–30 игроков.
- 22 мира (сектора), у каждого две локации: **поверхность** и **орбита**, это две отдельные карты. Всего 44 карты.
- Стороны: Республика (10 миров), Империя (10 миров), два нейтральных мира.
- Игрок входит **гражданином** своей стороны на поверхности столицы (Корусант или Дромунд-Каас). На вербовочной консоли он
  предъявляет ID-карту и становится солдатом, комплект берёт в арсенале. Должности (глава, министры, офицеры, пилоты, рабочие,
  разведка, пользователь Силы) выбираются при входе, у каждой свой лимит мест.
- Миры захватываются консолями захвата: отдельно орбита и поверхность. Корабли покупаются на верфях и летают между мирами
  настоящим FTL.
- Экономика: рынки миров, производственные линии, снабжение армии физическими грузами (ящики, корабли), казна и бюджеты министерств.
- Погибший возвращается через 5 минут гражданином своей стороны (пользователь Силы может вернуться им же).

### Как идёт раунд

Значения из прототипа `SWGlobalWarCampaignProduction` (`Resources/Prototypes/_SW/GlobalWar/campaign-roles.yml`).
Значения по умолчанию в коде другие (60/30/180 минут), первая версия их переопределяет.

1. **Мир, 0–20 минут** (`peaceDuration: 1200`). Войну объявить нельзя. Нейтральные миры принимают финансирование 10 минут
   (`neutralFundingDuration: 600`) и присоединяются к одной из сторон. Шанс стороны растёт с её вкладом: у Республики (вклад Р + 1) / (Р + И + 2)
   (`RepublicProbability` в `SWGlobalWarPoliticsSystem.Neutrals.cs`). Без решения нейтрал аннексируется через `annexationDuration` (10 минут).
2. **20-я минута.** Глава стороны может объявить войну в окне правительства. Если обе стороны есть в игре, но **ни у одной нет
   главы**, война начинается сама (`SWGlobalWarPoliticsSystem.Lifecycle.cs`, `Advance`). Если глава есть и не объявляет, мир
   длится до предела.
3. **Война.** Захват локаций консолями, флот, дроиды обороны, снабжение. Главы могут предложить перемирие, санкции,
   пограничный конфликт, капитуляцию (`SWGlobalWarPoliticalAction` в `Content.Shared/_SW/GlobalWar/Politics/SWGlobalWarDiplomacyTypes.cs`).
4. **Конец** (`ArbitrateCampaignStep`):
   - у стороны не осталось территории: победа другой;
   - капитуляция одной стороны: передача территории за `capitulationDuration` (5 минут), затем победа; обе сразу: взаимная капитуляция;
   - одной стороны нет в игре 10 минут подряд: конец без победителя (`InsufficientParticipants`). Значит, в одиночку раунд
     кончается на 10-й минуте;
   - **предел 120 минут** (`maximumDuration: 7200`). При `timeLimitVictory: true` побеждает сторона, у которой больше орбит и
     поверхностей, при равенстве победителя нет. Счёт объявляется за 10 минут до конца.
5. Окно итога кампании, потом раунд перезапускается сам через `RoundRestartTime` (`PublishCampaignStep`).

Прочие настройки первой версии: линкоры не продаются (`disabledShipClasses: [ Battleship ]`, карты и порты остаются),
стартовый арсенал считается по числу участников и пополняется, когда сторона растёт (`SWGlobalWarStartingArsenal`).

## Системы по папкам

Пространство имён повторяет путь: `Content.Server._SW.GlobalWar.Politics` и так далее. Типы начинаются с `SWGlobalWar`.

### Кампания: правило, планеты, локации (корень папки)

| Файл | Что делает |
|---|---|
| `SWGlobalWarGameruleSystem.cs`, `SWGlobalWarGameruleComponent.cs` | Правило-синглтон. При старте проверяет конфигурацию, создаёт сущности планет и рынки; при конце всё останавливает. У каждого запуска свой `Generation` (Guid): по нему системы отбрасывают устаревшие операции |
| `SWGlobalWarPlanetSystem.cs`, `SWGlobalWarPlanetComponent.cs` | Планеты и их локации. Карта локации **грузится при первом обращении** (`TryEnsureLocation`: вход игрока, телепорт, прилёт, возврат). Пустая карта ставится на паузу через `emptyMapPauseDelay`. Владельцы орбиты и поверхности меняются только через `TryChangeOwnership` |
| `SWGlobalWarPlanetTeleporterSystem.cs` | Телепорт поверхность ↔ орбита одного мира |
| `SWGlobalWarTravelSystem.cs`, `.Withdrawal.cs` | Межпланетный перелёт корабля: проверка, резерв точки прибытия, FTL к орбите цели. **Меняется, см. PR потока `gw-travel`** |
| `SWGlobalWarGalaxySystem.cs` | Живая карта галактики для солдата: владельцы миров раз в секунду, клиенту только при изменении |
| `SWGlobalWarDeathUiSystem.cs` | При смерти закрывает все окна, привязанные к телу |

Прототип мира: `swGlobalWarPlanet` (`Content.Shared/_SW/GlobalWar/SWGlobalWarPlanetPrototype.cs`), список миров в
`Resources/Prototypes/_SW/GlobalWar/campaign.yml`.

### Politics: кампания, должности, деньги, дипломатия

| Файл | Что делает |
|---|---|
| `SWGlobalWarCampaignSystem.cs`, `SWGlobalWarCampaignComponent.cs` | Часы кампании, фазы (`Running`, `Capitulating`, `Ending`, `Stopped`), все длительности |
| `SWGlobalWarPoliticsSystem.*.cs` | Отношения сторон, команды главы (`.Commands`), нейтралы (`.Neutrals`), право захвата (`.Policy`, `.CapturePolicy`), вывод войск при перемирии (`.Withdrawal`, `.ShipWithdrawal`), торговые ограничения (`.Trade`), исход и конец раунда (`.Lifecycle`, `.RoundEnd`) |
| `SWGlobalWarCampaignStepSystem.cs` | Закрывает шаг владения после политических систем. Никто, кроме него, не меняет владельцев напрямую |
| `SWGlobalWarAssignmentsSystem.*.cs` | Должности и права. **ID-карта и история работы не дают прав**, права даёт только запись о назначении. Назначения, вербовка (`.Recruitment`), преемственность главы (`.Succession`), спавн по должности |
| `SWGlobalWarLedgerSystem.*.cs` | Казна, счета министерств, переводы и резервы денег |
| `SWGlobalWarGovernmentConsoleSystem.cs` | Консоли правительства: финансы и дипломатия |
| `SWGlobalWarCharacterUiSystem.cs` | Строка службы в окне персонажа |
| `SWGlobalWarCoachSystem.cs` | Подсказки «что делать дальше» по должности; строка уходит, когда игрок сделал то, о чём она |
| `SWGlobalWarAideSystem.cs` | Офицер связи своей стороны вместо Tippy: 10 поводов, каждый не чаще раза за раунд |
| `SWGlobalWarShipOwnershipSystem.cs` | Чей корабль |

### Market: экономика, снабжение, заводы

Самая большая часть (46 файлов). **Меняется, см. PR потока `gw-economy`.**

- `SWGlobalWarMarketSimulation*.cs`: чистая модель рынков, шаг раз в секунду (`StepDuration`). Запасы, потребление,
  производство, потоки между мирами, лантан, возвраты, снабжение. Не трогает сущности, её удобно тестировать.
- `SWGlobalWarMarketSystem*.cs`: связь модели с игрой. Окна рынка (`.Views`, `.FlowEditing`, `.Growth`), хабы (`.Hub*`),
  физические заводы, добыча и грузы (`.PhysicalFactories`, `.PhysicalMining`, `.PhysicalFreight`), программа снабжения армии
  (`.Supply*`), стартовый арсенал (`.StartingArsenal`).
- `SWGlobalWarMarketConsoleSystem.cs`, `SWGlobalWarHubConsoleSystem.cs`: консоли рынка и хаба.
- Прототипы: `commodities.yml`, `economy-commodities.yml`, `economy-flows.yml`, `economy-profiles.yml`, `supply-commodities.yml`,
  `map-production.yml`, `production.yml`, `orbital-hub.yml`.
- Без игрока-промышленника автоматически идут только пайки. Оружие, броня, магазины и медицина приходят из стартового запаса.

### Spawning: вход, спавн, возврат

| Файл | Что делает |
|---|---|
| `SWGlobalWarSpawnSystem.cs`, `.Admission.cs` | Вход в раунд: сторона и должность, проверка мест и ограничений, спавн на поверхности столицы |
| `SWGlobalWarSpawnSystem.Respawn.cs` | Возврат после смерти, задержка `respawnDelay` 300 с |
| `SWGlobalWarSpawnSystem.Return.cs` | Резерв клетки на поверхности для возвращающегося тела |
| `SWGlobalWarSpawnSystem.TutorialAdmission.cs` | Вход гражданином из обучения |
| `SWGlobalWarReceptionSystem.cs` | Точки приёма: годится ли точка и путь от неё для тела (вывод войск, возвраты) |
| `SWGlobalWarSpawnConfigurationComponent.cs` | Должности и станции сторон: задаются в `campaign-roles.yml` |

Должности и их лимиты: `campaign-roles.yml`, работы в `Resources/Prototypes/_SW/GlobalWar/Roles/`.

### Access: кто куда может пройти

`SWGlobalWarAccessPolicySystem.cs` решает, может ли тело попасть на локацию (портал, телепорт, FTL);
`SWGlobalWarPortalAccessSystem.cs` применяет решение к порталам. **Меняется, см. PR потока `gw-admin`.**

### Combat: захват, оборона, флот

| Файл | Что делает |
|---|---|
| `SWGlobalWarCaptureSystem.cs` | Захват локации или корабля консолью: 300 с, пока захватчики в зоне и консоль под питанием. Живой защитник в зоне ставит захват на паузу; без захватчиков 30 с захват сбрасывается |
| `SWGlobalWarDefenceSystem.cs` | Дроиды обороны по заказу с консоли: 8000 кредитов, 60 с постройки, до 3 на локацию и 12 на сторону |
| `SWGlobalWarFleetSystem.cs` | Верфи: заказ корпуса за деньги бюджета, расход списывается только когда корабль пристыкован; стартовый флот (`SWGlobalWarStartingFleet`) |
| `SWGlobalWarGunnerySystem.cs`, `SWGlobalWarShipControlSystem.cs` | Орудия корабля (управление батареей по аренде, обычные боеприпасы и урон) и штурвал: кто может вести корабль, по должности и владельцу |
| `SWGlobalWarReconSystem.cs` | Разведка: датапад записывает ближнее устройство и видимых защитников. Украденный датапад роли разведки не даёт |
| `SWGlobalWarHostilitySystem.cs` | Перепроверяет политику кампании при выстреле, уроне, взрыве и стрельбе турелей: можно ли сейчас бить эту цель |
| `SWGlobalWarHullPreparationSystem.cs`, `SWGlobalWarStationAnchorSystem.cs` | Подготовка купленного корпуса; станции локаций закреплены, чтобы корабль их не сдвигал |

### Остальное на сервере

- `Identity/`: закрепляет сторону игрока на весь раунд, независимо от смены тела и разума.
- `Mapping/`: якоря карт (`SWGlobalWarMapAnchorComponent`): точки, по которым режим находит консоли, телепорты, площадки на
  карте. Маркеры в `Resources/Prototypes/_SW/Entities/Markers/GlobalWar/map_anchors.yml`.
- `Production/`: заводы кампании.
- Обучение: `Content.Server/_SW/Tutorial/` и `Content.Client/_SW/Tutorial/`, карта `Resources/Maps/SW/Mega/Tutorial/civil_workshop.yml`.

### Клиент

`Content.Client/_SW/GlobalWar/UI/` (71 файл):

- `SWGlobalWarWindow`: база всех окон кампании (рамка терминала, шрифт, цвет стороны). Стиль: `SWGlobalWarConsoleStyle`,
  `SWGlobalWarConsoleButton`, `SWGlobalWarKit`.
- Окна по консолям: рынок и атлас (`SWGlobalWarMarketConsoleMenu.*`), карта и перелёт (`SWGlobalWarTravelConsoleMenu`,
  `SWGlobalWarMapControl.*`, `SWGlobalWarGalaxyWindow`), правительство (`SWGlobalWarFinanceMenu`, `SWGlobalWarDiplomacyMenu`),
  хаб, завод, захват (`SWGlobalWarCaptureMenu`, `SWGlobalWarCaptureBar`), флот, орудия, разведка, возврат (`SWGlobalWarRespawnWindow`),
  итог кампании (`SWGlobalWarRoundEndWindow`), уведомление о службе и значок в верхней панели.
- Подсказки и офицер связи: `SWGlobalWarCoachUIController`, `SWGlobalWarAideUIController`.
- Вне этой папки: HUD (`Content.Client/_SW/UserInterface/Hud/`), лобби (`Content.Client/_SW/Lobby/SWLobbyUIController.cs`).

Общие типы окон и сообщений: `Content.Shared/_SW/GlobalWar/` (`*Ui.cs`, `Politics/`, `Combat/`).

## Где лежит остальное

| Что | Где |
|---|---|
| Пресет и правила | `Resources/Prototypes/_SW/GlobalWar/game-presets.yml`, `campaign-roles.yml` (`SWGlobalWarCampaignProduction`), `campaign.yml` (миры и базовое правило `SWGlobalWarCampaign`) |
| Тестовое правило с тремя мирами | `gamerules.yml` (`SWGlobalWar`, миры `SWGlobalWarTest*`), для тестов |
| Прототипы карт | `Resources/Prototypes/_SW/Maps/mega-mode.yml`, `Resources/Prototypes/_SW/GlobalWar/maps.yml` (пул `SWGlobalWarMapPool` с картой `Empty`: у раунда нет своей станции, миры грузит кампания) |
| Карты | `Resources/Maps/SW/Mega/Surface/`, `Orbit/` (по 22), `Ships/` (крейсеры и линкоры сторон), `Tutorial/` |
| Консоли и прочие сущности | `Resources/Prototypes/_SW/GlobalWar/*.yml` (`combat-consoles.yml`, `government-consoles.yml`, `market-console*.yml`, `travel-console.yml`, `planet-teleporter.yml`) |
| Локализация | `Resources/Locale/ru-RU/_SW/global-war/`, `ru-RU/_SW/GlobalWar/combat.ftl`; английский в `en-US/_SW/` так же |
| Справочник в игре | `Resources/Prototypes/_SW/Guidebook/campaign.yml`, текст `Resources/ServerInfo/_SW/Guidebook/Campaign.xml` |
| Тесты | `Content.IntegrationTests/Tests/_SW/GlobalWar/` (31 класс), `Content.Tests/_SW/GlobalWar/` |

### Миры

**Названия меняются, см. PR.** Сейчас в `campaign.yml`:

| Сторона | Миры (id, название, тип) |
|---|---|
| Республика | R0 Корусант (столица), R1 Дантуин, R2 Набу (сельское хозяйство), R3 Аркания, R4 Утапау, R5 Илум, R6 Белсавис (добыча), R7 Кореллия, R8 Куат (промышленность), R9 Тайтон (Сила) |
| Империя | E0 Дромунд-Каас (столица), E1 Датомир, E2 Рилот (сельское хозяйство), E3 Салласт, E4 Джеонозис, E5 Кессель, E6 Мустафар (добыча), E7 Зиост, E8 Фондор (промышленность), E9 Коррибан (Сила) |
| Нейтралы | N0 Альдераан (туризм), N1 Орд-Мантелл (промышленность) |

Id прототипов: `SWGlobalWarR0` и так далее, карты `SWGlobalWarR0Surface`, `SWGlobalWarR0Orbit`. Файлы карт названы по смыслу
(`coruscant.yml`, `republic_mine_quarry.yml`), сверяйте по `mega-mode.yml`.

## Как запустить

### Локально

Сборка и запуск как обычно (`CLAUDE.md`). Потом в консоли сервера или клиента с правами хоста (локальный игрок получает их сам):

```
setgamepreset globalwar
restartroundnow
```

Или в лобби до старта раунда: `forcepreset globalwar` (работает только в предраундовом лобби). Можно сразу запустить сервер с
пресетом: `dotnet run --project Content.Server -- --cvar game.defaultpreset=globalwar`. Если в вашей локальной конфигурации
лобби выключено, включите его `--cvar game.lobbyenabled=true`: в лобби видно окно выбора стороны и должности.

Для проверки нужны два клиента, по одному за сторону. Один игрок на сервере: кампания закончится на 10-й минуте без
победителя, так задумано (`InsufficientParticipants`).

### На сервере

Пресет выбирается только админом: `setgamepreset globalwar` перед раундом (вторым аргументом можно задать число раундов) или
`game.defaultpreset = "globalwar"` в конфиге сервера. Перед запуском проверьте:

- `game.role_timers` (по умолчанию `true`). Запрет колицидам на роли Силы работает только при `true`.
- `tips.enabled` по умолчанию `false` во всех режимах с PR #378: случайные советы и Tippy выключены и в обычных раундах.

## Админские и отладочные команды

Своих консольных команд у режима на `master` нет, кроме обучения. **Меняется: поток `gw-admin` добавляет админские средства, см. его PR.**

| Команда | Зачем |
|---|---|
| `setgamepreset globalwar [раунды]`, `forcepreset globalwar` | Включить режим |
| `restartroundnow`, `restartround`, `endround` | Перезапуск и конец. Конец раунда админом даёт исход `AdministrativeStop` |
| `delaystart`, `startround` | Лобби |
| `aghost`, `tp` | Посмотреть миры. Карта мира грузится при первом входе на неё |
| `swtutorial new`, `swtutorial server`, `swtutorial exit` | Обучение (доступна всем): для новичка в игре, для новичка на сервере, выход |
| ViewVariables (`vv`) | Состояние: сущность правила (`SWGlobalWarGameruleComponent`, `SWGlobalWarCampaignComponent`, `SWGlobalWarPoliticsComponent`), сущности планет (`SWGlobalWarPlanetComponent`: владельцы, состояние локаций) |

Для логов: `[ERRO]` в логе сервера при загрузке мира почти всегда значит проблему карты.

## Тесты

```bash
dotnet test --no-build --configuration DebugOpt Content.IntegrationTests/Content.IntegrationTests.csproj --filter "FullyQualifiedName~GlobalWarMechanicsTest" -- NUnit.ConsoleOut=0 NUnit.MapWarningTo=Failed
```

- Группы: `GlobalWarMarket*`, `GlobalWarEconomyIntegrationTest` (большая, по частичным классам), `GlobalWarDiplomacyTest`,
  `GlobalWarPoliticsTest`, `GlobalWarSpawnTest`, `GlobalWarRespawnTest`, `GlobalWarTravelTest`, `GlobalWarPlanetTest`,
  `GlobalWarWorldTest`, `GlobalWarFleetCampaignTest`, `GlobalWarLateJoinGuiTest`. Все 44 карты грузит `GameMapsLoadableTest`.
- Тестовые классы, которые открывают окна клиента, помечены `[NonParallelizable]`: параллельные клиенты ломают кэш шрифтов
  (`An item with the same key has already been added`). Новому такому классу нужно то же.
- `GlobalWarEconomyLongRunTest` помечен `[Explicit]`, идёт до 20 минут: запускайте вручную после правок экономики.
- Весь набор integration-тестов не укладывается в 20-минутный сторож пула ни на этой ветке, ни раньше: после 20-й минуты все
  тесты падают с `Pool manager has not been initialized`, это не настоящие падения. Гоняйте по группам.

## Известные пробелы

- **Карты первой версии сделаны быстрым проходом**, без обзора и почти без запусков в игре: гарантированы загрузка, `map_lint`,
  пары телепортов и правило лута (на мирах нет бесплатных вещей, кроме одного шкафа с дефибриллятором в медпункте поверхности
  своей стороны). Ходьба, питание и воздух на большинстве миров в игре не проверены. Второй этап карт впереди.
- Живыми игроками режим не проверен, сетевая нагрузка 20–60 человек не замерена.
- Шаг рынка на поздних минутах до 28 мс при цели 16 мс (прогон 120 минут скриптом).
- Без промышленника автоматически идут только пайки; автолинии полуфабрикатов отложены.
- Физический груз ждёт виртуальную партию; при блокаде промежуточного мира может повиснуть без потери (по коду, не проверено).
- Межпланетный маршрут есть только в меню действий корабля; консоль навигации станции отказывает солдату.
- Офицер связи: 8 из 10 поводов проверены только по коду.
- Отложено после плейтестов: линкоры, межпланетное снабжение сверх базового, ветки обучения офицера, министра, разведки,
  адресные работы. Не делается сейчас: пираты, мандалорцы, подкрепления кампании.
