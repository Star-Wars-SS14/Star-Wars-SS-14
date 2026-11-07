using System;

namespace Content.Client._SW._Senate.Training;

/// <summary>
/// Timelines of chapters 15 to 20: the arbitration and expenditure departments, the three bureaus and the chief justice.
/// Labels, log lines and costs come from the department, bureau and judicial consoles and their server systems. Cases,
/// ships, events and outcomes are drawn by the game at random, so every concrete one here is an example and marked so.
/// </summary>
public static partial class SWTrainingSamples
{
    private const string BureauConsole = "Рабочая консоль бюро";

    // One array per set of buttons: rows compare them by reference.
    private static readonly string[] ExpenditureTabs = { "Работа", "Сигналы", "Операции", "Заявки" };
    private static readonly string[] ShipsTabs = { "Стол регистрации", "Флот и розыск", "Операции", "Заявки" };
    private static readonly string[] WeaponsTabs = { "Арсенал", "Лицензии и дроиды", "Операции", "Заявки" };
    private static readonly string[] TransitTabs = { "Сеть перевозок", "События на линиях", "Операции", "Заявки" };
    private static readonly string[] ShipButtons = { "Одобрить (+300)", "Досмотр (400)", "Отказать" };
    private static readonly string[] ShipButtonsInspected = { "Одобрить (+300)", "Проверено", "Отказать" };
    private static readonly string[] TransitModes = { "Открыт", "Визы", "Кордон" };
    private static readonly string[] CourtTabs = { "Дело", "Реестр", "Законы", "Состав" };
    private static readonly string[] CaseViews = { "Материалы дела", "Решение суда" };
    private static readonly string[] VerdictButtons = { "В пользу истца", "В пользу ответчика" };

    private static SWTrainingFormPanel Journal(string title, params string[] lines)
    {
        var all = new SWTrainingRow[lines.Length + 1];
        all[0] = Sec(title);
        for (var i = 0; i < lines.Length; i++)
            all[i + 1] = Dim(lines[i]);
        return new SWTrainingFormPanel(string.Empty, all);
    }

    // ---- 15 ---------------------------------------------------------------

    private const string ShipyardCase = "Приписки на верфях";
    private const string CasinoCase = "Казино-лайнер «Фортуна хатта»";
    private static readonly string[] ArbStages = { "Зацепка", "Улики", "Арест" };
    private static readonly int[] ArbCosts = { 500, 800, 1200 };

    private static string ArbAdvance(int stage) => stage == 2 ? "Провести арест" : $"Собрать: {ArbStages[stage + 1].ToLowerInvariant()}";

    /// <summary>The game's collapse chance: 40 minus half the independence, cut to whole percent, between 5 and 50.</summary>
    private static int ArbRisk(int independence) => Math.Clamp((int) (40f - independence / 2f), 5, 50);

    private static SWTrainingFormPanel ArbCase(string name, int stage, int independence, bool aim = false, bool canPay = true) => Form("Пульт департамента",
        Txt($"Независимость: {independence}/100 - Держим удар"),
        Sec("Дела в производстве"),
        Txt(name),
        Dim($"стадия {stage + 1}/3: {ArbStages[stage]}"),
        Meter((stage + 1) / 3f),
        Warn($"риск давления сверху: {ArbRisk(independence)}%"),
        Btn($"{ArbAdvance(stage)}  ({ArbCosts[stage]} кр)", aim, !canPay, SWTrainingAccent.Good));

    public static SWTrainingStage[] ArbitrageOpen() => Steps(
        HandOut(),
        HandIn(),
        Result(ArbCase(ShipyardCase, 0, 60), note: "Дело на первой стадии из трёх. Дела приходят сами, их набор зависит от раунда. " + Example));

    public static SWTrainingStage[] ArbitragePay() => Steps(
        Aim(ArbCase(ShipyardCase, 0, 60, true)),
        Press(ArbCase(ShipyardCase, 0, 60, true)),
        Result(ArbCase(ShipyardCase, 1, 61), note: "Оплачено 500 кр, дело перешло на вторую стадию."),
        Result(ArbCase(ShipyardCase, 1, 61, canPay: false), note: "Другой случай: денег меньше 800 кр, кнопка отключена, стадия прежняя.", alternative: true));

