using System;
using System.Collections.Generic;

namespace Content.Client._SW._Senate.Training;

/// <summary>
/// Timelines of chapters 07А, 07Б, 08А and 10А: the defence fleet and world defence, internal security investigations
/// and the finance reserve. Labels, reasons and results are those of the ministry work console and its server systems:
/// the fleet order preview, the planetary programs and reserve, the security case options and the governor requests.
/// One story runs through them: the Корусант sector, its governor Келлан Ренн and the world Дунидин.
/// </summary>
public static partial class SWTrainingSamples
{
    private const string SectorGovernor = "Келлан Ренн";

    // One array per set of buttons: rows compare them by reference.
    private static readonly string[] DefenseTabs = { "Командование", "Флот", "Операции", "Оборона миров" };
    private static readonly string[] FleetNames = { "1-й флот", "2-й флот" };
    private static readonly string[] FleetTasks = { "Перемещение", "Охрана", "Патруль", "Граница", "На базу", "Учения" };
    private static readonly string[] SecurityCases = { "Контрабандная сеть", "Имперская сеть" };
    private static readonly string[] ImperialCaseOnly = { "Имперская сеть" };
    private static readonly string[] SecurityTeams = { "Группа 1", "Группа 2" };
    private static readonly string[] FinanceReserveTabs = { "Монетарка", "ДОЛГ", "Бюджеты", "Резерв", "Карта финансов", "Допналоги и льготы" };
    private static readonly string[] ReserveTabs = { "Запросы секторов", "Переводы и история" };
    private static readonly string[] GovernorAnswers = { "Выделить средства", "Отказать" };

    // ---- 07А: fleet --------------------------------------------------------

    private const string FleetStats = "Сила 82   Урон 10   Снабжение 28   Подготовка 60";
    private const string FleetRisk = "Снабжение расходуется; повреждения требуют верфи.";

    /// <summary>The «Флот» tab with the first fleet selected: its place, the order card, the tasks, the crossing and its state.</summary>
    private static SWTrainingFormPanel FleetPanel(string location = "На позиции: Корусант", string target = "Куда отправить флот?",
        string? stages = null, string issue = "Выберите назначение", bool issueOff = true, bool aimIssue = false, string? reason = null,
        int aimTask = -1, bool border = false, string? risk = null)
    {
        var rows = new List<SWTrainingRow>
        {
            Choice(FleetNames),
            Txt("1-й флот"),
            Dim(location),
            Txt(target),
        };
        if (stages != null)
            rows.Add(Dim(stages));
        rows.Add(Btn(issue, aimIssue, issueOff));
        if (reason != null)
            rows.Add(Dim(reason));
        rows.Add(Choice(FleetTasks, -1, aimTask));
        if (border)
        {
            rows.Add(Dim("Переход к Коррибан   Флота на позиции нет"));
            rows.Add(Warn("Напряжённо, прикрытие недостаточно"));
        }
        if (risk != null)
            rows.Add(Warn(risk));
        rows.Add(Dim(FleetStats));
        return new SWTrainingFormPanel("Флот", rows.ToArray());
    }

    private static SWTrainingFormPanel FleetList(int aim = -1) => Form("Флот",
        Choice(FleetNames, -1, aim),
        Dim("Корусант   Альдераан"),
        Dim("Выберите флот на карте или в списке"));

    private static SWTrainingFormPanel FleetMap(bool aim) => Form("Флот",
        Choice(FleetNames),
        Txt("1-й флот"),
        Sec("Галактика"),
        Item("Корусант", "1-й флот"),
        Item("Телос", string.Empty, target: aim));

    private static SWTrainingFormPanel FleetOnPost(bool covered) => Form("Флот",
        Choice(FleetNames),
        Txt("1-й флот"),
        Dim("На позиции: Телос   Прикрытие границы"),
        Dim("Переход к Коррибан   1-й флот"),
        covered ? Fine("Спокойно, переход прикрыт") : Warn("Напряжённо, прикрытие недостаточно"),
        Dim("Сила 82   Урон 10   Снабжение 24   Подготовка 60"));

