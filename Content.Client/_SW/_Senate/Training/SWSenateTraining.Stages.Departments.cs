using System;
using System.Collections.Generic;

namespace Content.Client._SW._Senate.Training;

/// <summary>
/// Timelines of chapters 13А, 16А and 18А: the healthcare department's express purchase and the call it makes possible,
/// a whistleblower signal of the expenditure department, and the licences and droid registry of the weapons bureau.
/// Labels, costs, chances and log lines are those of the department and bureau consoles and their server systems.
/// </summary>
public static partial class SWTrainingSamples
{
    // ---- 13А: supplies and the call ------------------------------------------

    private const string MineCall = "Обрушение шахты - Корусант";
    private const string MineText = "Под завалами живые. Счёт на часы.";

    /// <summary>«Операции»: the express purchase and the rotation, with their price, effect and state.</summary>
    private static SWTrainingFormPanel OpsBoard(string purchase, bool purchaseReady, string rotation, bool rotationReady, bool aim = false) => Form("Экспресс-операции",
        Txt("Закупка медикаментов   800 кр"),
        Dim("Пополнить полевые склады: запасы +30."),
        Btn(purchaseReady ? "Выполнить" : purchase, aim, !purchaseReady, SWTrainingAccent.Good),
        Txt("Ротация бригад   600 кр"),
        Dim("Свежие смены из резерва: готовность +25."),
        Btn(rotationReady ? "Выполнить" : rotation, disabled: !rotationReady, accent: SWTrainingAccent.Good));

    /// <summary>«Вызовы»: the mine call with both answers; each needs a free brigade and enough supplies, the reinforced one also 500 кр.</summary>
    private static SWTrainingFormPanel CareCall(string left, int chance, bool canBasic, bool canReinforced, bool aimBasic = false) => Form("Вызовы на линии",
        Txt(MineCall),
        Dim(MineText),
        Warn($"Вызов сгорит через ~{left} ч"),
        Dim($"Запасы -10. Успех зависит от готовности и запасов (сейчас ~{chance}%)."),
        Btn("Отправить бригаду", aimBasic, !canBasic),
        Dim("Запасы -15, гарантированный успех."),
        Btn("Усиленный выезд  (500 кр)", disabled: !canReinforced));