    public static SWTrainingStage[] ArbitragePressure() => Steps(
        Result(ArbCase(CasinoCase, 0, 61), note: "Риск показан на карточке до оплаты. Он зависит от независимости департамента."),
        Aim(ArbCase(CasinoCase, 0, 61, true), true),
        Press(ArbCase(CasinoCase, 0, 61, true), true),
        Result(Journal("Журнал департамента", $"[11:00] «{CasinoCase}»: дело развалено давлением сверху."),
            note: "Другой исход: деньги потрачены, дело закрыто, независимость снизилась. Продолжить его нельзя.", alternative: true));

    public static SWTrainingStage[] ArbitrageContinue() => Steps(
        Aim(ArbCase(ShipyardCase, 1, 61, true)),
        Press(ArbCase(ShipyardCase, 1, 61, true)),
        Result(ArbCase(ShipyardCase, 2, 62), note: "Другое дело дошло до третьей стадии. Риск остаётся на каждой оплате."));

    public static SWTrainingStage[] ArbitrageResult() => Steps(
        Aim(ArbCase(ShipyardCase, 2, 62, true)),
        Press(ArbCase(ShipyardCase, 2, 62, true)),
        Result(Journal("Журнал департамента", $"[12:00] «{ShipyardCase}»: арест! Штраф 3500 кр взыскан."),
            note: "Штраф поступил в бюджет департамента, независимость выросла. Сумма своя у каждого дела."));

    // ---- 16 ---------------------------------------------------------------

    private const string AuditedMinistry = "Министерство здравоохранения";

    private static SWTrainingFormPanel Ledger(string status, string returned = "0", bool aim = false, bool canAudit = true, int budget = 3000) => Form("Пульт департамента",
        Txt($"Возвращено в казну за раунд: {returned} кр"),
        Sec("Ведомость ведомств"),
        Item(AuditedMinistry, $"{budget} кр, {status}"),
        Btn(canAudit ? "Аудит (400)" : "Аудит", aim, !canAudit),
        Item("Министерство финансов", "2500 кр, проверено сегодня"),
        Btn("Аудит", disabled: true));

    public static SWTrainingStage[] AuditPick() => Steps(
        HandOut(),
        HandIn(),
        Result(Ledger("не проверено"), note: "В списке только занятые чужие ведомства. Проверенное сегодня отключено. " + Example));

    public static SWTrainingStage[] AuditPay() => Steps(
        Aim(Ledger("не проверено", aim: true)),
        Press(Ledger("не проверено", aim: true)),
        Result(Ledger("проверено сегодня", canAudit: false), note: "Оплачено 400 кр из бюджета департамента. Повторить сегодня нельзя."),
        Result(Ledger("не проверено", canAudit: false), note: "Другой случай: в бюджете меньше 400 кр, аудит не начать.", alternative: true));

    public static SWTrainingStage[] AuditRead() => Steps(
        Aim(TabRow(DepartmentConsole, ExpenditureTabs, 1)),
        Press(TabRow(DepartmentConsole, ExpenditureTabs, 1)),
        Result(Journal("Журнал департамента", $"[10:00] {AuditedMinistry}: отчётность чиста."),
            note: "Исход 1 из 3: чистая отчётность. У одного аудита только один исход. " + Example));

    // The next two shots show the other outcomes of the same audit at 10:00, not new audits of that ministry today.
    public static SWTrainingStage[] AuditRefund() => Steps(
        Hold(Journal("Журнал департамента", $"[10:00] {AuditedMinistry}: вскрыты приписки, возвращено 450 кр."),
            "Исход 2 из 3, вместо чистой отчётности: вскрыты приписки. " + Example, 4f),
        Result(Ledger("проверено сегодня", "450", canAudit: false, budget: 2550),
            note: "450 кр перешли из бюджета ведомства: было 3000, осталось 2550. Сумма зависит от его бюджета."));

