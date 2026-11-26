using System;
using System.Collections.Generic;

namespace Content.Client._SW._Senate.Training;

/// <summary>
/// Timelines of chapters 09А, 09Б and 12Б: production incidents and the economic policy of the economy ministry, and the
/// quarantine of the health ministry. Labels, statuses and announcements are those of the ministry work console, the
/// chancellor's approvals and the server systems; the story stays with Дунидин in the Корусант sector.
/// </summary>
public static partial class SWTrainingSamples
{
    // One array per set of buttons: rows compare them by reference.
    private static readonly string[] EconTabs = { "Экономика", "Карта экономики", "Программы" };
    private static readonly string[] PolicyLabels = { "Сдерживание", "Нейтрально", "Стимул" };
    private static readonly string[] ApproveReject = { "Одобрить", "Отклонить" };
    private static readonly string[] MinistryHealthTabs = { "Исследование", "Карта", "Медицинская сеть", "Карантин" };
    private static readonly string[] QuarantineWorlds = { "Дунидин", "Квенк", "Корусант", "Утрост" };
    private static readonly string[] ScopeLabels = { "Мир", "Весь сектор" };
    private static readonly string[] QuarantineHours = { "2 ч", "4 ч", "6 ч" };
    private static readonly string[] CorridorOpen = { "Коридор открыт" };
    private static readonly string[] CorridorClosed = { "Коридор закрыт" };

    /// <summary>A row of toggle buttons as the quarantine form has them: the chosen one is pressed, every one stays available.</summary>
    private static SWTrainingRow Toggles(string[] labels, int pressed, int target = -1) =>
        new(SWTrainingRowKind.Buttons, Buttons: labels, Current: pressed, TargetIndex: target, Target: target >= 0, Chosen: true);

    // ---- 09А: production incidents ------------------------------------------

    private const string StandardMaterials = "Материалы: Механические узлы 0.2 ед., Электронные компоненты 0.1 ед.   Работы после доставки: 2 ч";
    private const string FastMaterials = "Материалы: Механические узлы 0.2 ед., Электронные компоненты 0.1 ед., Промышленное оборудование 0.1 ед.   Работы после доставки: 1 ч";
    private const string NoSupply = "Недостаточно времени или доступных материалов для восстановления";

    private static List<SWTrainingRow> IncidentHead(string world, string recipe, string power, string status, string left) => new()
    {
        Choice(EconomyProgramTabs, 2),
        Txt($"Авария производства   {world}, Корусант"),
        Txt($"Мощность «{recipe}»: {power}"),
        Dim($"Повреждено оборудование производственной линии.   {status}   Осталось: {left} игровых ч"),
    };

    /// <summary>The active incident with both paid answers; an answer that cannot arrive in time is off with its reason.</summary>
    private static SWTrainingFormPanel IncidentCard(string world, string recipe, string left, string? standardStatus = null, string? fastStatus = null,
        bool aimStandard = false, bool aimFast = false, string status = "Действует")
    {
        var rows = IncidentHead(world, recipe, "60%", status, left);
        rows.Add(Txt("Стандартное восстановление — 600 кр"));
        rows.Add(Dim(standardStatus == null ? StandardMaterials : $"{StandardMaterials}   {standardStatus}"));
        rows.Add(Btn("Стандартное восстановление", aimStandard, standardStatus != null));
        rows.Add(Txt("Ускоренное восстановление — 1 050 кр"));
        rows.Add(Dim(fastStatus == null ? FastMaterials : $"{FastMaterials}   {fastStatus}"));
        rows.Add(Btn("Ускоренное восстановление", aimFast, fastStatus != null));
        return new SWTrainingFormPanel("Программы и поставки", rows.ToArray());
    }

    /// <summary>After payment the options give way to «Выполнение и поставки»: each material and then the work.</summary>
    private static SWTrainingFormPanel IncidentRestore(string world, string recipe, string left, string work, params string[] materials) =>
        IncidentRestoreAt("60%", "Действует", world, recipe, left, work, materials);

    /// <summary>The same card later on: after four hours the damage starts to ease by itself and the power climbs back.</summary>
    private static SWTrainingFormPanel IncidentRestoreAt(string power, string status, string world, string recipe, string left, string work, params string[] materials)
    {
        var rows = IncidentHead(world, recipe, power, status, left);
        rows.Add(Sec("Выполнение и поставки"));
        foreach (var material in materials)
            rows.Add(Dim(material));
        rows.Add(Dim(work));
        return new SWTrainingFormPanel("Программы и поставки", rows.ToArray());
    }

