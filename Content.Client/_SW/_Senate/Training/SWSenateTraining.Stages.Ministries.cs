using System;

namespace Content.Client._SW._Senate.Training;

/// <summary>
/// Timelines of chapters 08 to 14. Labels and messages come from the ministry and department consoles and their server
/// systems; which cases, prices and worlds appear depends on the round, so names and numbers are examples, marked as such.
/// Nothing here promises an outcome the game leaves to chance or to the economy.
/// </summary>
public static partial class SWTrainingSamples
{
    private const string MinistryConsole = "Рабочая консоль министерства";
    private const string DepartmentConsole = "Рабочая консоль департамента";
    private const string LineName = "Стерильные расходные материалы";
    private const string LineWorld = "Дунидин";
    private const string RouteName = "Корусант — Дунидин";

    // One array per set of buttons: rows compare them by reference.
    private static readonly string[] SecurityTabs = { "Политика безопасности", "Карта и группы", "Расследования" };
    private static readonly string[] PostureLabels = { "Чрезвычайное положение", "Усиленный", "Наблюдение", "Гражданский" };
    private static readonly string[] EconomyProgramTabs = { "Доступные проекты", "Выполнение и поставки", "Потрясения" };
    private static readonly string[] EconomyGoods = { "Выберите товар", "Медицинские расходники" };
    private static readonly string[] FinanceTabs = { "Монетарка", "ДОЛГ" };
    private static readonly string[] RateLabels = { "0%", "3%", "6%", "9%", "12%", "16%" };
    private static readonly string[] TaxLabels = { "Низкие 10%", "Средние 20%", "Высокие 30%" };
    private static readonly string[] Conclusions = { "Нарушений нет", "Халатность", "Умышленное нарушение" };
    private static readonly string[] HealthSections = { "Лечение", "Развитие", "Снабжение", "Состояние", "Справка" };
    private static readonly string[] SupplyTabs = { "В работе", "Завершено" };
    private static readonly string[] ResearchStages = { "1  Образцы", "2  Анализ", "3  Препарат", "4  Испытания", "5  Выпуск", "6  Вакцинация" };
    private static readonly string[] FundingLabels = { "Пауза", "Базовое", "Усиленное" };
    private static readonly string[] SampleWorlds = { "Дунидин", "Квенк" };
    private static readonly string[] LabWorlds = { "Корусант" };
    private static readonly string[] Producers = { "Квенк, 2 ч до Дунидин" };
    private static readonly string[] Recipients = { "Дунидин, заражение 12%", "Корусант" };
    private static readonly string[] BatchLabels = { "1 партия", "2 партии", "3 партии" };
    private static readonly string[] HealthcareTabs = { "Работа", "Вызовы", "Операции", "Заявки" };
    private static readonly string[] GuardTabs = { "Работа", "Ордера", "Операции", "Заявки" };
    private static readonly string[] MinusPlus = { "-", "+" };

    private static Step Hold(SWTrainingPanelState panel, string? note = null, float hold = 3f, bool alternative = false) =>
        new(hold, Closeup(), panel, Notes: note == null ? null : ScreenNote(note), Alternative: alternative);

    private static SWTrainingRow Choose(string label, string[] items, int current, bool target = false, bool open = false) =>
        new(SWTrainingRowKind.Options, label, Target: target, Chosen: open, Buttons: items, Current: current);

    private static SWTrainingRow Check(string text, bool pressed, bool target = false) =>
        new(SWTrainingRowKind.Check, text, Target: target, Chosen: pressed);

    /// <summary>A line with a counter and its two buttons, as a guard post in the department console.</summary>
    private static SWTrainingRow Post(string name, int assigned, int reserve, bool aimPlus = false) =>
        new(SWTrainingRowKind.Counter, name, $"{assigned}/2", aimPlus, assigned >= 2 || reserve <= 0, Buttons: MinusPlus,
            Current: assigned <= 0 ? 0 : -1, TargetIndex: aimPlus ? 1 : -1);

    private static SWTrainingFormPanel TabRow(string title, string[] tabs, int aim, int current = -1) => Form(title, Choice(tabs, current, aim));

    private static SWTrainingVisual MutationChart() => new(SWTrainingScene.Console, new[]
    {
        new SWTrainingOverlay(SWTrainingArt.ChartFragment, 150, 20, 1),
        new SWTrainingOverlay(SWTrainingArt.MarkMutation, 400, 90),
    });

    // ---- 08 ---------------------------------------------------------------

    private static SWTrainingFormPanel SecurityGauges(string threatStatus, int threat, string unrestStatus, int unrest, string? posture = null)
    {
        var last = posture == null ? Dim(string.Empty) : Txt($"Текущий: {posture}");
        return Form("Политика безопасности",
            Sec("Угроза - подрывная активность"),
            Txt($"{threatStatus}   {threat}/100"),
            Meter(threat / 100f, SWTrainingAccent.Bad),
            Sec("Накал - общественное недовольство"),
            Txt($"{unrestStatus}   {unrest}/100"),
            Meter(unrest / 100f, SWTrainingAccent.Neutral),
            last);
    }

    private static SWTrainingFormPanel SecurityPosture(int current, int aim = -1, bool pending = false) => Form("Политика безопасности",
        Sec("Режим безопасности"),
        Txt($"Текущий: {PostureLabels[current]}"),
        pending ? Warn($"Ждёт одобрения Канцлера: {PostureLabels[0]}") : Dim(string.Empty),
        Choice(PostureLabels, current, aim));

    private static SWTrainingFormPanel EmergencyRequest(bool aimApprove = false, bool aimReject = false) => Form(string.Empty,
        Bad($"Министр ВБ предлагает ввести режим: {PostureLabels[0]}."),
        Btn("Одобрить", aimApprove, accent: SWTrainingAccent.Bad),
        Btn("Отклонить", aimReject, accent: SWTrainingAccent.Neutral));