    /// <summary>The third outcome of the same audit, step by step: the press, the spent audit, the log line, and what it means.</summary>
    public static SWTrainingStage[] AuditReport() => Steps(
        Hold(Ledger("не проверено"), "Исход 3 из 3 того же аудита в 10:00, а не новая проверка. " + Example, 4f),
        Aim(Ledger("не проверено", aim: true)),
        Press(Ledger("не проверено", aim: true)),
        Hold(Ledger("проверено сегодня", canAudit: false), "Аудит оплачен. Возврата нет, сумма в строке ведомства прежняя.", 4f),
        Aim(TabRow(DepartmentConsole, ExpenditureTabs, 1)),
        Press(TabRow(DepartmentConsole, ExpenditureTabs, 1)),
        Hold(Journal("Журнал департамента", $"[10:00] {AuditedMinistry}: следы хищений. Досье на {ExampleName} передано Минюсту."),
            "Следы хищений найдены. Досье на руководителя ведомства передано.", 4f),
        Result(Journal("Журнал департамента", $"[10:00] {AuditedMinistry}: следы хищений. Досье на {ExampleName} передано Минюсту."),
            note: "Дальше решает министерство юстиции. Для руководителя это не приговор, а повод для проверки."));

    // ---- 17 ---------------------------------------------------------------

    private const string ShipName = "«Странник», Грузовик, «Кореллианская линия»";
    private const string ShipOrigin = "Корусант, груз: зерно, пошлина: 300 кр";
    private const string BribeShip = "«Астра», Яхта, «Хатт-Карго»";

    private static SWTrainingFormPanel ShipApp(bool inspected, int aim = -1, bool canInspect = true) => Form("Очередь регистрации",
        Txt(ShipName),
        Dim(ShipOrigin),
        inspected ? Warn("Подозрительно") : Dim("не проверено"),
        inspected ? Dim("Досье: груз не совпадает с декларацией") : Dim(string.Empty),
        Choice(inspected ? ShipButtonsInspected : ShipButtons, inspected || !canInspect ? 1 : -1, aim));

    private static SWTrainingFormPanel BribeApp(bool aim = false) => Form("Очередь регистрации",
        Txt(BribeShip),
        Dim("происхождение неизвестно, груз: ткани, пошлина: 600 кр"),
        Dim("не проверено"),
        Choice(ShipButtons),
        Btn("Принять подношение (+900 кр лично)", aim, accent: SWTrainingAccent.Neutral));

    private static SWTrainingFormPanel Fleet(params string[] ships)
    {
        var all = new SWTrainingRow[ships.Length + 1];
        all[0] = Sec("Реестр флота");
        for (var i = 0; i < ships.Length; i++)
            all[i + 1] = Txt(ships[i]);
        return new SWTrainingFormPanel(string.Empty, all);
    }