    private static SWTrainingFormPanel IncidentHistory(string place, string work) => Form("Программы и поставки",
        Choice(EconomyProgramTabs, 2),
        Dim("Сейчас экономических потрясений нет"),
        Sec("Последние потрясения"),
        Txt($"Авария производства — {place}"),
        Fine("Восстановление завершено"),
        Dim(work));

    public static SWTrainingStage[] IncidentOpen() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(MinistryConsole, EconTabs, 2), time: "10:00"),
        Press(TabRow(MinistryConsole, EconTabs, 2), time: "10:00"),
        Aim(TabRow("Программы и поставки", EconomyProgramTabs, 2), time: "10:00"),
        Press(TabRow("Программы и поставки", EconomyProgramTabs, 2), time: "10:00"),
        Result(IncidentCard("Дунидин", LineName, "6"),
            note: "На линии стерильных расходников в Дунидине сломалось оборудование: выпуск упал до 60%. Без ремонта он восстановится сам, но нескоро.", time: "10:00"));

    public static SWTrainingStage[] IncidentStandard() => Steps(
        Wait(IncidentCard("Дунидин", LineName, "6"), "Времени шесть часов, обычного ремонта хватит.", 4f, "10:05"),
        Aim(IncidentCard("Дунидин", LineName, "6", aimStandard: true), time: "10:05"),
        Press(IncidentCard("Дунидин", LineName, "6", aimStandard: true), time: "10:05"),
        Result(IncidentRestore("Дунидин", LineName, "5.9", "Ожидает материалов. Работы после доставки: 2 ч",
                "Механические узлы: получено 0 из 0.2 ед., в пути 0 ед.; доставка 0.8 ч",
                "Электронные компоненты: получено 0 из 0.1 ед., в пути 0 ед.; доставка 0.6 ч"),
            note: "600 кр списаны из бюджета министерства. Ремонт начнётся, когда узлы и электроника доедут до Дунидина.", time: "10:05"),
        Result(IncidentCard("Дунидин", LineName, "6", NoSupply, NoSupply),
            note: "Если нужных материалов нет поблизости или к Дунидину нет маршрута, обе кнопки отключены. Тогда остаётся естественное восстановление.",
            alternative: true, time: "10:05"));

    public static SWTrainingStage[] IncidentDelivery() => Steps(
        Back("Материалы нашлись, и маршрут к Дунидину есть: ремонт оплачен. Электроника на месте, узлы ещё в пути.",
            IncidentRestore("Дунидин", LineName, "5.4", "Материалы в пути. Работы после доставки: 2 ч",
                "Механические узлы: получено 0.1 из 0.2 ед., в пути 0.1 ед.; доставка 0.4 ч",
                "Электронные компоненты: получено 0.1 из 0.1 ед., в пути 0 ед.; доставлено"), "10:35"),
        Wait(IncidentRestore("Дунидин", LineName, "4", "Работы: выполнено 1 из 2 ч",
                "Механические узлы: получено 0.2 из 0.2 ед., в пути 0 ед.; доставлено",
                "Электронные компоненты: получено 0.1 из 0.1 ед., в пути 0 ед.; доставлено"),
            "Всё доставлено, бригада меняет оборудование.", 4f, "12:00"),
        Result(IncidentHistory("Дунидин", "Работы завершены: 2 ч"),
            note: "К часу дня линия снова работает в полную силу, авария ушла в «Последние потрясения».", time: "13:00"));

    public static SWTrainingStage[] IncidentResult() => Steps(
        Result(IncidentHistory("Дунидин", "Работы завершены: 2 ч"),
            note: "Платный ремонт вернул линию за три часа; без него авария продержалась бы ещё около трёх, до четырёх часов дня.", time: "13:00"),
        Result(IncidentRestoreAt("70%", "Естественное восстановление", "Дунидин", LineName, "1.5", "Ожидает транспорта. Работы после доставки: 2 ч",
                "Механические узлы: получено 0 из 0.2 ед., в пути 0 ед.; оценка доставки недоступна",
                "Электронные компоненты: получено 0.1 из 0.1 ед., в пути 0 ед.; доставлено"),
            note: "Оплата не гарантирует ремонт: если транспорт занят, материалы застрянут. К половине третьего авария слабеет сама, мощность уже 70%, и к четырём она закончится без ремонта.",
            alternative: true, time: "14:30"));

    public static SWTrainingStage[] IncidentFast() => Steps(
        Back("Дунидинскую линию починили в срок. Через день авария на Квенке, и до естественного восстановления всего два с половиной часа: обычный ремонт уже не успеет.",
            IncidentCard("Квенк", "Механические узлы", "2.5", NoSupply), "11:30"),
        Aim(IncidentCard("Квенк", "Механические узлы", "2.5", NoSupply, aimFast: true), time: "11:30"),
        Press(IncidentCard("Квенк", "Механические узлы", "2.5", NoSupply, aimFast: true), time: "11:30"),
        Wait(IncidentRestore("Квенк", "Механические узлы", "2.4", "Материалы в пути. Работы после доставки: 1 ч",
                "Механические узлы: получено 0.2 из 0.2 ед., в пути 0 ед.; доставлено",
                "Электронные компоненты: получено 0.05 из 0.1 ед., в пути 0.05 ед.; доставка 0.3 ч",
                "Промышленное оборудование: получено 0 из 0.1 ед., в пути 0.1 ед.; доставка 0.4 ч"),
            "1 050 кр списаны. Ускоренный ремонт везёт ещё и промышленное оборудование, зато работает час.", 4f, "11:35"),
        Result(IncidentHistory("Квенк", "Работы завершены: 1 ч"),
            note: "Оборудование доставили, за час линию подняли. Ускорение дороже и требует лишних материалов.", time: "13:00"));

    // ---- 09Б: economic policy ------------------------------------------------

    private static SWTrainingFormPanel CycleCard(string phase, string growth, float growthBar, string overheatStatus, int overheat, string policy,
        int current, int aim = -1) => Form("Экономика",
        Sec("Деловой цикл"),
        Txt($"{phase}   темп роста {growth}/ч"),
        Meter(growthBar),
        Sec("Перегрев"),
        Txt($"{overheatStatus}   {overheat}/100"),
        Meter(overheat / 100f, SWTrainingAccent.Bad),
        Sec("Экономическая политика"),
        Dim($"Текущая: {policy}"),
        Choice(PolicyLabels, current, aim));

    private static SWTrainingFormPanel StimulusAsk(bool pending, bool aim = false, string? log = null)
    {
        var rows = new List<SWTrainingRow> { Sec("Большой стимул") };
        if (pending)
            rows.Add(Warn("Ждёт одобрения Канцлера."));
        else
        {
            rows.Add(Btn("Запросить большой стимул", aim, accent: SWTrainingAccent.Bad));
            rows.Add(Dim("Рост экономики и перегрева. Одобрение Канцлера."));
        }
        if (log != null)
        {
            rows.Add(Sec("События"));
            rows.Add(Dim(log));
        }
        return new SWTrainingFormPanel("Экономика", rows.ToArray());
    }

    private static SWTrainingFormPanel StimulusApproval(int aim = -1) => Form("Согласования",
        Bad("Министр экономики предлагает запустить большой стимул."),
        Choice(ApproveReject, -1, aim));

    private const string StimulusLog = "Большой стимул: рост разогнан, перегрев подскочил.";

    public static SWTrainingStage[] PolicyRead() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(MinistryConsole, EconTabs, 0), time: "10:00"),
        Press(TabRow(MinistryConsole, EconTabs, 0), time: "10:00"),
        Result(CycleCard("Восстановление", "+1.2",.52f, "Напряжённо", 52, "Нейтрально", 1),
            note: "Экономика восстанавливается, перегрев уже 52 из 100. Политика задаёт, куда их толкать каждый час.", time: "10:00"));

    public static SWTrainingStage[] PolicyStimulus() => Steps(
        Aim(CycleCard("Восстановление", "+1.2",.52f, "Напряжённо", 52, "Нейтрально", 1, 2), time: "10:05"),
        Press(CycleCard("Восстановление", "+1.2",.52f, "Напряжённо", 52, "Нейтрально", 1, 2), time: "10:05"),
        Wait(CycleCard("Восстановление", "+1.2",.52f, "Напряжённо", 52, "Стимулирование", 2), "Курс сменился сразу, а цифры двигаются со временем.", 4f, "10:05"),
        Result(CycleCard("Рост", "+4.2", .82f, "Напряжённо", 58, "Стимулирование", 2),
            note: "За два часа темп поднялся выше трёх, и цикл перешёл в рост. Перегрев тоже пошёл вверх. На темп влияет и ставка Министерства финансов.", time: "12:05"));

    public static SWTrainingStage[] PolicyRestrain() => Steps(
        Aim(CycleCard("Рост", "+4.2", .82f, "Напряжённо", 58, "Стимулирование", 2, 0), time: "12:10"),
        Press(CycleCard("Рост", "+4.2", .82f, "Напряжённо", 58, "Стимулирование", 2, 0), time: "12:10"),
        Result(CycleCard("Восстановление", "+2.2",.62f, "Напряжённо", 54, "Сдерживание", 0),
            note: "Сдерживание остужает: рост замедляется, перегрев понемногу спадает.", time: "14:10"),
        new Step(ResultHold, Closeup("14:10"), CycleCard("Рецессия", "-3.0", .1f, "Здоровый", 40, "Стимулирование", 2),
            Notes: Heard("Экономика перегрелась - обвал! Республика входит в рецессию.", "Министерство экономики",
                "Если довести перегрев до 95, пузырь лопается: рост обваливается, начинается рецессия."), Alternative: true));

    public static SWTrainingStage[] StimulusRequest() => Steps(
        Back("Перегрев до 95 не дошёл: цикл в восстановлении, курс сдерживания.",
            CycleCard("Восстановление", "+2.2", .62f, "Напряжённо", 54, "Сдерживание", 0), "14:20"),
        Wait(StimulusAsk(false), "Большой стимул сильнее любого курса: рост подскочит сразу, а перегрев на 30.", 4f, "14:20"),
        Aim(StimulusAsk(false, true), time: "14:20"),
        Press(StimulusAsk(false, true), time: "14:20"),
        Result(StimulusAsk(true), note: "Запрос ушёл на согласование. Решает канцлер, а без канцлера спикер.", time: "14:20"),
        Result(StimulusAsk(false, log: StimulusLog), note: "Если нет ни канцлера, ни спикера, большой стимул запускается сразу, без согласования.",
            alternative: true, time: "14:20"));

    public static SWTrainingStage[] StimulusDecision() => Steps(
        Back("Канцлер на месте, поэтому стимул не запустился сам и ждёт его решения.", HandOut()),
        HandIn(),
        Wait(StimulusApproval(), "У канцлера в «Согласованиях» появился запрос министра.", 4f, "14:30"),
        Aim(StimulusApproval(0), time: "14:30"),
        Press(StimulusApproval(0), time: "14:30"),
        Result(CycleCard("Перегрев", "+6.0",1f, "Перегрев", 84, "Сдерживание", 0),
            note: "Рост разогнан, перегрев 84, и цикл перешёл в перегрев. Стимул не решает кризис сам: без сдержанности пузырь лопнет на 95.", time: "14:30"),
        Case("Канцлер может и не пойти на риск перегрева.", StimulusApproval(), "14:30"),
        Aim(StimulusApproval(1), true, "14:30"),
        Press(StimulusApproval(1), true, "14:30"),
        Result(StimulusAsk(false), note: "Запрос снят, цифры не изменились. Министр может попросить снова.", alternative: true, time: "14:30", continues: true));

    // ---- 12Б: quarantine --------------------------------------------------

    private const string DunidinZone = "Дунидин. Миров: 1. Минимальный медицинский охват: 64%.";
    private const string SectorZone = "Сектор: Корусант. Миров: 4. Минимальный медицинский охват: 41%.";
    private const string DunidinStock = "Минимальный запас пищи: 9.5 ч; лекарств: 3.2 ч.";
    private const string OpenConsequence = "Обычные перевозки остановятся. Входящие пища и лекарства разрешены.";
    private const string ClosedConsequence = "Обычные перевозки остановятся. Конвои Минздрава проходят.";
    private const string SectorConsequence = "Внешние перевозки остановятся, внутренние сохранятся. Входящие пища и лекарства разрешены. Конвои Минздрава проходят.";
    // Дунидин has one allowed crossing, the link to Корусант.
    private const string PendingConsequences = "Обычный импорт и экспорт: будут остановлены после одобрения. Переходов: 1. Минимальный запас продуктов: 9.5 ч; лекарств: 3.2 ч. Входящие поставки пищи и лекарств разрешены. Конвои Минздрава проходят; военная блокада остаётся в силе.";
    private const string ActiveConsequences = "Обычный импорт и экспорт: остановлены. Переходов: 1. Минимальный запас продуктов: 9.1 ч; лекарств: 3.4 ч. Входящие поставки пищи и лекарств разрешены. Конвои Минздрава проходят; военная блокада остаётся в силе.";
    private const string QuarantineBegun = "Введён карантин: Дунидин, на 4 игровых ч. Входящие поставки пищи и лекарств разрешены. Конвои Минздрава проходят.";

    /// <summary>The «Карантин» tab: the world list, the zone, the duration and the corridor, what will happen, and the request list below.</summary>
    private static SWTrainingFormPanel QuarantineForm(int hours = 0, bool corridor = true, bool sector = false, bool aimWorld = false, bool openWorld = false,
        int aimScope = -1, int aimTerms = -1, bool aimCorridor = false, bool aimRequest = false, bool requestOff = false, SWTrainingRow[]? list = null)
    {
        var rows = new List<SWTrainingRow>
        {
            Choose(string.Empty, QuarantineWorlds, 0, aimWorld, openWorld),
            Toggles(ScopeLabels, sector ? 1 : 0, aimScope),
            Toggles(QuarantineHours, hours, aimTerms),
            // The corridor is one toggle: pressed while open, released and renamed when closed.
            Toggles(corridor ? CorridorOpen : CorridorClosed, corridor ? 0 : -1, aimCorridor ? 0 : -1),
            Txt(sector ? SectorZone : DunidinZone),
            Dim(DunidinStock),
            Dim(sector ? SectorConsequence : corridor ? OpenConsequence : ClosedConsequence),
            Btn("Запросить карантин", aimRequest, requestOff),
        };
        rows.AddRange(list ?? new[] { Dim("Активные и ожидающие карантины: 0") });
        return new SWTrainingFormPanel("Карантин", rows.ToArray());
    }

    private static SWTrainingRow[] QuarantineList(string item, string details, string lift, bool aimLift = false) => new[]
    {
        Dim("Активные и ожидающие карантины: 1"),
        Btn(item, accent: SWTrainingAccent.Neutral),
        Dim($"Дунидин. Миров: 1. Коридор открыт.   {details}"),
        Btn(lift, aimLift, accent: SWTrainingAccent.Neutral),
    };

    private static SWTrainingFormPanel QuarantineApproval(bool pending, int aim = -1) => pending
        ? Form("Согласования", Warn("Дунидин: запрос карантина"), Dim(PendingConsequences), Choice(ApproveReject, -1, aim))
        : Form("Согласования", Warn("Дунидин: карантин, осталось 4.0 ч"), Dim(ActiveConsequences), Btn("Снять карантин", accent: SWTrainingAccent.Neutral));

    private static SWTrainingRow[] PendingList(bool aim = false) => QuarantineList("Дунидин: ожидает решения", PendingConsequences, "Отозвать запрос", aim);

    public static SWTrainingStage[] QuarantineZone() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(MinistryConsole, MinistryHealthTabs, 3), time: "11:00"),
        Press(TabRow(MinistryConsole, MinistryHealthTabs, 3), time: "11:00"),
        Aim(QuarantineForm(aimWorld: true), time: "11:00"),
        Press(QuarantineForm(aimWorld: true, openWorld: true), time: "11:00"),
        Result(QuarantineForm(),
            note: "Лихорадка в Дунидине растёт. Других миров в его системе нет, поэтому выбор только между миром и сектором. Лекарств там часа на три.", time: "11:00"),
        Result(QuarantineForm(sector: true),
            note: "«Весь сектор» закрыл бы четыре мира: внешние перевозки встанут, внутренние сохранятся.", alternative: true, time: "11:00"));

    public static SWTrainingStage[] QuarantineTerms() => Steps(
        Back("Масштаб оставлен прежним: карантин только для Дунидина.", QuarantineForm(), "11:02"),
        Aim(QuarantineForm(aimTerms: 1), time: "11:02"),
        Press(QuarantineForm(aimTerms: 1), time: "11:02"),
        Result(QuarantineForm(1), note: "Четыре игровых часа, коридор открыт: пища и лекарства продолжат поступать в Дунидин.", time: "11:02"),
        Case("Коридор можно и закрыть. Тогда обычные поставки пищи и лекарств в Дунидин тоже остановятся.", QuarantineForm(1), "11:02"),
        Aim(QuarantineForm(1, aimCorridor: true), true, "11:02"),
        Press(QuarantineForm(1, aimCorridor: true), true, "11:02"),
        Result(QuarantineForm(1, false), note: "Коридор закрыт: при запасе лекарств на три часа помощь дойдёт до Дунидина только с конвоями Минздрава.", alternative: true, time: "11:02", continues: true));

    public static SWTrainingStage[] QuarantineRequest() => Steps(
        Back("Министр оставил коридор открытым: срок четыре часа, пища и лекарства в Дунидин идут.", QuarantineForm(1), "11:03"),
        Aim(QuarantineForm(1, aimRequest: true), time: "11:03"),
        Press(QuarantineForm(1, aimRequest: true), time: "11:03"),
        new Step(ResultHold, Closeup("11:03"), QuarantineForm(1, requestOff: true, list: PendingList()),
            Notes: Heard("На рассмотрение канцлера направлен карантин: Дунидин.", "Министерство здравоохранения",
                "Пока это только запрос: перевозки идут как раньше. Утвердить собственный запрос нельзя.")),
        new Step(ResultHold, Closeup("11:03"), QuarantineForm(1, requestOff: true, list: QuarantineList("Дунидин: активен, 4 ч", ActiveConsequences, "Снять карантин")),
            Notes: Heard(QuarantineBegun, "Автопилот Сената", "Если нет ни канцлера, ни спикера, запрос утверждается сразу."), Alternative: true));

    public static SWTrainingStage[] QuarantineDecision() => Steps(
        Back("Канцлер на месте, поэтому запрос не утвердился сам и ждёт его решения.", HandOut()),
        HandIn(),
        Wait(QuarantineApproval(true), "Запрос пришёл канцлеру в «Согласования». Без канцлера его решает спикер.", 4f, "11:10"),
        Aim(QuarantineApproval(true, 0), time: "11:10"),
        Press(QuarantineApproval(true, 0), time: "11:10"),
        new Step(ResultHold, Closeup("11:10"), QuarantineApproval(false),
            Notes: Heard(QuarantineBegun, "Канцлер", "Четыре часа отсчитываются с одобрения, обычный транспорт к Дунидину встал. Снять карантин досрочно теперь может и канцлер.")),
        Case("Канцлер может и отказать, если считает карантин лишним.", QuarantineApproval(true), "11:10"),
        Aim(QuarantineApproval(true, 1), true, "11:10"),
        Press(QuarantineApproval(true, 1), true, "11:10"),
        Result(QuarantineForm(1),
            note: "У министра запрос исчез из списка, в журнале осталось «Карантин отклонён: Дунидин.». Перевозки продолжаются, можно подать новый.",
            alternative: true, time: "11:10", continues: true));

    private static SWTrainingFormPanel DunidinCondition() => Form("Медицинская сеть",
        Txt("Дунидин"),
        Dim("Заражение 18.4%. Изменение: -1.2 п.п./ч"),
        Fine("Сдерживание заражения иммунитетом: -0.8 п.п./ч"),
        Dim("Запас помощи: 2.6 ч"));

    public static SWTrainingStage[] QuarantineLift() => Steps(
        Back("Канцлер одобрил карантин, он действует с 11:10. В «Медицинской сети» видно: заражение в Дунидине падает. Его сбивают лечение и вакцинация, а карантин не даёт вспышке уйти дальше.",
            DunidinCondition(), "12:35"),
        Wait(QuarantineForm(1, requestOff: true, list: QuarantineList("Дунидин: активен, 2.5 ч", ActiveConsequences, "Снять карантин")),
            "Помощь дошла, запасы держатся. Министр решает снять карантин, не дожидаясь срока.", 4f, "12:40"),
        Aim(QuarantineForm(1, requestOff: true, list: QuarantineList("Дунидин: активен, 2.5 ч", ActiveConsequences, "Снять карантин", true)), time: "12:40"),
        Press(QuarantineForm(1, requestOff: true, list: QuarantineList("Дунидин: активен, 2.5 ч", ActiveConsequences, "Снять карантин", true)), time: "12:40"),
        new Step(ResultHold, Closeup("12:40"), QuarantineForm(1),
            Notes: Heard("Карантин снят: Дунидин.", "Министерство здравоохранения", "Заражение пошло вниз, и министр снял карантин раньше срока. Обычные перевозки к Дунидину снова идут.")),
        Result(QuarantineForm(1), note: "Если не снимать, в 15:10 срок истечёт и карантин закончится сам.", alternative: true, time: "15:10"));
}