    public static SWTrainingStage[] SupplyOpen() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(DepartmentConsole, HealthcareTabs, 1), time: "10:25"),
        Press(TabRow(DepartmentConsole, HealthcareTabs, 1), time: "10:25"),
        Result(CareCall("1.6", 62, false, false),
            note: "В шахте Корусанта завал. На складах всего 8 из 100, а обычному выезду нужно 10, усиленному 15: обе кнопки отключены.", time: "10:25"));

    public static SWTrainingStage[] SupplyBuy() => Steps(
        Aim(TabRow(DepartmentConsole, HealthcareTabs, 2), time: "10:27"),
        Press(TabRow(DepartmentConsole, HealthcareTabs, 2), time: "10:27"),
        Wait(OpsBoard(string.Empty, true, string.Empty, true), "В бюджете департамента ровно 800 кр.", 3f, "10:27"),
        Aim(OpsBoard(string.Empty, true, string.Empty, true, true), time: "10:28"),
        Press(OpsBoard(string.Empty, true, string.Empty, true, true), time: "10:28"),
        Result(OpsBoard("готовность через 2 ч 00 мин", false, "недостаточно бюджета", false),
            note: "800 кр списаны, склады пополнены сразу. Повторить закупку можно через два активных игровых часа, а на ротацию денег больше нет.", time: "10:28"));

    public static SWTrainingStage[] SupplyStock() => Steps(
        Aim(TabRow(DepartmentConsole, HealthcareTabs, 0), time: "10:29"),
        Press(TabRow(DepartmentConsole, HealthcareTabs, 0), time: "10:29"),
        Result(CareWork(60, "Устали, но держатся", 38, "Запасы тают", 3, 0),
            note: "Запасы выросли с 8 до 38.", time: "10:29"),
        Result(CareWork(60, "Устали, но держатся", 100, "Склады полны", 3, 0),
            note: "Выше 100 запасы не поднимаются: при 85 та же закупка добавила бы только 15.", alternative: true, time: "10:29"));

    public static SWTrainingStage[] SupplyDispatch() => Steps(
        Back("На складах 38: закупка добавила 30 к прежним 8.", CareWork(60, "Устали, но держатся", 38, "Запасы тают", 3, 0), "10:30"),
        Aim(TabRow(DepartmentConsole, HealthcareTabs, 1), time: "10:30"),
        Press(TabRow(DepartmentConsole, HealthcareTabs, 1), time: "10:30"),
        Wait(CareCall("1.5", 69, true, false), "Теперь на обычный выезд хватает. Усиленный недоступен: бюджет ушёл на закупку. Кнопка показывает прогноз по нынешним запасам и готовности.", 4f, "10:30"),
        Aim(CareCall("1.5", 69, true, false, true), time: "10:30"),
        Press(CareCall("1.5", 69, true, false, true), time: "10:30"),
        Result(CareWork(52, "Устали, но держатся", 28, "Работаем на бинтах", 2, 1),
            note: "Бригада вылетела на час: запасы −10, готовность −8.", time: "10:30"),
        Result(CareJournal("[12:00] Обрушение шахты: вызов сгорел без ответа (Корусант)."),
            note: "Если вызов успел сгореть, отправлять уже некуда, а последствия ложатся на сектор.", alternative: true, time: "12:00"));

    public static SWTrainingStage[] SupplyOutcome() => Steps(
        Back("Вызов не сгорел: бригада успела вылететь и работает под завалами.", CareWork(52, "Устали, но держатся", 28, "Работаем на бинтах", 2, 1), "11:00"),
        Result(CareJournal("[11:00] Обрушение шахты: бригада справилась (Корусант).", "[10:00] Бригада вылетела: Обрушение шахты (Корусант)."),
            note: "Шахтёров вытащили, бригада вернулась, готовность немного выросла. Шанс выезда считается уже после вылета: при готовности 52 и запасах 28 он был около 63%, а не 69% из прогноза.", time: "11:30"),
        Result(CareJournal("[11:00] Обрушение шахты: выезд прошёл тяжело, потери среди пострадавших.", "[10:00] Бригада вылетела: Обрушение шахты (Корусант)."),
            note: "Примерно в 37 случаях из ста такой выезд проходит тяжело: бригада вернулась, но пострадавших спасли не всех.", alternative: true, time: "11:30"));

    // ---- 16А: a whistleblower signal ---------------------------------------

    private static SWTrainingFormPanel TipCard(string name, string text, string left, int bribe = 0, bool aimCheck = false, bool aimBribe = false, bool canCheck = true)
    {
        var rows = new List<SWTrainingRow>
        {
            Sec("Сигналы о нарушениях"),
            Txt($"Сигнал: {name}"),
            Dim(text),
            Warn($"След остынет через ~{left} ч"),
            Dim("Ревизоры выедут на место. Подтверждение вернёт деньги в казну."),
            Btn("Проверить сигнал  (300 кр)", aimCheck, !canCheck),
        };
        if (bribe > 0)
        {
            rows.Add(Dim("Нарушитель предлагает скрыть нарушение."));
            rows.Add(Btn($"Скрыть нарушение (+{bribe} кр лично)", aimBribe, accent: SWTrainingAccent.Bad));
        }
        return new SWTrainingFormPanel("Пульт департамента", rows.ToArray());
    }

    private static SWTrainingFormPanel TipWork(int transparency, string status, string returned) => Form("Пульт департамента",
        Txt($"Прозрачность бюджета: {transparency}/100 - {status}"),
        Meter(transparency / 100f),
        Txt($"Возвращено в казну за раунд: {returned} кр"),
        Sec("Ведомость ведомств"),
        Item(AuditedMinistry, "3000 кр, не проверено"));

    private const string GhostTip = "Фиктивные сотрудники в ведомостях";
    private const string GhostText = "На довольствии числятся давно уволенные сотрудники.";

    public static SWTrainingStage[] TipOpen() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(DepartmentConsole, ExpenditureTabs, 1), time: "11:00"),
        Press(TabRow(DepartmentConsole, ExpenditureTabs, 1), time: "11:00"),
        Result(TipCard(GhostTip, GhostText, "2.6", 800),
            note: "Аноним сообщает о мёртвых душах в ведомостях. Нарушитель уже предлагает 800 кр, чтобы сигнал закрыли.", time: "11:00"));

    public static SWTrainingStage[] TipCheck() => Steps(
        Aim(TipCard(GhostTip, GhostText, "2.6", 800, aimCheck: true), time: "11:02"),
        Press(TipCard(GhostTip, GhostText, "2.6", 800, aimCheck: true), time: "11:02"),
        Result(Journal("Журнал департамента", $"[11:00] Сигнал «{GhostTip}» подтвердился: возвращено 1000 кр."),
            note: "300 кр ушли на выезд ревизоров, 1 000 вернулись в бюджет департамента. Возврат бывает от 600 до 1 400 кр.", time: "11:02"),
        Result(Journal("Журнал департамента", $"[11:00] Сигнал «{GhostTip}» не подтвердился."),
            note: "Примерно в четырёх случаях из десяти сигнал не подтверждается: 300 кр потрачены, денег не вернули.", alternative: true, time: "11:02"));

    public static SWTrainingStage[] TipTransparency() => Steps(
        Back("Сигнал подтвердился: в бюджет департамента вернулась 1 000 кр.",
            Journal("Журнал департамента", $"[11:00] Сигнал «{GhostTip}» подтвердился: возвращено 1000 кр."), "11:05"),
        Aim(TabRow(DepartmentConsole, ExpenditureTabs, 0), time: "11:05"),
        Press(TabRow(DepartmentConsole, ExpenditureTabs, 0), time: "11:05"),
        Result(TipWork(51, "Допустимая прозрачность", "1000"),
            note: "Подтверждённый сигнал поднял прозрачность с 48 до 51, и возврат учтён за раунд.", time: "11:05"),
        Result(TipWork(49, "Допустимая прозрачность", "0"),
            note: "Неподтверждённая проверка тоже немного помогает: прозрачность +1.", alternative: true, time: "11:05"));

    public static SWTrainingStage[] TipBribe() => Steps(
        Back("После подтверждённого сигнала прозрачность 51. Днём приходит новый сигнал, и снова с предложением.",
            TipCard("Двойные командировочные", "Одни и те же перелёты оплачены дважды.", "2.8", 700), "13:00"),
        Aim(TipCard("Двойные командировочные", "Одни и те же перелёты оплачены дважды.", "2.8", 700, aimBribe: true), time: "13:00"),
        Press(TipCard("Двойные командировочные", "Одни и те же перелёты оплачены дважды.", "2.8", 700, aimBribe: true), time: "13:00"),
        Result(Journal("Журнал департамента", "[13:00] Сигнал «Двойные командировочные» закрыт без проверки."),
            note: "700 кр легли на личный счёт главы департамента. Прозрачность −5, личная коррупция растёт, и об этом может узнать голонет.", time: "13:00"));

    public static SWTrainingStage[] TipCold() => Steps(
        Wait(TipCard("Банкет за казённый счёт", "Приём «для служебных нужд» на роскошной палубе.", "0.4"),
            "Третий сигнал пришёл в 13:00, вместе с командировочными, и так и остался без проверки. Скрыть его никто не предлагает.", 4f, "15:35"),
        Result(Journal("Журнал департамента", "[16:00] Сигнал «Банкет за казённый счёт» остыл: следы заметены."),
            note: "В 16:00, через три часа после прихода, след остыл, и прозрачность упала на 2. Проверить можно было в любой момент до этого.", time: "16:00"));

    // ---- 18А: licences and droids ------------------------------------------

    private static readonly string[] ArmsModes = { "Запрет", "Лицензии", "Свободно" };
    private static readonly string[] LicenseButtons = { "Одобрить (+740)", "Проверка (300)", "Отказать" };
    private static readonly string[] LicenseButtonsChecked = { "Одобрить (+740)", "Проверено", "Отказать" };
    private const string PvcApplicant = "ЧВК «Красные вымпелы», Тяжёлое вооружение";

    private static SWTrainingFormPanel LicenseRegime(int heavy, string income, int aim = -1) => Form("Пульт регулирования",
        Txt($"Лицензионные сборы: ~{income} кр/ч в казну бюро"),
        Sec("Лицензионные режимы"),
        Dim("Гражданское оружие"),
        Choice(ArmsModes, 1),
        Dim("Тяжёлое вооружение"),
        Choice(ArmsModes, heavy, aim),
        Dim("Боевые дроиды"),
        Choice(ArmsModes, 1));

    /// <summary>One application: its applicant and cargo, the paid inspection once, and the decision buttons.</summary>
    private static SWTrainingFormPanel LicenseApp(bool inspected, int aim = -1, bool aimBribe = false)
    {
        var rows = new List<SWTrainingRow>
        {
            Sec("Заявки на лицензии"),
            Txt(PvcApplicant),
            inspected ? Warn("Сомнительный") : Dim("не проверено"),
            Dim("назначение: охрана конвоев, партия: 30 ед., пошлина: 740 кр"),
        };
        if (inspected)
            rows.Add(Txt("Досье: часть прошлых партий не найдена"));
        rows.Add(inspected ? Choice(LicenseButtonsChecked, 1, aim) : Choice(LicenseButtons, -1, aim));
        rows.Add(Btn("Принять подношение (+1100 кр лично)", aimBribe, accent: SWTrainingAccent.Bad));
        return new SWTrainingFormPanel("Лицензии и дроиды", rows.ToArray());
    }

    private static SWTrainingFormPanel DroidRegistry(int reserve, int security, int aim = -1, string? log = null)
    {
        var rows = new List<SWTrainingRow>
        {
            Sec("Реестр боевых дроидов"),
            Txt($"В резерве бюро: {reserve} парт., приданы МВБ: {security}/5"),
            Btn("Передать Обороне", aim == 0, reserve <= 0),
            Btn("Придать МВБ", aim == 1, reserve <= 0 || security >= 5),
            Btn("Отозвать от МВБ", aim == 2, security <= 0, SWTrainingAccent.Neutral),
        };
        if (log != null)
        {
            rows.Add(Sec("Журнал бюро"));
            rows.Add(Dim(log));
        }
        return new SWTrainingFormPanel("Лицензии и дроиды", rows.ToArray());
    }

    public static SWTrainingStage[] LicenseOpen() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(BureauConsole, WeaponsTabs, 0), time: "10:00"),
        Press(TabRow(BureauConsole, WeaponsTabs, 0), time: "10:00"),
        Wait(LicenseRegime(0, "30"), "Тяжёлое вооружение под запретом: заявок на него нет и сборов тоже.", 4f, "10:00"),
        Aim(LicenseRegime(0, "30", 1), time: "10:00"),
        Press(LicenseRegime(0, "30", 1), time: "10:00"),
        Result(LicenseRegime(1, "42"),
            note: "Теперь тяжёлое вооружение идёт по лицензиям: появятся заявки, а сборы выросли. При «Свободно» заявок и сборов не было бы вовсе.", time: "10:00"));

    public static SWTrainingStage[] LicenseInspect() => Steps(
        Aim(TabRow(BureauConsole, WeaponsTabs, 1), time: "11:00"),
        Press(TabRow(BureauConsole, WeaponsTabs, 1), time: "11:00"),
        Wait(LicenseApp(false), "Наёмники просят партию тяжёлого вооружения и сразу предлагают деньги.", 4f, "11:00"),
        Aim(LicenseApp(false, 1), time: "11:00"),
        Press(LicenseApp(false, 1), time: "11:00"),
        Result(LicenseApp(true),
            note: "Осмотр стоил 300 кр и добавил контролю оборота 1. Заявитель сомнительный, а второй раз заявку не проверить.", time: "11:00"));

    public static SWTrainingStage[] LicenseDecide() => Steps(
        Aim(LicenseApp(true, 2), time: "11:05"),
        Press(LicenseApp(true, 2), time: "11:05"),
        Result(Form("Лицензии и дроиды", Sec("Заявки на лицензии"), Dim("Заявок нет. Лицензионные категории закрыты или очередь пуста.")),
            note: "Сомнительному заявителю отказано: пошлины нет, зато контроль оборота +2.", time: "11:05"),
        Result(LicenseApp(true),
            note: "Если одобрить, бюро получит 740 кр, но примерно в половине случаев часть партии уйдёт на чёрный рынок.", alternative: true, time: "11:05"),
        Case("Наёмники готовы заплатить главе бюро 1 100 кр лично, лишь бы партию оформили без вопросов.", LicenseApp(true), "11:05"),
        Aim(LicenseApp(true, aimBribe: true), true, "11:05"),
        Press(LicenseApp(true, aimBribe: true), true, "11:05"),
        Result(DroidRegistry(2, 4, log: "[11:00] Партия ЧВК «Красные вымпелы» оформлена без лишних вопросов."),
            note: "Подношение сразу оформило партию: 1 100 кр на личном счёте, контроль −3, личная коррупция растёт.", alternative: true, time: "11:05", continues: true));

    public static SWTrainingStage[] DroidTransfer() => Steps(
        Back("Наёмникам отказано, подношение не принято. В резерве две партии дроидов, МВБ уже держит четыре из пяти.", DroidRegistry(2, 4), "12:00"),
        Aim(DroidRegistry(2, 4, 1), time: "12:00"),
        Press(DroidRegistry(2, 4, 1), time: "12:00"),
        Wait(DroidRegistry(1, 5, log: "[12:00] Партия дроидов придана МВБ: облавы станут точнее."),
            "У МВБ теперь пять партий, больше не придать.", 4f, "12:00"),
        Aim(DroidRegistry(1, 5, 0, "[12:00] Партия дроидов придана МВБ: облавы станут точнее."), time: "12:05"),
        Press(DroidRegistry(1, 5, 0, "[12:00] Партия дроидов придана МВБ: облавы станут точнее."), time: "12:05"),
        Result(DroidRegistry(0, 5, log: "[12:00] Партия боевых дроидов передана Обороне."),
            note: "Последняя партия ушла армии и пополнила резерв обороны. Резерв бюро пуст: передавать нечего, а у МВБ партию можно только отозвать.", time: "12:05"),
        Case("Если партия нужнее в другом месте, её можно отозвать у МВБ.", DroidRegistry(0, 5), "12:10"),
        Aim(DroidRegistry(0, 5, 2), true, "12:10"),
        Press(DroidRegistry(0, 5, 2), true, "12:10"),
        Result(DroidRegistry(1, 4, log: "[12:00] Партия дроидов отозвана из МВБ в резерв бюро."),
            note: "Партия вернулась в резерв бюро, и её снова можно передать.", alternative: true, time: "12:10", continues: true));

    /// <summary>The close of the day for the registry: where the two batches went, and what is left to do.</summary>
    public static SWTrainingStage[] DroidResult() => Steps(
        Back("Партию у МВБ не отзывали: резерв бюро пуст, у МВБ пять партий из пяти.", DroidRegistry(0, 5), "13:00"),
        Aim(TabRow(BureauConsole, WeaponsTabs, 1), time: "13:00"),
        Press(TabRow(BureauConsole, WeaponsTabs, 1), time: "13:00"),
        Wait(Journal("Журнал бюро",
                "[12:00] Партия боевых дроидов передана Обороне.",
                "[12:00] Партия дроидов придана МВБ: облавы станут точнее.",
                "[10:00] Тяжёлое вооружение: режим «Лицензии»."),
            "К обеду журнал бюро сводит утро вместе. Отказ наёмникам записи не оставил: пропала только их заявка.", 4f, "13:00"),
        Result(DroidRegistry(0, 5),
            note: "Армия получила партию в резерв, у МВБ пять партий для облав. Новые партии в реестре появятся, например, после заказа «Гос-программа дроидов» во вкладке «Операции».", time: "13:00"));
}