    public static SWTrainingStage[] SecurityThreats() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(MinistryConsole, SecurityTabs, 0)),
        Press(TabRow(MinistryConsole, SecurityTabs, 0)),
        Result(SecurityGauges("Умеренная", 40, "Напряжение", 35), note: "После вспышки лихорадки в Корусанте недовольство растёт, а подполье этим пользуется."));

    public static SWTrainingStage[] SecurityMode() => Steps(
        Aim(SecurityPosture(2, 1)),
        Press(SecurityPosture(2, 1)),
        Result(SecurityPosture(1), note: "Режим сменился. Угроза и накал от этого меняются постепенно."),
        Case("Министр может пойти дальше и объявить чрезвычайное положение.", SecurityPosture(1)),
        Aim(SecurityPosture(1, 0), true),
        Press(SecurityPosture(1, 0), true),
        Result(SecurityPosture(1, pending: true), note: "Чрезвычайное положение ждёт решения, если канцлер или спикер на месте.", alternative: true, continues: true),
        Result(SecurityPosture(0), note: "Если нет ни канцлера, ни спикера, режим меняется сразу.", alternative: true));

    public static SWTrainingStage[] SecurityRequest() => Steps(
        Back("Режим усилен, а затем министр запросил и чрезвычайное положение. Канцлер на месте: запрос ждёт его решения.", EmergencyRequest()),
        Aim(EmergencyRequest(aimApprove: true)),
        Press(EmergencyRequest(aimApprove: true)),
        Result(SecurityPosture(0), note: "Канцлер одобрил: в Республике чрезвычайное положение."),
        Case("Канцлер может и отказать в чрезвычайном положении.", EmergencyRequest()),
        Aim(EmergencyRequest(aimReject: true), true),
        Press(EmergencyRequest(aimReject: true), true),
        Result(SecurityPosture(1), note: "Запрос отклонён, режим остался прежним.", alternative: true, continues: true));

    public static SWTrainingStage[] SecurityWatch() => Steps(
        Back("Канцлер одобрил: в Республике чрезвычайное положение. Ночью на складах Утроста нашли оружейный тайник.", Picture(ChartView(), 3f)),
        Hold(SecurityGauges("Умеренная", 40, "Напряжение", 35), "Утром угроза была умеренной.", 4f),
        Result(SecurityGauges("Высокая", 58, "Напряжение", 40), note: "После находки угроза выросла, режим тот же. Подробности в делах и на карте."));

    public static SWTrainingStage[] SecurityOutcome() => Steps(
        Hold(SecurityGauges("Высокая", 58, "Напряжение", 40, PostureLabels[0]), "Чрезвычайное положение действует второй час.", 4f),
        Result(SecurityGauges("Высокая", 55, "Напряжение", 41, PostureLabels[0]),
            note: "Угроза снижается постепенно, а накал может расти. Выбранный режим сам по себе проблему не решает."));

    // ---- 09 ---------------------------------------------------------------

    private static SWTrainingFormPanel EconomyPicker(int good, bool aim = false, bool open = false) => Form("Программы и поставки",
        Choice(EconomyProgramTabs, 0),
        Sec("Промышленные расширения"),
        Choose(string.Empty, EconomyGoods, good, aim, open));

    private static SWTrainingFormPanel EconomyCandidate(string status, bool aim = false, bool available = true) => Form("Программы и поставки",
        Choice(EconomyProgramTabs, 0),
        Txt("Медицинские расходники: доступно 1 из 2"),
        Txt($"{LineName} — {LineWorld}, Корусант"),
        Dim("Стоимость: 1200 кр   Выпуск: +2 ед/ч"),
        Dim("Материалы: строительные модули 4 ед."),
        Dim("Работы: 3 ч после доставки"),
        Dim(status),
        Btn("Финансировать", aim, !available));

    private static SWTrainingFormPanel EconomyOrders(params SWTrainingRow[] rows)
    {
        var all = new SWTrainingRow[rows.Length + 1];
        all[0] = Choice(EconomyProgramTabs, 1);
        rows.CopyTo(all, 1);
        return new SWTrainingFormPanel("Программы и поставки", all);
    }

    private static SWTrainingRow[] LineProject(int progress, string status, string materials, string work) => new[]
    {
        Txt($"{LineName} / {LineWorld}   {progress}%"),
        Dim(status),
        Dim(materials),
        Dim(work),
    };

    private static SWTrainingFormPanel EconomyRoute(string status, bool aim = false, bool available = true) => Form("Программы и поставки",
        Choice(EconomyProgramTabs, 0),
        Sec("Транспортные модернизации"),
        Txt(RouteName),
        Dim("Стройплощадка: Дунидин"),
        Dim("Стоимость: 1800 кр"),
        Dim(status),
        Btn("Финансировать", aim, !available));

    public static SWTrainingStage[] EconomyFind() => Steps(
        HandOut(),
        HandIn(),
        Aim(EconomyPicker(0, true)),
        Press(EconomyPicker(0, true, true)),
        Hold(EconomyPicker(1, open: true)),
        Result(EconomyCandidate("Доступно"), note: "Новым клиникам Дунидина нужны расходники. Линия в самом Дунидине их и выпустит."));

    public static SWTrainingStage[] EconomyFund() => Steps(
        Aim(EconomyCandidate("Доступно", true)),
        Press(EconomyCandidate("Доступно", true)),
        Result(EconomyOrders(LineProject(0, "Заказ принят", "Строительные модули: 0 / 4 ед.", "Работы: 0 / 3 ч")), note: "1200 кр списаны, проект появился в поставках. Цеха ещё нет."),
        Result(EconomyCandidate("Недостаточно бюджета", available: false), note: "Если бюджета мало, кнопка отключена и проекта нет.", alternative: true));

    public static SWTrainingStage[] EconomyWait() => Steps(
        Back("Бюджета хватило: проект оплачен. Модули ещё на складах Корусанта.",
            EconomyOrders(LineProject(0, "Ожидает материалов", "Строительные модули: 0 / 4 ед.", "Работы: 0 / 3 ч"))),
        Hold(EconomyOrders(LineProject(25, "Материалы в пути", "Строительные модули: 2 / 4 ед.", "Работы: 0 / 3 ч")), "Половина модулей уже в Дунидине.", 4f),
        Result(EconomyOrders(LineProject(75, "Монтаж", "Строительные модули: 4 / 4 ед.", "Работы: 1,5 / 3 ч")), note: "Материалы на месте, идёт монтаж. Выпуск вырастет, когда он закончится."));

    public static SWTrainingStage[] EconomyRouteFund() => Steps(
        Aim(EconomyRoute("Доступно", true)),
        Press(EconomyRoute("Доступно", true)),
        Result(EconomyRoute("Расширение оплачено", available: false), note: "Чтобы расходники быстрее доходили до столицы, расширяется и маршрут. Он оплачивается отдельно и только один раз."),
        Result(EconomyRoute("Нет действующего узла для строительства", available: false), note: "Если строить негде, кнопка отключена.", alternative: true));

    public static SWTrainingStage[] EconomyBoth() => Steps(
        Back("Узел для строительства нашёлся, расширение маршрута оплачено. Оба проекта идут параллельно.", EconomyOrders(
            Txt($"{LineName} / {LineWorld}   75%"), Dim("Монтаж"),
            Txt($"Маршрут {RouteName} / {LineWorld}   25%"), Dim("Материалы в пути"))),
        Result(EconomyOrders(
            Txt($"{LineName} / {LineWorld}   100%"), Fine("Завершён"),
            Txt($"Маршрут {RouteName} / {LineWorld}   50%"), Dim("Монтаж")), note: "Линия готова и выпускает расходники. Маршрут ещё строится."));

    // ---- 10 ---------------------------------------------------------------

    private static SWTrainingFormPanel Monetary(string rate, int rateCurrent, string tax, int taxCurrent, int aimRate = -1, int aimTax = -1) => Form("Монетарка",
        Txt("Казна: 60/100   Рейтинг: A"),
        Dim("Под контролем   20/100, цены 100%"),
        Sec("Ключевая ставка"),
        Dim($"Текущая ставка: {rate}"),
        Choice(RateLabels, rateCurrent, aimRate),
        Sec("Налоговая политика"),
        Dim($"Текущая: {tax}"),
        Choice(TaxLabels, taxCurrent, aimTax));

    private const string RepayNone = "Погасить ближайший выпуск (−0 кр)";
    private const string RepayBond = "Погасить ближайший выпуск (−19 000 кр)";
    private const string IssueBond = "Выпустить облигации (+17 000 кр в казну, долг +15)";

    private static SWTrainingFormPanel Debt(int debt, string credit, string repay, bool aimIssue = false, bool aimRepay = false, bool canRepay = false) => Form("ДОЛГ",
        Sec("Государственный долг"),
        Txt($"Низкий   {debt}/100, рейтинг A"),
        Meter(debt / 100f, SWTrainingAccent.Neutral),
        Dim($"Долг по облигациям: {credit} кр"),
        Sec("Инструменты"),
        Btn(IssueBond, aimIssue),
        Btn(repay, aimRepay, !canRepay, SWTrainingAccent.Good));

    private static SWTrainingFormPanel EmissionOffer(bool aim = false) => Form("ДОЛГ",
        Sec("Денежная эмиссия"),
        Btn("Запросить эмиссию", aim, accent: SWTrainingAccent.Bad),
        Dim("Пополнение казны, рост инфляции, снижение рейтинга. Одобрение Канцлера."));

    private static SWTrainingFormPanel EmissionWaiting() => Form("ДОЛГ",
        Sec("Денежная эмиссия"),
        Warn("Ждёт одобрения Канцлера."));

    private static SWTrainingFormPanel EmissionRequest(bool aimApprove = false, bool aimReject = false) => Form(string.Empty,
        Bad("Министр финансов предлагает провести денежную эмиссию."),
        Btn("Одобрить", aimApprove, accent: SWTrainingAccent.Bad),
        Btn("Отклонить", aimReject, accent: SWTrainingAccent.Neutral));

    public static SWTrainingStage[] FinanceCheck() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(MinistryConsole, FinanceTabs, 0)),
        Press(TabRow(MinistryConsole, FinanceTabs, 0)),
        Result(Monetary("5%", -1, "Средние", 1), note: "Утро нового созыва: инфляция под контролем, рейтинг A. Казна, ставка и налог на одной вкладке."));

    public static SWTrainingStage[] FinanceParameters() => Steps(
        Aim(Monetary("5%", -1, "Средние", 1, 2)),
        Press(Monetary("5%", -1, "Средние", 1, 2)),
        Result(Monetary("6%", 2, "Средние", 1), note: "Ставка изменилась, налог остался прежним."),
        Aim(Monetary("6%", 2, "Средние", 1, aimTax: 2)),
        Press(Monetary("6%", 2, "Средние", 1, aimTax: 2)),
        Result(Monetary("6%", 2, "Высокие", 2), note: "Налог меняется отдельным нажатием. Текущая кнопка отключена."));

    public static SWTrainingStage[] FinanceBonds() => Steps(
        Aim(Debt(0, "0", RepayNone, aimIssue: true)),
        Press(Debt(0, "0", RepayNone, aimIssue: true)),
        Result(Debt(15, "19 000", RepayBond, canRepay: true), note: "В казну поступило 17 000 кр, вернуть нужно 19 000 кр. Чем выше рейтинг, тем выгоднее выпуск."));

    public static SWTrainingStage[] FinanceRepay() => Steps(
        Aim(Debt(15, "19 000", RepayBond, aimRepay: true, canRepay: true)),
        Press(Debt(15, "19 000", RepayBond, aimRepay: true, canRepay: true)),
        Result(Debt(0, "0", RepayNone), note: "Выпуск погашен, 19 000 кр ушли из казны. Долга больше нет."),
        Result(Debt(15, "19 000", RepayBond), note: "Если в казне меньше нужной суммы, кнопка отключена и долг остаётся.", alternative: true));

    public static SWTrainingStage[] FinanceEmission() => Steps(
        Back("Денег в казне хватило: выпуск погашен, долга нет.", EmissionOffer()),
        Aim(EmissionOffer(true)),
        Press(EmissionOffer(true)),
        Hold(EmissionWaiting()),
        Aim(EmissionRequest(aimApprove: true)),
        Press(EmissionRequest(aimApprove: true)),
        Result(Form("Монетарка", Txt("Казна пополнена"), Warn("Высокая   45/100, цены 125%"), Sec("События"), Dim("Денежная эмиссия: казна полна, инфляция подскочила.")),
            note: "Канцлер одобрил: казна полна, но инфляция подскочила."),
        Case("Канцлер может и отказать в эмиссии.", EmissionRequest()),
        Aim(EmissionRequest(aimReject: true), true),
        Press(EmissionRequest(aimReject: true), true),
        Result(EmissionOffer(), note: "Эмиссия отклонена: казна и инфляция остались прежними, а министр может запросить снова.", alternative: true, continues: true));

    // ---- 11 ---------------------------------------------------------------

    private const string InstitutionName = "Районная больница Дунидина";
    private const string CaseName = "Закупки бакты по двойной цене";
    private const string FirstCheck = "Сверка счетов";
    private const string SecondCheck = "Опрос персонала";

    private static SWTrainingFormPanel Institution(bool open, bool aim = false) => Form("Проверки",
        Sec("Учреждения"),
        Txt("Дунидин"),
        Txt(InstitutionName),
        open ? Btn("Перейти к делу") : Dim("Больница"),
        Btn("Открыть проверку", aim, open),
        open ? Warn("Уже проводится проверка") : Dim(string.Empty),
        Dim(open ? "Дела: 1/4   Проверки: 0/2" : "Дела: 0/4   Проверки: 0/2"));

    private static SWTrainingFormPanel CheckList(string first, string? second, bool aim = false, string? reason = null)
    {
        var material = first == "Завершена" ? Btn($"Открыть: {FirstCheck}") : Dim(string.Empty);
        // As in the game, only a check not yet started has the button.
        var run = second != null ? Dim(string.Empty) : Btn("Провести проверку", aim, reason != null);
        return Form("Дела",
            Txt(CaseName),
            Sec("Проверки"),
            Dim($"{FirstCheck}: {first}"),
            Dim($"{SecondCheck}: {second ?? "300 кр, 2 ч 00 мин"}"),
            run,
            reason == null ? Sec("Материалы") : Warn(reason),
            material);
    }

    private static SWTrainingFormPanel ConclusionForm(bool firstOn, bool secondOn, int conclusion, int aimCheck = -1, int aimConclusion = -1, bool aimSign = false)
    {
        var reason = conclusion < 0 ? "Не выбрано заключение" : !(firstOn && secondOn) ? "Нужно выбрать два материала" : string.Empty;
        return Form("Дела",
            Sec("Материалы"),
            Btn($"Открыть: {FirstCheck}"),
            Check("Использовать в заключении", firstOn, aimCheck == 0),
            Btn($"Открыть: {SecondCheck}"),
            Check("Использовать в заключении", secondOn, aimCheck == 1),
            Sec("Заключение"),
            Choice(Conclusions, conclusion, aimConclusion),
            Btn("Подписать заключение", aimSign, reason.Length > 0),
            Dim(reason));
    }

    private static SWTrainingFormPanel Remedies(bool aim = false) => Form("Дела",
        Txt("Заключение: Халатность"),
        Sec("Мера"),
        Txt("Исправить документы: 200 кр, 1 ч 00 мин"),
        Btn("Применить меру", aim),
        Txt("Возместить ущерб: 350 кр, 2 ч 00 мин"),
        Btn("Применить меру"));

    private static SWTrainingFormPanel RemedyRunning(bool done, bool aim = false) => Form("Дела",
        Txt("Заключение: Халатность"),
        Sec("Мера"),
        Txt("Исправить документы"),
        Btn("Проверить исполнение", aim, !done),
        done ? Dim(string.Empty) : Dim("Ожидание: 1 ч 00 мин"));

    private static SWTrainingFormPanel CaseArchive(string result, string actual) => Form("Архив",
        Sec("Результат"),
        Txt(result),
        Txt("Заключение: Халатность"),
        Txt($"Установлено: {actual}"));

    public static SWTrainingStage[] JusticeInstitution() => Steps(
        HandOut(),
        HandIn(),
        Aim(Institution(false, true)),
        Press(Institution(false, true)),
        Result(Institution(true), note: "Больница закупает бакту вдвое дороже соседей. Дело открыто на вкладке «Дела»."));

    public static SWTrainingStage[] JusticeChecks() => Steps(
        Aim(CheckList("Завершена", null, true)),
        Press(CheckList("Завершена", null, true)),
        Hold(CheckList("Завершена", "Выполняется: 2 ч 00 мин"), "Счета уже сверены. Опрос персонала займёт два часа.", 4f),
        Result(CheckList("Завершена", "Завершена"), note: "Опрос закончен, второй материал получен."),
        Result(CheckList("Завершена", null, reason: "Все специалисты заняты"), note: "Если свободного специалиста нет, проверку не начать.", alternative: true));

    public static SWTrainingStage[] JusticeConclude() => Steps(
        Back("Свободный специалист нашёлся: счета сверены, персонал опрошен, оба материала собраны.", CheckList("Завершена", "Завершена")),
        Aim(ConclusionForm(false, false, -1, 0)),
        Press(ConclusionForm(false, false, -1, 0)),
        Aim(ConclusionForm(true, false, -1, 1)),
        Press(ConclusionForm(true, false, -1, 1)),
        Aim(ConclusionForm(true, true, -1, aimConclusion: 1)),
        Press(ConclusionForm(true, true, -1, aimConclusion: 1)),
        Aim(ConclusionForm(true, true, 1, aimSign: true)),
        Press(ConclusionForm(true, true, 1, aimSign: true)),
        Result(Remedies(), note: "Заключение подписано, дело перешло к мере. Верен ли вывод, покажет повторная проверка."));

    public static SWTrainingStage[] JusticeRemedy() => Steps(
        Aim(Remedies(true)),
        Press(Remedies(true)),
        Result(RemedyRunning(false), note: "Мера назначена и оплачена. Она ещё не завершена."));

    public static SWTrainingStage[] JusticeFollowUp() => Steps(
        Hold(RemedyRunning(true)),
        Aim(RemedyRunning(true, true)),
        Press(RemedyRunning(true, true)),
        Result(CaseArchive("Заключение подтверждено", "Халатность"), note: "Больница действительно переплачивала по небрежности. Вывод верный."),
        Result(CaseArchive("Повторная проверка выявила ошибочное заключение", "Нарушений нет"), note: "Если вывод был неверным, доверие к ведомству падает.", alternative: true));

    // ---- 12 ---------------------------------------------------------------

    private static SWTrainingFormPanel HealthWorld(string infection) => Form("Медицинская сеть",
        Txt("Дунидин"),
        Warn($"Заражение {infection}"),
        Dim("Иммунитет 3%"),
        Dim("Охват 40%"));

    private static SWTrainingFormPanel HealthSectionChoice() => Form("Медицинская сеть",
        Txt("Дунидин   бюджет 3000 кр"),
        Choice(HealthSections, 0, 1));

    private static SWTrainingFormPanel HealthDevelopment(string reserve, bool aimAid = false, bool canAid = true) => Form("Медицинская сеть",
        Txt("Дунидин   бюджет 3000 кр"),
        Choice(HealthSections, 1),
        Sec("Медицинская помощь"),
        Txt($"Запас помощи: {reserve}"),
        Btn("Заказать за 750 кр", aimAid, !canAid));

    /// <summary>A delivered order has left the list of orders in work, so the list is empty until the other tab opens.</summary>
    private static SWTrainingFormPanel HealthSupplyEmpty() => Form("Медицинская сеть",
        Choice(HealthSections, 2),
        Choice(SupplyTabs, 0, 1),
        Dim("Нет выполняемых заявок в этом мире"));

    private static SWTrainingFormPanel HealthSupply(int tab, string status, int progress, string? material = null, bool completed = false)
    {
        var body = completed ? Fine(status) : material == null ? Dim(string.Empty) : Dim(material);
        return Form("Медицинская сеть",
            Choice(HealthSections, 2),
            Choice(SupplyTabs, tab),
            Item("Медицинская помощь", status, true),
            Dim("Получатель: Дунидин"),
            Meter(progress / 100f),
            body);
    }

    private const string AidMaterial = "Лекарственные препараты, получено: 0/0,25";

    public static SWTrainingStage[] HealthFind() => Steps(
        HandOut(),
        HandIn(),
        Picture(ChartView(1), 3f, ScreenNote("Риммианская лихорадка вышла из Корусанта.")),
        Result(HealthWorld("12%"), note: "В Дунидине заражён каждый восьмой. Заражение выше 1% отмечает очаг."));

    public static SWTrainingStage[] HealthPick() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Console, new[] { new SWTrainingOverlay(SWTrainingArt.PlanetNeutralStone, 328, 62) }), 3f, ScreenNote("Дунидин, нижние уровни.")),
        Hold(HealthWorld("12%")),
        Aim(HealthSectionChoice()),
        Press(HealthSectionChoice()),
        Result(HealthDevelopment("2 ч"), note: "Запаса помощи в Дунидине осталось на два часа. Заказа ещё нет."));

    /// <summary>
    /// The order goes through the supply list: it starts there, stays alone for this world, and its delivery has no promised date.
    /// The button itself stays enabled in the game, so the refusals are shown by the missing second order.
    /// </summary>
    public static SWTrainingStage[] HealthOrder() => Steps(
        Aim(HealthDevelopment("2 ч", true)),
        Press(HealthDevelopment("2 ч", true)),
        Result(HealthSupply(0, "Заказ принят", 0), note: "Оплачено 750 кр. Заявка появилась в разделе «Снабжение», помощь ещё в пути."),
        Result(HealthSupply(0, "Заказ принят", 0), note: "Повторное нажатие не даст второй заявки, пока эта не доставлена.", alternative: true),
        Result(HealthDevelopment("4 ч"), note: "При запасе от 3,5 ч новый заказ не примут.", alternative: true),
        Result(HealthDevelopment("2 ч", canAid: false), note: "Если денег мало или рабочий день окончен, кнопка отключена.", alternative: true));

    public static SWTrainingStage[] HealthDelivery() => Steps(
        Back("Принят один заказ помощи для Дунидина. Лекарства собирают на складах; когда они придут, зависит от снабжения.",
            HealthSupply(0, "Ожидание материалов", 0, AidMaterial)),
        Result(HealthSupply(0, "Снабжение ожидает транспорт", 0, AidMaterial), note: "Заявка ждёт транспорт. Запас помощи пока прежний."),
        Aim(HealthSupplyEmpty()),
        Press(HealthSupplyEmpty()),
        Result(HealthSupply(1, "Доставлено", 100, completed: true), note: "После доставки заявка перешла в «Завершено»."),
        Result(HealthSupply(0, "Снабжение остановлено: маршрут недоступен", 0, AidMaterial), note: "Если маршрут недоступен, поставка стоит.", alternative: true));

    public static SWTrainingStage[] HealthEffect() => Steps(
        Back("Маршрут был открыт, и помощь доставлена. До доставки запас был на два часа.", HealthDevelopment("2 ч")),
        Result(HealthDevelopment("6 ч"), note: "Доставка добавила 4 ч запаса, не больше 8 ч всего."),
        Result(HealthWorld("15%"), note: "Запас вырос, но заражение могло вырасти тоже. Одного заказа может быть мало."));

    // ---- 12А --------------------------------------------------------------

    private static SWTrainingFormPanel Research(int stage, string status, params SWTrainingRow[] rows)
    {
        var all = new SWTrainingRow[rows.Length + 3];
        all[0] = Choice(ResearchStages, stage);
        all[1] = Dim("Риммианская лихорадка   штамм 1   бюджет 3000 кр");
        all[2] = Txt(status);
        rows.CopyTo(all, 3);
        return new SWTrainingFormPanel("Исследование", all);
    }

    private static SWTrainingFormPanel Samples(int count, int world, bool aim = false, bool canTake = true)
    {
        var source = count == 0 ? "не выбран" : SampleWorlds[0];
        return Research(0, count == 0 ? "Выберите источник наблюдений" : "Сбор наблюдений",
            Dim($"Образцов: {count}   источник: {source}"),
            Choose("Очаг для отбора", SampleWorlds, world),
            Btn("Получить образец", aim, !canTake));
    }

    private static SWTrainingFormPanel Funding(int stage, string status, string line, int current, int aim = -1) =>
        Research(stage, status, Dim(line), Choice(FundingLabels, current, aim));

    private static SWTrainingFormPanel Candidates(bool aim = false) => Form("Исследование",
        Choice(ResearchStages, 2),
        Txt("Узкий препарат"),
        Dim("Быстрее и дешевле; действие сильнее зависит от текущего штамма."),
        Btn("Выбрать"),
        Txt("Устойчивый препарат"),
        Dim("Больше времени и ресурсов; лучше сохраняет действие при мутации."),
        Btn("Выбрать", aim));

    private static SWTrainingFormPanel Trials(string status, string observations, string button, bool aim = false, bool enabled = false, string? warn = null) =>
        Research(3, status,
            Dim($"Лаборатория: Корусант   наблюдений: {observations}"),
            warn == null ? Dim(string.Empty) : Warn(warn),
            Choose("Медицинская база", LabWorlds, 0),
            Btn(button, aim, !enabled));

    private static SWTrainingFormPanel Production(string cost, int batch, int aimBatch = -1, bool aimOrder = false, bool canOrder = true) => Form("Исследование",
        Choice(ResearchStages, 4),
        Choose("Производитель", Producers, 0),
        Choose("Получатель", Recipients, 0),
        Dim("Охват партии: 8% населения"),
        Choice(BatchLabels, batch, aimBatch),
        Btn($"Заказать за {cost} кр", aimOrder, !canOrder));

    private static SWTrainingFormPanel Delivery() => Research(5, "Производство и доставка",
        Txt("Вакцина, Квенк в Дунидин, штамм 1"),
        Dim("Ожидание материалов"),
        Meter(0f));

    private static SWTrainingFormPanel Mutated(bool aim = false) => Form("Исследование",
        Choice(ResearchStages, 0),
        Warn("Штамм изменился. Эффективность текущей партии снижена."),
        Btn("Получить образец нового штамма", aim));

    public static SWTrainingStage[] VaccineSamples() => Steps(
        Picture(ChartView(1), 3f, ScreenNote("Лихорадка держится в Дунидине и Квенке.")),
        Aim(Samples(0, 0, true)),
        Press(Samples(0, 0, true)),
        Result(Samples(1, 0), note: "Первая проба из Дунидина, идёт сбор наблюдений."),
        Aim(Samples(1, 1, true)),
        Press(Samples(1, 1, true)),
        Result(Samples(2, 1, canTake: false), note: "Вторая проба с другого мира. Больше двух не берут."),
        Result(Samples(1, 0, canTake: false), note: "Второй раз из того же мира пробу не взять.", alternative: true));

    public static SWTrainingStage[] VaccineFunding() => Steps(
        Back("Пробы взяты с двух миров, из Дунидина и Квенка. На паузе исследование не идёт.",
            Funding(0, "Сбор наблюдений", "Этап: Образцы   0%   Ожидание поставок или возобновления   0 кр/ч", 0)),
        Aim(Funding(0, "Сбор наблюдений", "Этап: Образцы   0%   Ожидание поставок или возобновления   0 кр/ч", 0, 1)),
        Press(Funding(0, "Сбор наблюдений", "Этап: Образцы   0%   Ожидание поставок или возобновления   0 кр/ч", 0, 1)),
        Hold(Funding(0, "Сбор наблюдений", "Этап: Образцы   40%   0,3 активных ч   60 кр/ч", 1), "Работа идёт, бюджет расходуется по 60 кр в час.", 4f),
        Result(Funding(1, "Анализ данных", "Этап: Анализ   50%   0,3 активных ч   60 кр/ч", 1), note: "Кандидат появится только после анализа."));

    public static SWTrainingStage[] VaccineCandidate() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Lab, new[] { new SWTrainingOverlay(SWTrainingArt.LabVials, SWSenateTrainingArt.VialsX, SWSenateTrainingArt.VialsY, 0, true) }), 3f),
        Hold(Candidates(), "Лихорадка меняется быстро, поэтому выбран устойчивый препарат.", 4f),
        Aim(Candidates(true)),
        Press(Candidates(true)),
        Result(Trials("Готов к испытаниям", "0", "Начать испытания за 450 кр", enabled: true), note: "Препарат выбран, исследование перешло к испытаниям. Вакцины ещё нет."));

    public static SWTrainingStage[] VaccineTrials() => Steps(
        Aim(Trials("Готов к испытаниям", "0", "Начать испытания за 450 кр", true, true)),
        Press(Trials("Готов к испытаниям", "0", "Начать испытания за 450 кр", true, true)),
        Hold(Trials("Ожидание материалов лаборатории", "0", "Испытания запущены"), "Деньги списаны, лаборатории нужны материалы.", 4f),
        Hold(Trials("Испытания идут", "3", "Испытания запущены"), "Материалы пришли, наблюдения копятся.", 4f),
        Result(Trials("Испытания завершены", "7", "Испытания завершены"), note: "Теперь вакцину можно заказать."),
        Result(Trials("Готов к испытаниям", "0", "Начать испытания за 450 кр", warn: "Недостаточно средств: требуется 450 кр, доступно 200 кр"),
            note: "Если денег мало, испытания не начать.", alternative: true),
        Result(Production("1600", 0, canOrder: false), note: "Пока испытания не завершены, кнопка заказа отключена.", alternative: true));

    public static SWTrainingStage[] VaccineOrder() => Steps(
        Back("Денег хватило, испытания завершены. Квенк может выпускать вакцину и ближе всех к Дунидину.", Production("1600", 0)),
        Aim(Production("1600", 0, 1)),
        Press(Production("1600", 0, 1)),
        Aim(Production("3200", 1, aimOrder: true)),
        Press(Production("3200", 1, aimOrder: true)),
        Result(Delivery(), note: "Заказ оплачен, партии ждут материалов и производства."),
        Result(Production("1600", 0), note: "Пока у Дунидина есть активная поставка, новый заказ для него не примут.", alternative: true));

    public static SWTrainingStage[] VaccineCurrency() => Steps(
        Back("Две партии вакцины для Дунидина в работе. Через день лихорадка мутировала.", Picture(MutationChart(), 4f)),
        Hold(Mutated(), "Партии в пути доедут, но подойдут хуже.", 4f),
        Aim(Mutated(true)),
        Press(Mutated(true)),
        Result(Samples(1, 0), note: "Новая проба перезапускает исследование с этапа образцов."));

    // ---- 13 ---------------------------------------------------------------

    private const string CallName = "Авария на верфи - Корусант";
    private const string CallText = "Разгерметизация дока: десятки рабочих с баротравмами.";
    private const string BasicCall = "Запасы -10. Успех зависит от готовности и запасов (сейчас ~87%).";
    private const string ReinforcedCall = "Запасы -15, гарантированный успех.";
    private const string ReinforcedButton = "Усиленный выезд  (500 кр)";

    private static SWTrainingFormPanel CallCard(string timeLeft, string? extra = null) => Form("Вызовы на линии",
        Txt(CallName),
        Dim(CallText),
        Warn($"Вызов сгорит через ~{timeLeft} ч"),
        extra == null ? Dim(string.Empty) : Dim(extra));

    private static SWTrainingFormPanel CallOptions(bool aimBasic = false, bool aimReinforced = false, bool canBasic = true, bool canReinforced = true, string? line = null) => Form("Вызовы на линии",
        Txt(CallName),
        Warn("Вызов сгорит через ~1.4 ч"),
        Dim(BasicCall),
        Btn("Отправить бригаду", aimBasic, !canBasic),
        Dim(ReinforcedCall),
        Btn(ReinforcedButton, aimReinforced, !canReinforced),
        line == null ? Dim(string.Empty) : Warn(line));

    private static SWTrainingFormPanel CareWork(int readiness, string readinessStatus, int supplies, string suppliesStatus, int free, int away) => Form("Пульт департамента",
        Txt($"Готовность бригад: {readiness}/100 - {readinessStatus}"),
        Meter(readiness / 100f),
        Txt($"Запасы медикаментов: {supplies}/100 - {suppliesStatus}"),
        Meter(supplies / 100f, SWTrainingAccent.Neutral),
        Fine($"Бригады: свободно {free}/3, на выезде: {away}"));

    private static SWTrainingFormPanel CareJournal(params string[] lines)
    {
        var all = new SWTrainingRow[lines.Length + 1];
        all[0] = Sec("Журнал департамента");
        for (var i = 0; i < lines.Length; i++)
            all[i + 1] = Dim(lines[i]);
        return new SWTrainingFormPanel(string.Empty, all);
    }

    public static SWTrainingStage[] CareOpen() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(DepartmentConsole, HealthcareTabs, 1)),
        Press(TabRow(DepartmentConsole, HealthcareTabs, 1)),
        Result(CallCard("2.0"), note: "На верфи Корусанта разгерметизировался док. На решение два часа."));

    public static SWTrainingStage[] CareDeadline() => Steps(
        Hold(CallCard("1.4"), "Прошло полчаса.", 4f),
        Result(CallCard("0.3"), note: "Срок идёт и без вашего решения."),
        Result(CareJournal("[11:00] Авария на верфи: вызов сгорел без ответа (Корусант)."), note: "Если срок истёк, вызов исчезает, а последствия видны в журнале.", alternative: true));

    public static SWTrainingStage[] CareCompare() => Steps(
        Back("Вызов ещё в силе: он сгорит примерно через 1.4 ч.", CallOptions()),
        Aim(CallOptions(aimBasic: true)),
        Hold(CallOptions(aimBasic: true), "Вероятность меняется вместе с готовностью и запасами.", 4f),
        Aim(CallOptions(aimReinforced: true)),
        Result(CallOptions(aimReinforced: true), note: "Обычный выезд дешевле, но успех не гарантирован. Усиленный надёжен и дороже."));

    public static SWTrainingStage[] CareReinforced() => Steps(
        Result(CallOptions(), note: "Успех усиленного выезда гарантирован, но он берёт больше запасов и 500 кр ведомства."),
        Result(CallOptions(canReinforced: false, line: "Бюджет: 400 кр"), note: "Если денег не хватает, усиленный выезд отключён.", alternative: true),
        Result(CallOptions(canBasic: false, canReinforced: false, line: "Бригады: свободно 0/3, на выезде: 3"), note: "Если свободной бригады нет, обе кнопки отключены.", alternative: true));

    public static SWTrainingStage[] CareDispatch() => Steps(
        Back("Денег и бригад хватает: все три бригады на базе.", CareWork(80, "Бригады рвутся в бой", 70, "Склады полны", 3, 0)),
        Aim(CallOptions(aimBasic: true)),
        Press(CallOptions(aimBasic: true)),
        Result(CareWork(72, "Бригады рвутся в бой", 60, "Склады полны", 2, 1), note: "Запасы и готовность снизились, бригада занята на игровой час."));

    public static SWTrainingStage[] CareResult() => Steps(
        Hold(CareWork(72, "Бригады рвутся в бой", 60, "Склады полны", 2, 1), hold: 4f),
        Result(CareWork(75, "Бригады рвутся в бой", 60, "Склады полны", 3, 0), note: "Выезд завершён, бригада вернулась."),
        Result(CareJournal("[10:00] Авария на верфи: бригада справилась (Корусант).", "[09:00] Бригада вылетела: Авария на верфи (Корусант)."), note: "Рабочих верфи спасли."),
        Result(CareJournal("[10:00] Авария на верфи: выезд прошёл тяжело, потери среди пострадавших.", "[09:00] Бригада вылетела: Авария на верфи (Корусант)."),
            note: "Обычный выезд может закончиться и так.", alternative: true));

    // ---- 14 ---------------------------------------------------------------

    private static SWTrainingFormPanel GuardWork(int total, int reserve, int hall = 0, int corridors = 0, bool aimPlus = false) => Form("Пульт департамента",
        Txt($"Отрядов всего: {total}, в резерве: {reserve}"),
        Sec("Посты гвардии"),
        Post("Зал заседаний, порядок на трибуне: Мораль", hall, reserve),
        Post("Коридоры Сената, патрули: Полиция, гасят волнения", corridors, reserve, aimPlus),
        Post("Доки станции, досмотр грузов: Преступность вниз", 0, reserve),
        Post("Архив и серверные, охрана тайн: Контрразведка", 0, reserve));

    private static SWTrainingFormPanel GuardRecruit(string button, bool aim = false, bool available = true) => Form("Экспресс-операции",
        Txt("Набор рекрутов   1200 кр"),
        Dim("Сформировать новый отряд гвардии (до 6)."),
        Btn(button, aim, !available, SWTrainingAccent.Good));

    private static SWTrainingFormPanel Writ(int reserve, bool aimOne = false, string timeLeft = "2.0") => Form("Судебные ордера",
        Txt("Конвой заключённого"),
        Dim("Доставить обвиняемого из блока временного содержания в зал суда."),
        Warn($"Ордер сгорит через ~{timeLeft} ч"),
        Dim("Успех ~74%."),
        Btn("Направить 1 отряд", aimOne, reserve < 1),
        Dim("Успех ~95%."),
        Btn("Направить 2 отряда", disabled: reserve < 2));

    private static SWTrainingFormPanel GuardJournal(int total, int reserve, params string[] lines)
    {
        var all = new SWTrainingRow[lines.Length + 2];
        all[0] = Txt($"Отрядов всего: {total}, в резерве: {reserve}");
        all[1] = Sec("Журнал департамента");
        for (var i = 0; i < lines.Length; i++)
            all[i + 2] = Dim(lines[i]);
        return new SWTrainingFormPanel(string.Empty, all);
    }

    public static SWTrainingStage[] GuardStart() => Steps(
        HandOut(),
        HandIn(),
        Result(GuardWork(4, 4), note: "Четыре отряда и четыре поста в начале созыва. Посты пока пусты, весь состав в резерве."),
        Aim(TabRow(DepartmentConsole, GuardTabs, 2)),
        Press(TabRow(DepartmentConsole, GuardTabs, 2)),
        Aim(GuardRecruit("Выполнить", true)),
        Press(GuardRecruit("Выполнить", true)),
        Result(GuardWork(5, 5), note: "Набор оплачен из бюджета, отряд сформирован. Следующий набор будет доступен позже."),
        Result(GuardRecruit("штат укомплектован", available: false), note: "Когда отрядов шесть, набор закрыт.", alternative: true));

    public static SWTrainingStage[] GuardPost() => Steps(
        Back("Новый отряд набран: всего пять, все в резерве, посты пусты.", GuardWork(5, 5)),
        Aim(GuardWork(5, 5, aimPlus: true)),
        Press(GuardWork(5, 5, aimPlus: true)),
        Result(GuardWork(5, 4, corridors: 1), note: "Отряд ушёл на пост: всего столько же, резерв меньше."),
        Aim(GuardWork(5, 4, corridors: 1, aimPlus: true)),
        Press(GuardWork(5, 4, corridors: 1, aimPlus: true)),
        Result(GuardWork(5, 3, corridors: 2), note: "На посту два отряда, кнопка «+» у него отключена."),
        Result(GuardWork(4, 0, hall: 2, corridors: 2), note: "Если бы отрядов было четыре и два из них уже стояли в зале, резерв опустел бы и кнопки «+» отключились бы на всех постах.", alternative: true));

    public static SWTrainingStage[] GuardWrit() => Steps(
        Back("В коридорах стоят два отряда, в зале никого. В резерве три.", GuardWork(5, 3, corridors: 2)),
        Aim(TabRow(DepartmentConsole, GuardTabs, 1)),
        Press(TabRow(DepartmentConsole, GuardTabs, 1)),
        Result(Writ(3), note: "Суд ждёт обвиняемого. В резерве три отряда, поэтому доступны оба варианта."));

    public static SWTrainingStage[] GuardDispatch() => Steps(
        Aim(Writ(3, true, "1.6")),
        Press(Writ(3, true, "1.6")),
        Result(GuardJournal(5, 2, "[09:00] Гвардия выдвинулась: Конвой заключённого (1 отр.)."), note: "Отряд ушёл из резерва на игровой час."),
        Result(Writ(0), note: "Без резерва обе кнопки отключены, и ордер ждёт.", alternative: true));

    public static SWTrainingStage[] GuardResult() => Steps(
        Back("Резерв был, и один отряд ушёл с конвоем. В резерве остались два.",
            GuardJournal(5, 2, "[09:00] Гвардия выдвинулась: Конвой заключённого (1 отр.).")),
        Result(GuardJournal(5, 3, "[10:00] Конвой заключённого: исполнено. Суды разгружены, взыскано 300 кр.", "[09:00] Гвардия выдвинулась: Конвой заключённого (1 отр.)."), note: "Обвиняемый доставлен, отряд вернулся в резерв."),
        Result(GuardJournal(5, 3, "[10:00] Конвой заключённого: провал. Гвардия опозорилась.", "[09:00] Гвардия выдвинулась: Конвой заключённого (1 отр.)."),
            note: "Отряд может вернуться и ни с чем.", alternative: true),
        Result(GuardJournal(5, 3, "[11:00] Конвой заключённого: ордер просрочен без исполнения."), note: "Если за два часа ордер не взяли в работу, он сгорает.", alternative: true));
}
