using System;

namespace Content.Client._SW._Senate.Training;

/// <summary>
/// Timelines of chapters 15 to 20: the arbitration and expenditure departments, the three bureaus and the chief justice.
/// Labels, log lines, costs and case texts come from the consoles, their server systems and the prototypes. Each chapter
/// follows one coherent situation; which case, ship or event a real round brings is up to the round.
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
        Result(ArbCase(ShipyardCase, 0, 60), note: "Подрядчик выставлял счета за корабли, которых нет. Дело на первой из трёх стадий."));

    public static SWTrainingStage[] ArbitragePay() => Steps(
        Aim(ArbCase(ShipyardCase, 0, 60, true)),
        Press(ArbCase(ShipyardCase, 0, 60, true)),
        Result(ArbCase(ShipyardCase, 1, 61), note: "500 кр оплачены, зацепка найдена. Дело на второй стадии."),
        Result(ArbCase(ShipyardCase, 1, 61, canPay: false), note: "Если в бюджете меньше 800 кр, кнопка отключена и дело стоит.", alternative: true));

    public static SWTrainingStage[] ArbitragePressure() => Steps(
        Back("Бюджета хватило: дело о верфях на второй стадии. У второго дела те же 9% риска, и каждая оплата может обернуться давлением сверху.",
            Result(ArbCase(CasinoCase, 0, 61))),
        Case("Если с этими 9% не повезёт, оплата сорвётся: дело развалит давление сверху.", ArbCase(CasinoCase, 0, 61)),
        Aim(ArbCase(CasinoCase, 0, 61, true), true),
        Press(ArbCase(CasinoCase, 0, 61, true), true),
        Result(Journal("Журнал департамента", $"[11:00] «{CasinoCase}»: дело развалено давлением сверху."),
            note: "Деньги потрачены, дело закрыто, независимость снизилась.", alternative: true, continues: true));

    public static SWTrainingStage[] ArbitrageContinue() => Steps(
        Back("Дело о верфях по-прежнему на второй стадии, риск давления 9%.", ArbCase(ShipyardCase, 1, 61)),
        Aim(ArbCase(ShipyardCase, 1, 61, true)),
        Press(ArbCase(ShipyardCase, 1, 61, true)),
        Result(ArbCase(ShipyardCase, 2, 62), note: "Дело о верфях дошло до ареста. Риск остаётся на каждой оплате."));

    public static SWTrainingStage[] ArbitrageResult() => Steps(
        Aim(ArbCase(ShipyardCase, 2, 62, true)),
        Press(ArbCase(ShipyardCase, 2, 62, true)),
        Result(Journal("Журнал департамента", $"[12:00] «{ShipyardCase}»: арест! Штраф {3500} кр взыскан."),
            note: "Штраф поступил в бюджет департамента, независимость выросла, производство оздоровится."));

    // ---- 16 ---------------------------------------------------------------

    private const string AuditedMinistry = "Министерство здравоохранения";
    private const string HealthMinister = "Дорн Вейс";

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
        Result(Ledger("не проверено"), note: "Минздрав много тратит на лихорадку. В списке только чужие занятые ведомства, проверенное сегодня отключено."));

    public static SWTrainingStage[] AuditPay() => Steps(
        Aim(Ledger("не проверено", aim: true)),
        Press(Ledger("не проверено", aim: true)),
        Result(Ledger("проверено сегодня", canAudit: false), note: "400 кр ушли на проверку. До завтра это ведомство снова не проверить."),
        Result(Ledger("не проверено", canAudit: false), note: "Если в бюджете меньше 400 кр, аудит не начать.", alternative: true));

    public static SWTrainingStage[] AuditRead() => Steps(
        Back("Денег хватило: Минздрав проверен сегодня.", Ledger("проверено сегодня", canAudit: false)),
        Aim(TabRow(DepartmentConsole, ExpenditureTabs, 1)),
        Press(TabRow(DepartmentConsole, ExpenditureTabs, 1)),
        Result(Journal("Журнал департамента", $"[10:00] {AuditedMinistry}: отчётность чиста."),
            note: "Отчётность Минздрава чиста. Это один из трёх возможных итогов аудита."));

    // The next two shots replay the same audit at 10:00 with its other outcomes; they are not new audits today.
    public static SWTrainingStage[] AuditRefund() => Steps(
        Hold(Journal("Журнал департамента", $"[10:00] {AuditedMinistry}: вскрыты приписки, возвращено 450 кр."),
            "Тот же аудит мог закончиться иначе: найдены приписки.", 4f),
        Result(Ledger("проверено сегодня", "450", canAudit: false, budget: 2550),
            note: "450 кр перешли из бюджета Минздрава: было 3000, осталось 2550. Сумма зависит от его бюджета."));

    /// <summary>The third outcome of the same audit, step by step: the press, the spent audit, the log line, and what it means.</summary>
    public static SWTrainingStage[] AuditReport() => Steps(
        Hold(Ledger("не проверено"), "Третий возможный итог того же аудита в 10:00.", 4f),
        Aim(Ledger("не проверено", aim: true)),
        Press(Ledger("не проверено", aim: true)),
        Hold(Ledger("проверено сегодня", canAudit: false), "Аудит оплачен. Денег не вернули, сумма Минздрава прежняя.", 4f),
        Aim(TabRow(DepartmentConsole, ExpenditureTabs, 1)),
        Press(TabRow(DepartmentConsole, ExpenditureTabs, 1)),
        Hold(Journal("Журнал департамента", $"[10:00] {AuditedMinistry}: следы хищений. Досье на {HealthMinister} передано Минюсту."),
            "Найдены следы хищений. Досье на министра здравоохранения ушло в Минюст.", 4f),
        Result(Journal("Журнал департамента", $"[10:00] {AuditedMinistry}: следы хищений. Досье на {HealthMinister} передано Минюсту."),
            note: "Дальше решает Министерство юстиции. Для Дорна Вейса это не приговор, а повод для проверки."));

    // ---- 17 ---------------------------------------------------------------

    private const string ShipName = "«Странник», Грузовик, «Кореллианская линия»";
    private const string ShipOrigin = "Корусант, груз: медикаменты, пошлина: 300 кр";
    private const string BribeShip = "«Ронто», Яхта, «Хатт-Карго»";

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
        Picture(new SWTrainingVisual(SWTrainingScene.Console, new[] { new SWTrainingOverlay(SWTrainingArt.Shuttle, 328, 88, 0, true) }), 3f, ScreenNote("К станции подходит грузовик «Странник».")),
        HandOut(),
        HandIn(),
        Result(ShipApp(false), note: "Он идёт из Корусанта и везёт медикаменты. Что на борту на самом деле, покажет только досмотр."));

    public static SWTrainingStage[] ShipsInspect() => Steps(
        Aim(ShipApp(false, 1)),
        Press(ShipApp(false, 1)),
        Result(ShipApp(true), note: "Досмотр оплачен: груз не совпадает с декларацией. Второй раз эту заявку не досмотреть."),
        Result(ShipApp(false, canInspect: false), note: "Если в бюджете меньше 400 кр, досмотр отключён.", alternative: true));

    public static SWTrainingStage[] ShipsDecide() => Steps(
        Back("Досмотр оплачен: груз «Странника» не совпадает с декларацией.", ShipApp(true)),
        Aim(ShipApp(true, 2)),
        Press(ShipApp(true, 2)),
        Result(Form("Очередь регистрации", Dim("Очередь пуста - диспетчеры ждут бортов.")), note: "«Страннику» отказано, заявка закрыта."),
        Case("Досмотр не обязывает отказывать: «Странника» можно и одобрить.", ShipApp(true)),
        Aim(ShipApp(true, 0), true),
        Press(ShipApp(true, 0), true),
        Result(Fleet("«Странник», Грузовик, «Кореллианская линия»   сомнительный"), note: "Заявка тоже закрыта, а сомнительное судно попало в реестр флота.", alternative: true, continues: true));

    public static SWTrainingStage[] ShipsBribe() => Steps(
        Back("«Страннику» отказано, в реестр он не попал. Следом пришёл «Ронто», и его капитан сразу предлагает деньги.", BribeApp()),
        Aim(BribeApp(true)),
        Press(BribeApp(true)),
        Result(Journal("Журнал бюро", "[11:00] «Ронто» зарегистрирован без лишних вопросов."),
            note: "Подношение сразу одобряет заявку. 900 кр на личном счёте, коррупция растёт, целостность реестра падает."));

    public static SWTrainingStage[] ShipsResult() => Steps(
        Aim(TabRow(BureauConsole, ShipsTabs, 1)),
        Press(TabRow(BureauConsole, ShipsTabs, 1)),
        Result(Fleet($"{BribeShip}   сомнительный"),
            note: "Подношения предлагают только за рискованный груз. «Ронто» летает под флагом Республики как сомнительное судно."),
        Result(Fleet($"{BribeShip}   фальшивый борт"),
            note: "С поддельным транспондером он значился бы фальшивым бортом и поднял бы угрозу.", alternative: true));

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
        Result(Arsenal("2000", "45%, стволы в подворотнях", JointSeize), note: "В Корусанте оружие ходит по подворотням. Самые вооружённые сектора стоят сверху."));

    public static SWTrainingStage[] WeaponsPrice() => Steps(
        Result(Arsenal("2000", "45%, стволы в подворотнях", JointSeize), note: "Изъятие стоит 600 кр из бюджета бюро, денег хватает."),
        Result(Arsenal("2000", "30%, редкие стычки", "готовность через 1 ч 30 мин", canSeize: false), note: "После изъятия сектор ждёт два активных часа.", alternative: true),
        Result(Arsenal("400", "45%, стволы в подворотнях", "Изъятие (совместно с ВБ)", canSeize: false), note: "Если в бюджете меньше 600 кр, кнопка отключена.", alternative: true));

    public static SWTrainingStage[] WeaponsSeize() => Steps(
        Back("Задержки нет, и денег хватает: в бюджете бюро 2000 кр.", Arsenal("2000", "45%, стволы в подворотнях", JointSeize)),
        Aim(Arsenal("2000", "45%, стволы в подворотнях", JointSeize, true)),
        Press(Arsenal("2000", "45%, стволы в подворотнях", JointSeize, true)),
        Result(Arsenal("1400", "30%, редкие стычки", "готовность через 2 ч 00 мин", canSeize: false), note: "Склады вскрыты: вооружённость Корусанта упала, 600 кр списаны."));

    public static SWTrainingStage[] WeaponsRights() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Entrance, new[] { Figure(SWTrainingArt.FigGuardOfficer, SWSenateTrainingArt.SlotAX), Figure(SWTrainingArt.FigCorrespondent, SWSenateTrainingArt.SlotBX) }), 4f,
            ScreenNote("После рейда горожане спрашивают, по какому праву вскрывали их склады.")),
        Picture(new SWTrainingVisual(SWTrainingScene.Entrance, new[] { Figure(SWTrainingArt.FigGuardOfficer, SWSenateTrainingArt.SlotAX), Figure(SWTrainingArt.FigCorrespondent, SWSenateTrainingArt.SlotBX, 1) }), ResultHold,
            ScreenNote("Каждое изъятие понемногу снижает гражданские права. Чем чаще рейды, тем сильнее недовольство.")));

    public static SWTrainingStage[] WeaponsLog() => Steps(
        Aim(TabRow(BureauConsole, WeaponsTabs, 1)),
        Press(TabRow(BureauConsole, WeaponsTabs, 1)),
        Result(Journal("Журнал бюро", "[10:00] Совместное с ВБ изъятие в Корусант: склады вскрыты, заговорщики без оружия."),
            note: "Министр внутренней безопасности на месте, поэтому рейд ещё и ударил по угрозе и заговорам."),
        Result(Journal("Журнал бюро", "[10:00] Изъятие оружия в Корусант."), note: "Без министра внутренней безопасности изъятие только забирает оружие.", alternative: true));

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
        Result(TransitBoard(0), note: "Лихорадка в Корусанте, а линии открыты. Режим задаётся только секторам с сенатором."));

    public static SWTrainingStage[] TransitCompare() => Steps(
        Aim(TransitBoard(0, 1)),
        Press(TransitBoard(0, 1)),
        Result(Journal("Журнал бюро", "[10:00] Корусант: режим «Визовый режим»."), note: "Визы введены сразу. Следите, как изменятся показатели пульта."));

    public static SWTrainingStage[] TransitCordon() => Steps(
        Aim(TransitBoard(1, 2)),
        Press(TransitBoard(1, 2)),
        Result(Journal("Журнал бюро", "[11:00] Кордон: Корусант закрыт для перемещений.", "[10:00] Корусант: режим «Визовый режим»."),
            note: "Вспышка растёт, и Корусант закрыт для перемещений. Снять кордон можно тем же рядом кнопок."));

    public static SWTrainingStage[] TransitEvent() => Steps(
        Aim(TabRow(BureauConsole, TransitTabs, 1)),
        Press(TabRow(BureauConsole, TransitTabs, 1)),
        Hold(LineEvent(), "На линии застрял лайнер с пятью тысячами пассажиров.", 4f),
        Aim(LineEvent(true)),
        Press(LineEvent(true)),
        Result(Journal("Журнал бюро", "[12:00] Буксир дотащил лайнер до узла. Пассажиры аплодируют."), note: "Буксир отправлен до срока, 600 кр списаны."),
        Result(Journal("Журнал бюро", "[13:00] «Застрявший лайнер»: диспетчеры не дождались решения."), note: "Если не ответить до срока, событие провалится.", alternative: true));

    public static SWTrainingStage[] TransitNetwork() => Steps(
        Back("Буксир успел, лайнер дотащили до узла. Утром, до виз и кордона, показатели были такими.", TransitMeters(60, 35, 55)),
        Result(TransitMeters(63, 38, 55), note: "К вечеру показатели сдвинулись. На них влияет многое, не только ваш режим."));

    // ---- 20 ---------------------------------------------------------------

    private const string ChiefName = "Арен Солт";
    private const string CourtCase = "Дело о незаконном карантине";
    private const string PlaintiffWin = "Карантин признан чрезмерным, сектору выплачена компенсация.";
    private const string DefendantWin = "Действия санитарных властей признаны правомерными.";

    private static SWTrainingFormPanel Bench(bool inOffice, bool aim = false, bool canTake = true)
    {
        if (inOffice)
            return Form("Состав верховного суда", Txt($"Верховный Судья {ChiefName}"), Dim("Обвинитель: не назначен"), Dim("Защитник: не назначен"));
        return Form("Состав верховного суда",
            Txt($"Карта подтверждена: Верховный судья {ChiefName}."),
            canTake ? Btn("Принять полномочия", aim, accent: SWTrainingAccent.Good) : Dim("Должность занята или несовместима с текущим статусом"),
            Dim("Состав суда не сформирован."));
    }

    private static SWTrainingFormPanel Registry(string status) => Form("Реестр",
        Txt(CourtCase),
        Dim($"Дело №1, {status}"),
        Dim("День 2, 09:00-13:00, совещание до 14:00"));

    private static SWTrainingFormPanel Decision(int plaintiff, string? myVote, int aim = -1, bool canClose = false, bool aimClose = false, string? verdict = null)
    {
        if (verdict != null)
            return Form("Решение суда",
                Txt(CourtCase),
                Fine($"В пользу истца: {PlaintiffWin}"),
                Warn($"В пользу ответчика: {DefendantWin}"),
                Txt($"Вердикт: {verdict}"));

        var votes = $"Подано голосов: истец - {plaintiff}, ответчик - 0" + (myVote == null ? string.Empty : $"   |   ваш голос: {myVote}");
        return Form("Решение суда",
            Txt(CourtCase),
            Fine($"В пользу истца: {PlaintiffWin}"),
            Warn($"В пользу ответчика: {DefendantWin}"),
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
        Result(Bench(true), note: "Арен Солт принял полномочия и возглавил суд."),
        Result(Bench(false, canTake: false), note: "Если место уже занято, кнопки нет.", alternative: true));

    public static SWTrainingStage[] JusticeSchedule() => Steps(
        Back("Место в суде зарегистрировано за вами.", TabRow("Верховный суд Республики", CourtTabs, -1), "08:40"),
        Aim(TabRow("Верховный суд Республики", CourtTabs, 1), time: "08:40"),
        Press(TabRow("Верховный суд Республики", CourtTabs, 1), time: "08:40"),
        new Step(4f, Closeup("08:40"), Registry("Ожидает слушания"), Notes: ScreenNote("Корусант оспаривает давний карантин: санитарные власти на девять суток закрыли его маршруты из-за кореллианского гриппа. Слушание в девять.")),
        Result(Registry("Слушание открыто"), note: "В девять слушание открылось и идёт до 13:00. Решение судьи принимают до 14:00.", time: "09:00"));

    public static SWTrainingStage[] JusticeHearing() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Court, Array.Empty<SWTrainingOverlay>(), Focus: SWTrainingFocus.Prosecutor), 4f,
            ScreenNote("Обвинитель: вспышку локализовали на вторые сутки, карантин был лишним.")),
        Picture(new SWTrainingVisual(SWTrainingScene.Court, Array.Empty<SWTrainingOverlay>(), Focus: SWTrainingFocus.Defender), 4f,
            ScreenNote("Защитник: при угрозе жизни осторожность обязательна.")),
        Picture(new SWTrainingVisual(SWTrainingScene.Court, Array.Empty<SWTrainingOverlay>(), Focus: SWTrainingFocus.Judges), ResultHold,
            ScreenNote("Судьи выслушали обе стороны. Голосовать будут только они.")));

    public static SWTrainingStage[] JusticeVote() => Steps(
        Aim(TabRow(CourtCase, CaseViews, 1, 0), time: "10:30"),
        Press(TabRow(CourtCase, CaseViews, 1, 0), time: "10:30"),
        Aim(Decision(0, null, 0), time: "10:30"),
        Press(Decision(0, null, 0), time: "10:30"),
        Result(Decision(1, "истец"), note: "Голос за Корусант учтён. До закрытия его можно изменить.", time: "10:30"));

    public static SWTrainingStage[] JusticeClose() => Steps(
        new Step(4f, Closeup("13:05"), Decision(2, "истец", canClose: true), Notes: ScreenNote("Слушание окончено, оба судьи за истца. Идёт совещание.")),
        Aim(Decision(2, "истец", canClose: true, aimClose: true), time: "13:10"),
        Press(Decision(2, "истец", canClose: true, aimClose: true), time: "13:10"),
        Result(Decision(2, "истец", verdict: "В пользу истца"), note: "Голосование закрыто: Корусант получит компенсацию.", time: "13:10"),
        Result(Decision(0, null, verdict: "Решение не вынесено"), note: "Если к 14:00 голосов нет, голосование закроется само и решения не будет.", alternative: true, time: "14:00"));
}