    public static SWTrainingStage[] ShipsOpen() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Console, new[] { new SWTrainingOverlay(SWTrainingArt.Shuttle, 328, 88, 0, true) }), 3f, ScreenNote("Условное изображение судна у станции.")),
        HandOut(),
        HandIn(),
        Result(ShipApp(false), note: "Заявка показывает судно, происхождение, груз и пошлину. Риск неизвестен до досмотра. " + Example));

    public static SWTrainingStage[] ShipsInspect() => Steps(
        Aim(ShipApp(false, 1)),
        Press(ShipApp(false, 1)),
        Result(ShipApp(true), note: "Досмотр оплачен, досье открыто. Второй раз эту заявку досмотреть нельзя."),
        Result(ShipApp(false, canInspect: false), note: "Другой случай: денег меньше 400 кр, досмотр отключён.", alternative: true));

    public static SWTrainingStage[] ShipsDecide() => Steps(
        Aim(ShipApp(true, 2)),
        Press(ShipApp(true, 2)),
        Result(Form("Очередь регистрации", Dim("Очередь пуста - диспетчеры ждут бортов.")), note: "Отказ закрыл заявку."),
        Aim(ShipApp(true, 0), true),
        Press(ShipApp(true, 0), true),
        Result(Fleet($"«Странник», Грузовик, «Кореллианская линия»   сомнительный"), note: "Другой выбор: одобрение тоже закрывает заявку, судно в реестре.", alternative: true));

    public static SWTrainingStage[] ShipsBribe() => Steps(
        Hold(BribeApp(), "Другая заявка, ещё без решения. Подношение можно принять и до досмотра, и после. " + Example, 4f),
        Aim(BribeApp(true)),
        Press(BribeApp(true)),
        Result(Journal("Журнал бюро", "[11:00] «Астра» зарегистрирован без лишних вопросов."),
            note: "Подношение сразу одобряет заявку. Коррупция растёт, целостность реестра падает."));

    public static SWTrainingStage[] ShipsResult() => Steps(
        Aim(TabRow(BureauConsole, ShipsTabs, 1)),
        Press(TabRow(BureauConsole, ShipsTabs, 1)),
        Result(Fleet("«Астра», Яхта, «Хатт-Карго»   сомнительный"),
            note: "Подношение бывает только у заявок с рискованным грузом, досмотрены они или нет. Поэтому «Астра» в реестре не чистая: здесь «сомнительный»."),
        Result(Fleet("«Астра», Яхта, «Хатт-Карго»   фальшивый борт"),
            note: "Другой случай: у «Астры» поддельный транспондер. Такое судно поднимает угрозу.", alternative: true));

    // ---- 18 ---------------------------------------------------------------

    private static SWTrainingFormPanel Arsenal(string budget, string arms, string seize, bool aim = false, bool canSeize = true, bool security = true) => Form("Арсенал",
        Txt($"Бюджет: {budget} кр"),
        security ? Dim(string.Empty) : Bad("МВБ не активно: изъятия проходят без поддержки ячеек."),
        Sec("Чёрный рынок по секторам"),
        Item("Корусант", arms),
        Btn(seize, aim, !canSeize),
        Item("Тайтон", "10%, чисто"),
        Btn(security ? "Изъятие (совместно с ВБ) (600)" : "Изъятие (600)"));

    private const string JointSeize = "Изъятие (совместно с ВБ) (600)";

    public static SWTrainingStage[] WeaponsPick() => Steps(
        HandOut(),
        HandIn(),
        Result(Arsenal("2000", "45%, стволы в подворотнях", JointSeize), note: "В списке только сектора с сенатором, самые вооружённые сверху. " + Example));

    public static SWTrainingStage[] WeaponsPrice() => Steps(
        Result(Arsenal("2000", "45%, стволы в подворотнях", JointSeize), note: "Изъятие стоит 600 кр из бюджета бюро."),
        Result(Arsenal("2000", "30%, редкие стычки", "готовность через 1 ч 30 мин", canSeize: false), note: "Другой случай: после изъятия сектор ждёт два активных часа.", alternative: true),
        Result(Arsenal("400", "45%, стволы в подворотнях", "Изъятие (совместно с ВБ)", canSeize: false), note: "Другой случай: денег меньше 600 кр, кнопка отключена.", alternative: true));

    public static SWTrainingStage[] WeaponsSeize() => Steps(
        Aim(Arsenal("2000", "45%, стволы в подворотнях", JointSeize, true)),
        Press(Arsenal("2000", "45%, стволы в подворотнях", JointSeize, true)),
        Result(Arsenal("1400", "30%, редкие стычки", "готовность через 2 ч 00 мин", canSeize: false), note: "Вооружённость сектора снизилась, деньги списаны. " + Example));

    public static SWTrainingStage[] WeaponsRights() => Steps(
        Picture(ChartView(0), 4f, ScreenNote(SchemeNote)),
        Picture(ChartView(1), ResultHold, ScreenNote("Каждое изъятие немного снижает показатель прав. На этой консоли его нет.")));

    public static SWTrainingStage[] WeaponsLog() => Steps(
        Aim(TabRow(BureauConsole, WeaponsTabs, 1)),
        Press(TabRow(BureauConsole, WeaponsTabs, 1)),
        Result(Journal("Журнал бюро", "[10:00] Совместное с ВБ изъятие в Корусант: склады вскрыты, заговорщики без оружия."),
            note: "Министерство внутренней безопасности занято: изъятие ещё и снизило угрозу и заговоры."),
        Result(Journal("Журнал бюро", "[10:00] Изъятие оружия в Корусант."), note: "Другой случай: МВБ не занято, запись простая и без этого действия.", alternative: true));

    // ---- 19 ---------------------------------------------------------------

    private static SWTrainingFormPanel TransitBoard(int mode, int aim = -1, int mobility = 60) => Form("Пульт сети перевозок",
        Txt($"Мобильность: {mobility}/100 - Оживлённые линии"),
        Meter(mobility / 100f),
        Sec("Режимы секторов"),
        Txt("Корусант"),
        Choice(TransitModes, mode, aim),
        Txt("Тайтон"),
        Choice(TransitModes, 0));

    private static SWTrainingFormPanel TransitMeters(int mobility, int congestion, int safety) => Form("Пульт сети перевозок",
        Txt($"Мобильность: {mobility}/100 - Оживлённые линии"),
        Meter(mobility / 100f),
        Txt($"Загруженность узлов: {congestion}/100 - Рабочая нагрузка"),
        Meter(congestion / 100f, SWTrainingAccent.Neutral),
        Txt($"Безопасность линий: {safety}/100 - Тревожные сводки"),
        Meter(safety / 100f, SWTrainingAccent.Good));

    private static SWTrainingFormPanel LineEvent(bool aim = false) => Form("События на линиях",
        Txt("Застрявший лайнер"),
        Dim("Пассажирский лайнер с пятью тысячами пассажиров потерял ход между системами."),
        Warn("Решить в течение ~1.5 ч"),
        Dim("Спасательная операция за счёт бюро."),
        Btn("Выслать буксир  (600 кр)", aim),
        Dim("Сигнал бедствия передан ближайшим рейсам без выделения ресурсов бюро."),
        Btn("Оставить в ожидании"));

    public static SWTrainingStage[] TransitPick() => Steps(
        HandOut(),
        HandIn(),
        Result(TransitBoard(0), note: "Режим задаётся только секторам с сенатором. Текущий режим отключён. " + Example));

    public static SWTrainingStage[] TransitCompare() => Steps(
        Aim(TransitBoard(0, 1)),
        Press(TransitBoard(0, 1)),
        Result(Journal("Журнал бюро", "[10:00] Корусант: режим «Визовый режим»."), note: "Режим сменился сразу. Как он влияет на линии, видно по показателям пульта."));

    public static SWTrainingStage[] TransitCordon() => Steps(
        Aim(TransitBoard(1, 2)),
        Press(TransitBoard(1, 2)),
        Result(Journal("Журнал бюро", "[11:00] Кордон: Корусант закрыт для перемещений.", "[10:00] Корусант: режим «Визовый режим»."),
            note: "Сектор закрыт для перемещений. Снять кордон можно тем же рядом кнопок."));

    public static SWTrainingStage[] TransitEvent() => Steps(
        Aim(TabRow(BureauConsole, TransitTabs, 1)),
        Press(TabRow(BureauConsole, TransitTabs, 1)),
        Hold(LineEvent(), Example, 4f),
        Aim(LineEvent(true)),
        Press(LineEvent(true)),
        Result(Journal("Журнал бюро", "[12:00] Буксир дотащил лайнер до узла. Пассажиры аплодируют."), note: "Решение принято до срока, 600 кр списаны."),
        Result(Journal("Журнал бюро", "[13:00] «Застрявший лайнер»: диспетчеры не дождались решения."), note: "Другой случай: срок вышел, событие провалено.", alternative: true));

    public static SWTrainingStage[] TransitNetwork() => Steps(
        Hold(TransitMeters(60, 35, 55), "Раньше. " + Example, 4f),
        Result(TransitMeters(63, 38, 55), note: "Показатели меняются от многих причин. Не приписывайте всё одному решению."));

    // ---- 20 ---------------------------------------------------------------

    private static SWTrainingFormPanel Bench(bool inOffice, bool aim = false, bool canTake = true)
    {
        if (inOffice)
            return Form("Состав верховного суда", Txt($"Верховный Судья {ExampleName}"));
        return Form("Состав верховного суда",
            Txt($"Карта подтверждена: Верховный судья {ExampleName}."),
            canTake ? Btn("Принять полномочия", aim, accent: SWTrainingAccent.Good) : Dim("Должность занята или несовместима с текущим статусом"),
            Dim("Состав суда не сформирован."));
    }

    private static SWTrainingFormPanel Registry(string status) => Form("Реестр",
        Txt("Дело для примера"),
        Dim($"Дело №1, {status}"),
        Dim("День 1, 09:00-13:00, совещание до 14:00"));

    private static SWTrainingFormPanel Decision(int plaintiff, string? myVote, int aim = -1, bool canClose = false, bool aimClose = false, string? verdict = null)
    {
        if (verdict != null)
            return Form("Решение суда",
                Txt("Дело для примера"),
                Fine("В пользу истца: исход для примера"),
                Warn("В пользу ответчика: исход для примера"),
                Txt($"Вердикт: {verdict}"));

        var votes = $"Подано голосов: истец - {plaintiff}, ответчик - 0" + (myVote == null ? string.Empty : $"   |   ваш голос: {myVote}");
        return Form("Решение суда",
            Txt("Дело для примера"),
            Fine("В пользу истца: исход для примера"),
            Warn("В пользу ответчика: исход для примера"),
            Dim(votes),
            Choice(VerdictButtons, -1, aim),
            canClose ? Btn("Закрыть голосование", aimClose, accent: SWTrainingAccent.Bad) : Dim("Голос можно изменить до закрытия голосования."));
    }

    public static SWTrainingStage[] JusticeRegister() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow("Верховный суд Республики", CourtTabs, 3)),
        Press(TabRow("Верховный суд Республики", CourtTabs, 3)),
        Aim(Bench(false, true)),
        Press(Bench(false, true)),
        Result(Bench(true), note: "Полномочия приняты, имя появилось в составе. " + Example),
        Result(Bench(false, canTake: false), note: "Другой случай: место занято, кнопки нет.", alternative: true));

    public static SWTrainingStage[] JusticeSchedule() => Steps(
        Aim(TabRow("Верховный суд Республики", CourtTabs, 1), time: "08:40"),
        Press(TabRow("Верховный суд Республики", CourtTabs, 1), time: "08:40"),
        new Step(4f, Closeup("08:40"), Registry("Ожидает слушания"), Notes: ScreenNote("Время на часах для примера.")),
        Result(Registry("Слушание открыто"), note: "В девять слушание открылось. Сроки дела указаны в реестре.", time: "09:00"));

    public static SWTrainingStage[] JusticeHearing() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Court, Array.Empty<SWTrainingOverlay>(), Focus: SWTrainingFocus.Prosecutor), 4f, ScreenNote("Учебная сцена. Стороны говорят в зале, а не через консоль.")),
        Picture(new SWTrainingVisual(SWTrainingScene.Court, Array.Empty<SWTrainingOverlay>(), Focus: SWTrainingFocus.Defender), 4f),
        Picture(new SWTrainingVisual(SWTrainingScene.Court, Array.Empty<SWTrainingOverlay>(), Focus: SWTrainingFocus.Judges), ResultHold,
            ScreenNote("Голосуют только судьи. У обвинителя и защитника кнопок голоса нет.")));

    public static SWTrainingStage[] JusticeVote() => Steps(
        Aim(TabRow("Дело для примера", CaseViews, 1, 0), time: "10:30"),
        Press(TabRow("Дело для примера", CaseViews, 1, 0), time: "10:30"),
        Aim(Decision(0, null, 0), time: "10:30"),
        Press(Decision(0, null, 0), time: "10:30"),
        Result(Decision(1, "истец"), note: "Голос учтён. До закрытия его можно изменить.", time: "10:30"));

    public static SWTrainingStage[] JusticeClose() => Steps(
        new Step(4f, Closeup("13:05"), Decision(2, "истец", canClose: true), Notes: ScreenNote("Слушание окончено, идёт совещание. " + Example)),
        Aim(Decision(2, "истец", canClose: true, aimClose: true), time: "13:10"),
        Press(Decision(2, "истец", canClose: true, aimClose: true), time: "13:10"),
        Result(Decision(2, "истец", verdict: "В пользу истца"), note: "Голосование закрыто, вердикт вынесен.", time: "13:10"),
        Result(Decision(0, null, verdict: "Решение не вынесено"), note: "Другой случай: к 14:00 голосование закрылось само, голосов не было.", alternative: true, time: "14:00"));
}