    private const string MoveStages = "Прибытие через 33 мин   Бесплатно";
    private const string BorderStages = "Прибытие через 41 мин   Бесплатно";

    public static SWTrainingStage[] FleetSelect() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(MinistryConsole, DefenseTabs, 1), time: "10:00"),
        Press(TabRow(MinistryConsole, DefenseTabs, 1), time: "10:00"),
        Wait(FleetList(), "Два флота стоят у своих баз.", 3f, "10:00"),
        Aim(FleetList(0), time: "10:00"),
        Press(FleetList(0), time: "10:00"),
        Result(FleetPanel(reason: "Выберите сектор или мир на карте"),
            note: "1-й флот у Корусанта. Снабжения у него 28: в долгом деле он слабее, чем по силе на бумаге.", time: "10:00"));

    public static SWTrainingStage[] FleetTarget() => Steps(
        Aim(FleetMap(true), time: "10:02"),
        Press(FleetMap(true), time: "10:02"),
        Result(FleetPanel(target: "Назначение: Телос", stages: MoveStages, issue: "Отправить в Телос", issueOff: false, border: true),
            note: "Телос стоит на переходе к Коррибану, и прикрытия там нет. Пока выбрано простое перемещение.", time: "10:02"));

    public static SWTrainingStage[] FleetBorder() => Steps(
        Aim(FleetPanel(target: "Назначение: Телос", stages: MoveStages, issue: "Отправить в Телос", issueOff: false, aimTask: 3, border: true), time: "10:03"),
        Press(FleetPanel(target: "Назначение: Телос", stages: MoveStages, issue: "Отправить в Телос", issueOff: false, aimTask: 3, border: true), time: "10:03"),
        Result(FleetPanel(target: "Назначение: Телос", stages: BorderStages, issue: "Прикрыть переход", issueOff: false, border: true, risk: FleetRisk),
            note: "Задача «Граница»: флот встанет на переход и будет его прикрывать. Видны срок, цена и риск, но не исход.", time: "10:03"),
        Result(FleetPanel(target: "Назначение: Телос", stages: BorderStages, issue: "Прикрыть переход", reason: "Министр не на службе", border: true, risk: FleetRisk),
            note: "Приказа нет без министра на службе, ночью, без разрешённого маршрута, при снабжении ниже 15 или без денег на платный приказ.",
            alternative: true, time: "10:03"));

    public static SWTrainingStage[] FleetSend() => Steps(
        Back("Министр на службе, маршрут разрешён: приказ «Прикрыть переход» доступен.",
            FleetPanel(target: "Назначение: Телос", stages: BorderStages, issue: "Прикрыть переход", issueOff: false, border: true, risk: FleetRisk), "10:04"),
        Aim(FleetPanel(target: "Назначение: Телос", stages: BorderStages, issue: "Прикрыть переход", issueOff: false, aimIssue: true, border: true, risk: FleetRisk), time: "10:04"),
        Press(FleetPanel(target: "Назначение: Телос", stages: BorderStages, issue: "Прикрыть переход", issueOff: false, aimIssue: true, border: true, risk: FleetRisk), time: "10:04"),
        Wait(FleetPanel(location: "К Телос   Прибытие через 41 мин", target: "Назначение: Телос", stages: BorderStages, issue: "Приказ отправлен", border: true),
            "Приказ принят. Сначала подготовка, затем переход.", 4f, "10:04"),
        Result(FleetPanel(location: "К Телос   Прибытие через 12 мин", target: "Назначение: Телос", stages: BorderStages, issue: "Прикрыть переход", border: true),
            note: "Флот в пути. Пока он идёт, можно сменить курс или вернуть его на базу, но не дать новую задачу.", time: "10:33"));

    public static SWTrainingStage[] FleetPost() => Steps(
        Result(FleetOnPost(true), note: "Флот встал на переход. Пока прикрытие не меньше давления, на границе спокойно.", time: "10:45"),
        Result(FleetOnPost(false), note: "Если давление выше прикрытия, флот на месте, а переход всё ещё под угрозой. При нападении исход решают сила, снабжение и подготовка.",
            alternative: true, time: "10:45"));

    // ---- 07Б: world defence ------------------------------------------------

    private const string SiteReason = "Крупный грузовой узел. Местная безопасность 38, оборона 42; объекту требуется защита.";

    private static SWTrainingFormPanel DefenseRequests(bool aim = false) => Form("Оборона миров",
        Txt("Запросы губернаторов: 1"),
        Btn("Прикрыть важный объект", aim),
        Dim($"Дунидин   {SectorGovernor}"),
        Dim(SiteReason),
        Dim("Ожидает решения министра"));

    private static SWTrainingFormPanel DefenseWorld(string orbital = "0%", string reserveHours = "0", string status = "Сначала разверните оборону",
        bool canOrder = false, bool aimOrder = false) => Form("Оборона миров",
        Txt("Дунидин"),
        Dim($"{SectorGovernor}   Защищаемый объект: Крупный грузовой узел"),
        Dim($"Гарнизон: 0%   Орбитальная оборона: {orbital}"),
        Sec("Материальный резерв"),
        Dim($"Запас обороны: {reserveHours} ч   Стоимость комплекта: 800 кр"),
        Dim(status),
        Btn("Заказать материальный резерв", aimOrder, !canOrder));

    /// <summary>The «Орбитальная оборона» card: its price before, then the delivered materials and the work done.</summary>
    private static SWTrainingFormPanel OrbitalCard(bool placed, string status = "", string generators = "0", string weapons = "0", string parts = "0",
        string transit = "", float supply = 0f, string work = "0", float worked = 0f, bool built = false, bool canStart = true, bool aim = false)
    {
        var rows = new List<SWTrainingRow>
        {
            Txt("Орбитальная оборона"),
            Fine("Снижает потери и перебои у порта"),
            Dim(placed ? $"{(built ? "Построено" : "Строительство")}: Дунидин" : "Стоимость строительства: 6 000 кр"),
        };
        if (status.Length > 0)
            rows.Add(Dim(status));
        rows.Add(Dim($"Генераторы щита: {generators} / 0.3{transit}"));
        rows.Add(Dim($"Корабельное вооружение: {weapons} / 0.15   Механические узлы: {parts} / 0.4"));
        if (placed)
            rows.Add(Meter(supply));
        rows.Add(Dim(placed ? $"Работы: {work} / 4 ч" : "Срок работ: 4 ч"));
        if (placed)
            rows.Add(Meter(worked, SWTrainingAccent.Neutral));
        rows.Add(Btn(built ? "Построено" : placed ? "Строительство начато" : "Начать строительство", aim, placed || !canStart));
        return new SWTrainingFormPanel("Оборона миров", rows.ToArray());
    }

    public static SWTrainingStage[] WorldRequests() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(MinistryConsole, DefenseTabs, 3), time: "11:00"),
        Press(TabRow(MinistryConsole, DefenseTabs, 3), time: "11:00"),
        Result(DefenseRequests(), note: $"{SectorGovernor}, губернатор Корусанта, просит прикрыть Дунидин: там крупный грузовой узел.", time: "11:00"));

    public static SWTrainingStage[] WorldSelect() => Steps(
        Aim(DefenseRequests(true), time: "11:02"),
        Press(DefenseRequests(true), time: "11:02"),
        Result(DefenseWorld(), note: "Запрос только выбирает мир. Строить ли и что, решает министр в карточке программы ниже.", time: "11:02"));

    public static SWTrainingStage[] WorldBuild() => Steps(
        Wait(OrbitalCard(false), "Для грузового узла губернатор просит орбитальную оборону.", 4f, "11:03"),
        Aim(OrbitalCard(false, aim: true), time: "11:03"),
        Press(OrbitalCard(false, aim: true), time: "11:03"),
        Result(OrbitalCard(true, "Заказ принят"),
            note: "6 000 кр списаны из бюджета министерства. Дальше материалы едут на Дунидин, обороны пока нет.", time: "11:03"),
        Result(OrbitalCard(false, canStart: false),
            note: "Кнопка отключена ночью, без министра на службе или если в бюджете меньше сметы.", alternative: true, time: "11:03"));

    public static SWTrainingStage[] WorldProgress() => Steps(
        // The bar is the average of the delivered shares: 0.12/0.3, 0.15/0.15 and 0.2/0.4.
        Back("Строительство начато днём, 6 000 кр списаны. Часть грузов уже на месте, остальное в пути.",
            OrbitalCard(true, "Материалы в пути", "0.12", "0.15", "0.2", "; в пути 0.18", .63f), "12:30"),
        Wait(OrbitalCard(true, "Выполнение", "0.3", "0.15", "0.4", supply: 1f, work: "1.5", worked: .375f), "Всё доставлено, идёт монтаж батарей.", 4f, "14:30"),
        Result(OrbitalCard(true, "Действует", "0.3", "0.15", "0.4", supply: 1f, work: "4", worked: 1f, built: true),
            note: "Оборона заработала только после доставки и монтажа. Дальше её готовность держится на военном снабжении.", time: "17:00"));

    public static SWTrainingStage[] WorldReserve() => Steps(
        Wait(DefenseWorld("100%", status: "Резерва нет", canOrder: true), "Батареи стоят, а запаса на перебои нет.", 3f, "17:10"),
        Aim(DefenseWorld("100%", status: "Резерва нет", canOrder: true, aimOrder: true), time: "17:10"),
        Press(DefenseWorld("100%", status: "Резерва нет", canOrder: true, aimOrder: true), time: "17:10"),
        Result(DefenseWorld("100%", status: "Заказ резерва принят"),
            note: "800 кр списаны, комплект заказан. Пока заказ не выполнен, второй не оформить.", time: "17:10"),
        Wait(DefenseWorld("100%", status: "Военное снабжение: 0.2/0.5, в пути 0.3; Механические узлы: 0.25/0.25, в пути 0; Электронные компоненты: 0/0.1, в пути 0.1"),
            "Механические узлы и часть военного снабжения уже на Дунидине; остальное снабжение и электроника идут из Корусанта.", 4f, "17:40"),
        Wait(DefenseWorld("100%", status: "Подготовка резерва"), "Всё доставлено, резерв готовят к хранению.", 4f, "18:20"),
        Result(DefenseWorld("100%", "4", "Резерв: 4 ч нехватки снабжения"),
            note: "Резерв на складе: если поставки прервутся, оборона продержится ещё около четырёх часов. Пока он не израсходован, новый не заказать.", time: "18:50"));

    // ---- 08А: investigations -----------------------------------------------

    private const string SecurityBudget = "Бюджет: 2400 кр   Закрыто дел: 0";
    private const string FinishRisk = "Вероятность провала и безвозвратной потери группы: 29%; улики сохранятся";
    private const string SupplyWarn = "Недостаток снабжения: +4% к риску; итоговая вероятность потери 29%.";

    /// <summary>The «Расследования» tab: the cases, the dossier of the domestic case, the teams and the one action for its stage.</summary>
    private static SWTrainingFormPanel CaseFile(bool open = true, int aimCase = -1, int stage = 1, float evidence = 0f, int clues = 0,
        int aimTeam = -1, string? teamStatus = null, string action = "Собрать улику — 0 кр", bool actionOff = true, bool aimAction = false,
        bool aimMap = false, string? reason = "Выберите группу", string? path = null, string? warn = null)
    {
        var rows = new List<SWTrainingRow> { Txt(SecurityBudget), Choice(SecurityCases, -1, aimCase) };
        if (!open)
        {
            rows.Add(Dim("Выберите дело"));
            return new SWTrainingFormPanel("Расследования", rows.ToArray());
        }

        rows.Add(Sec($"Этап {stage}"));
        rows.Add(Dim("Место расследования: Квенк"));
        rows.Add(Meter(evidence));
        if (clues > 0)
            rows.Add(Btn($"Показать улики ({clues})", accent: SWTrainingAccent.Neutral));
        rows.Add(Btn("Показать на карте", aimMap, accent: SWTrainingAccent.Neutral));
        rows.Add(Choice(SecurityTeams, -1, aimTeam));
        if (teamStatus != null)
            rows.Add(Txt(teamStatus));
        rows.Add(Btn(action, aimAction, actionOff));
        if (warn != null)
            rows.Add(Warn(warn));
        if (reason != null)
            rows.Add(Warn(reason));
        if (path != null)
            rows.Add(Dim(path));
        return new SWTrainingFormPanel("Расследования", rows.ToArray());
    }

    /// <summary>«Карта и группы» with the first team and Квенк chosen.</summary>
    private static SWTrainingFormPanel TeamMap(string status, string action = "Собрать улику", bool actionOff = false, bool aim = false) => Form("Карта и группы",
        Choice(SecurityTeams),
        Dim("Бюджет: 2400 кр   Ячейки: 0/5"),
        Txt("Квенк"),
        Txt(status),
        Dim("Снабжение: 100/100, готова"),
        Btn("Контрабандная сеть", accent: SWTrainingAccent.Neutral),
        Btn(action, aim, actionOff),
        Dim("Стоимость: 0 кр; путь: 24 мин; работа: 1 ч 00 мин"),
        Warn("Низкий"));

    private static SWTrainingFormPanel CaseResult(string log, string closed, string[] cases) => Form("Расследования",
        Txt($"Бюджет: 2400 кр   Закрыто дел: {closed}"),
        Sec("Результаты"),
        Dim(log),
        Choice(cases),
        Dim("Выберите дело"));

    public static SWTrainingStage[] CaseOpen() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(MinistryConsole, SecurityTabs, 2), time: "10:00"),
        Press(TabRow(MinistryConsole, SecurityTabs, 2), time: "10:00"),
        Aim(CaseFile(false, 0), time: "10:00"),
        Press(CaseFile(false, 0), time: "10:00"),
        Result(CaseFile(), note: "Контрабандная сеть на Квенке, улик нужно две. Пока группа не выбрана, приказа нет.", time: "10:00"));

    public static SWTrainingStage[] CaseTeam() => Steps(
        Aim(CaseFile(aimTeam: 0), time: "10:01"),
        Press(CaseFile(aimTeam: 0), time: "10:01"),
        Result(CaseFile(actionOff: false, reason: null, path: "Путь: 24 мин; работа: 1 ч 00 мин   Низкий"),
            note: "Группа 1 готова. Сбор улики бесплатен; видны путь, работа и риск.", time: "10:01"),
        Aim(CaseFile(actionOff: false, reason: null, aimMap: true, path: "Путь: 24 мин; работа: 1 ч 00 мин   Низкий"), time: "10:02"),
        Press(CaseFile(actionOff: false, reason: null, aimMap: true, path: "Путь: 24 мин; работа: 1 ч 00 мин   Низкий"), time: "10:02"),
        Result(TeamMap("Группа 1: Готова   Корусант"), note: "На карте видно, где стоит группа и куда ей идти: с Корусанта на Квенк.", time: "10:02"));

    public static SWTrainingStage[] CaseEvidence() => Steps(
        Aim(TeamMap("Группа 1: Готова   Корусант", aim: true), time: "10:03"),
        Press(TeamMap("Группа 1: Готова   Корусант", aim: true), time: "10:03"),
        Wait(TeamMap("Группа 1: В пути — 24 мин   Корусант", "Приказ отправлен", true), "Группа вылетела на Квенк.", 4f, "10:03"),
        Wait(TeamMap("Группа 1: Работает — 1 ч 00 мин   Квенк", "Собрать улику", true), "На месте группа ищет следы.", 4f, "10:27"),
        Result(CaseFile(stage: 2, evidence: .5f, clues: 1, teamStatus: null, actionOff: false, reason: null, path: "Путь: 0 мин; работа: 1 ч 03 мин   Низкий"),
            note: "Первая улика собрана, группа снова готова. Снабжения стало 90 из 100, поэтому вторая работа займёт чуть дольше.", time: "11:27"));

    private const string SecondClue = "Путь: 0 мин; работа: 1 ч 03 мин   Низкий";

    public static SWTrainingStage[] CaseFinish() => Steps(
        Aim(CaseFile(stage: 2, evidence: .5f, clues: 1, actionOff: false, aimAction: true, reason: null, path: SecondClue), time: "11:27"),
        Press(CaseFile(stage: 2, evidence: .5f, clues: 1, actionOff: false, aimAction: true, reason: null, path: SecondClue), time: "11:27"),
        Wait(CaseFile(stage: 2, evidence: .5f, clues: 1, teamStatus: "Работает: осталось 1 ч 03 мин", action: "Приказ отправлен", reason: "Группа занята"),
            "Группа уже на Квенке, поэтому сразу берётся за вторую улику.", 4f, "11:27"),
        Wait(CaseFile(stage: 2, evidence: .5f, clues: 1, teamStatus: "Работает: осталось 20 мин", action: "Собрать улику — 0 кр", reason: "Группа занята"),
            "Работа идёт, другой приказ этой группе не дать.", 4f, "12:10"),
        Wait(CaseFile(stage: 2, evidence: 1f, clues: 2, action: "Завершить дело — 0 кр", actionOff: false, reason: null, warn: SupplyWarn,
            path: $"Путь: 0 мин; работа: 1 ч 00 мин   {FinishRisk}"), "К 12:30 вторая улика собрана. Снабжения 80 из 100, и риск операции вырос.", 4f, "12:35"),
        Aim(CaseFile(stage: 2, evidence: 1f, clues: 2, action: "Завершить дело — 0 кр", actionOff: false, aimAction: true, reason: null, warn: SupplyWarn,
            path: $"Путь: 0 мин; работа: 1 ч 00 мин   {FinishRisk}"), time: "12:35"),
        Press(CaseFile(stage: 2, evidence: 1f, clues: 2, action: "Завершить дело — 0 кр", actionOff: false, aimAction: true, reason: null, warn: SupplyWarn,
            path: $"Путь: 0 мин; работа: 1 ч 00 мин   {FinishRisk}"), time: "12:35"),
        Result(CaseResult("Контрабандная сеть: дело раскрыто; доверие к безопасности сектора временно выросло.", "1", ImperialCaseOnly),
            note: "Операция удалась: через час работы дело закрыто, угроза снизилась. Группа 1 восстанавливается до 14:05.", time: "13:35"),
        Result(CaseResult("Контрабандная сеть: операция провалена, группа 1 потеряна; улики сохранены.", "0", SecurityCases),
            note: "С вероятностью 29% операция проваливается: группа 1 потеряна, улики остаются для новой попытки другой группой.", alternative: true, time: "13:35"));

    /// <summary>The imperial case with its evidence complete: the safe report, the sabotage that needs the chancellor, and its risk.</summary>
    private static SWTrainingFormPanel ImperialCase(string approval, string sabotage, bool sabotageOff, bool aim = false, string? reason = null,
        string? log = null, string? teamStatus = null, string budget = "2400", bool busy = false)
    {
        var rows = new List<SWTrainingRow> { Txt($"Бюджет: {budget} кр   Закрыто дел: 1") };
        if (log != null)
            rows.Add(Dim(log));
        rows.Add(Choice(ImperialCaseOnly));
        rows.Add(Dim("Место расследования: Коррибан"));
        rows.Add(Warn(approval));
        if (teamStatus != null)
            rows.Add(Txt(teamStatus));
        rows.Add(Btn("Завершить разведку — 0 кр", disabled: busy, accent: SWTrainingAccent.Neutral));
        rows.Add(Btn(sabotage, aim, sabotageOff, SWTrainingAccent.Bad));
        // Группа 1 comes back with 65 of 100 supply: +7% on top of the base 25%.
        rows.Add(Warn("Недостаток снабжения: +7% к риску; итоговая вероятность потери 32%."));
        rows.Add(Warn("Вероятность провала и безвозвратной потери группы: 32%; улики сохранятся"));
        if (reason != null)
            rows.Add(Dim(reason));
        return new SWTrainingFormPanel("Расследования", rows.ToArray());
    }

    private const string AskSabotage = "Запросить одобрение диверсии (запуск: 1200 кр)";
    private const string LaunchSabotage = "Запустить диверсию (запуск: 1200 кр)";

    public static SWTrainingStage[] CaseSabotage() => Steps(
        Back("Дело о контрабанде закрыто успешно, и группа 1 полчаса восстанавливается. По имперской сети на Коррибане улики уже собраны.",
            ImperialCase("Диверсия требует одобрения Канцлера", AskSabotage, true, reason: "Группа занята", teamStatus: "Восстановление: осталось 30 мин", busy: true), "13:35"),
        Wait(ImperialCase("Диверсия требует одобрения Канцлера", AskSabotage, false),
            "В 14:05 группа снова готова. Разведотчёт безопасен, диверсия сильнее, но опаснее.", 4f, "14:10"),
        Aim(ImperialCase("Диверсия требует одобрения Канцлера", AskSabotage, false, true), time: "14:10"),
        Press(ImperialCase("Диверсия требует одобрения Канцлера", AskSabotage, false, true), time: "14:10"),
        Result(ImperialCase("Ожидается решение Канцлера", AskSabotage, true, reason: "Ожидается решение канцлера",
                log: "Имперская сеть: запрошено одобрение канцлера."),
            note: "Запрос ничего не списывает и не отправляет группу. Решает канцлер.", time: "14:10"),
        Result(ImperialCase("Диверсия одобрена; подтвердите запуск", LaunchSabotage, false, log: "Канцлер одобрил диверсию; группа может начать операцию."),
            note: "Канцлер одобрил. Запускать всё равно нужно отдельно.", time: "14:30"),
        Aim(ImperialCase("Диверсия одобрена; подтвердите запуск", LaunchSabotage, false, true), time: "14:30"),
        Press(ImperialCase("Диверсия одобрена; подтвердите запуск", LaunchSabotage, false, true), time: "14:30"),
        Result(ImperialCase("Диверсия требует одобрения Канцлера", AskSabotage, true, reason: "Группа занята", teamStatus: "В пути: осталось 30 мин",
                budget: "1200", busy: true),
            note: "1 200 кр списаны, группа ушла, остальные приказы ей недоступны. Исход станет известен после работы: успех временно усилит защиту ближайшего сектора, провал будет стоить группы.", time: "14:30"),
        Result(ImperialCase("Диверсия одобрена; подтвердите запуск", LaunchSabotage, false, log: "Имперская сеть: диверсия автоматически одобрена; требуется запуск."),
            note: "Без канцлера запрос одобряется сам, но запуск всё равно подтверждает министр.", alternative: true, time: "14:10"));

    // ---- 10А: finance reserve ----------------------------------------------

    private const string ReserveNote = "Остаток возвращается в общую казну в конце дня.";
    private const string GovernorProject = "Производство защитного снаряжения";

    /// <summary>«Резерв», «Запросы секторов»: one governor's request, what it buys and how its price is split.</summary>
    private static SWTrainingFormPanel ReserveRequest(string remaining = "4 000", int aim = -1, bool approveOff = false, bool bothOff = false, string? result = null)
    {
        var rows = new List<SWTrainingRow>
        {
            Txt($"Остаток резерва: {remaining} кр из 6 000 кр"),
            Dim(ReserveNote),
            Choice(ReserveTabs),
        };
        if (result != null)
            rows.Add(Txt(result));
        rows.Add(Txt($"Корусант   Дунидин   Губернатор: {SectorGovernor}"));
        // A +1.2 expansion costs 650 + 250 × 1.2 = 950 кр in a sector without corruption markup.
        rows.Add(Txt($"{GovernorProject}   +1.2 цикла производства в час"));
        rows.Add(Dim("Загрузка 96%; запас 1.4 ч; потребность 2.5/ч"));
        rows.Add(Dim("Стоимость 950 кр   Средства сектора 600 кр   Из резерва 350 кр"));
        rows.Add(bothOff
            ? new SWTrainingRow(SWTrainingRowKind.Buttons, Buttons: GovernorAnswers, Disabled: true)
            : Choice(GovernorAnswers, approveOff ? 0 : -1, aim));
        return new SWTrainingFormPanel("Резерв", rows.ToArray());
    }

    private static SWTrainingFormPanel ReserveEmpty(string remaining, string result, int aimTab = -1) => Form("Резерв",
        Txt($"Остаток резерва: {remaining} кр из 6 000 кр"),
        Dim(ReserveNote),
        Choice(ReserveTabs, -1, aimTab),
        Txt(result),
        Dim("Нет запросов секторов"));

    public static SWTrainingStage[] ReserveOpen() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(MinistryConsole, FinanceReserveTabs, 3), time: "10:30"),
        Press(TabRow(MinistryConsole, FinanceReserveTabs, 3), time: "10:30"),
        Result(ReserveRequest(), note: $"{SectorGovernor} хочет расширить цех в Дунидине, но средств сектора не хватает.", time: "10:30"));

    public static SWTrainingStage[] ReserveRead() => Steps(
        Wait(ReserveRequest(), "Смета 950 кр: 600 сектор платит сам, 350 нужно из резерва.", 4f, "10:31"),
        Result(ReserveRequest(), note: "Выделить можно днём, министру на службе, при утверждённом бюджете дня и если в резерве хватает недостающей суммы.", time: "10:31"),
        Result(ReserveRequest("200", approveOff: true), note: "В резерве меньше 350 кр: кнопка «Выделить средства» отключена, отказать можно.",
            alternative: true, time: "10:31"));

    public static SWTrainingStage[] ReserveApprove() => Steps(
        Back("В резерве 4 000 кр: недостающих 350 хватает.", ReserveRequest(), "10:32"),
        Aim(ReserveRequest(aim: 0), time: "10:32"),
        Press(ReserveRequest(aim: 0), time: "10:32"),
        Wait(ReserveRequest(bothOff: true, result: "Ожидается ответ сервера"), "Пока ответа нет, обе кнопки отключены.", 3f, "10:32"),
        Result(ReserveEmpty("3 650", "Проект профинансирован и заказан"),
            note: "Проект, бюджет сектора и резерв остались прежними: из резерва ушло 350 кр, из бюджета сектора 600, расширение заказано.", time: "10:32"),
        Result(ReserveEmpty("4 000", "Проект устарел или больше недоступен"),
            note: "Если спрос на Дунидине за это время сменился, проекта больше нет: обращение закрыто, резерв не тронут.", alternative: true, time: "10:32"));

    public static SWTrainingStage[] ReserveOrder() => Steps(
        Back("Проект ещё был нужен: расширение профинансировано и заказано.", ReserveEmpty("3 650", "Проект профинансирован и заказан"), "10:35"),
        Aim(ReserveEmpty("3 650", "Проект профинансирован и заказан", 1), time: "10:35"),
        Press(ReserveEmpty("3 650", "Проект профинансирован и заказан", 1), time: "10:35"),
        Result(Form("Резерв", Txt("Остаток резерва: 3 650 кр из 6 000 кр"), Dim(ReserveNote), Choice(ReserveTabs), Sec("История переводов"),
                Txt($"{SectorGovernor}   350 кр"), Dim($"Дунидин: {GovernorProject}")),
            note: "Деньги ушли на заказ, а не на готовый цех: оборудование ещё доставят и смонтируют, и только потом мощность вырастет.", time: "10:35"));

    /// <summary>The whole shot is the other answer to the same request at 10:32; it opens on the request before the press.</summary>
    public static SWTrainingStage[] ReserveReject() => Steps(
        Case("Если цех Дунидину не нужен, на тот же запрос в 10:32 министр может ответить отказом.", ReserveRequest(), "10:32"),
        Aim(ReserveRequest(aim: 1), true, "10:32"),
        Press(ReserveRequest(aim: 1), true, "10:32"),
        Result(ReserveEmpty("4 000", "Обращение отклонено"),
            note: "Отказ закрывает обращение, резерв не тронут. Этот губернатор обратится снова не раньше следующего дня.", alternative: true, time: "10:32", continues: true));
}
