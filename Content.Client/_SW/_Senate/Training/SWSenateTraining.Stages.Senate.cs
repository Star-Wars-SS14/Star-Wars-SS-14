using System;
using System.Linq;

namespace Content.Client._SW._Senate.Training;

public enum SWTrainingRowKind
{
    Section,
    Text,
    Dim,
    Good,
    Warn,
    Bad,
    Button,
    Buttons,
    /// <summary>A drop-down list, as the game's OptionButton. Chosen shows it open with its items.</summary>
    Options,
    /// <summary>A check box; Chosen is its state.</summary>
    Check,
    /// <summary>A line with a counter and "-" and "+" buttons: Current 0 turns "-" off, Disabled turns "+" off.</summary>
    Counter,
    Field,
    Item,
    Bar,
}

public enum SWTrainingAccent
{
    Accent,
    Good,
    Bad,
    Neutral,
}

/// <summary>One line of an illustrated game window. Labels come from the game's windows.</summary>
public sealed record SWTrainingRow(
    SWTrainingRowKind Kind,
    string Text = "",
    string? Value = null,
    bool Target = false,
    bool Disabled = false,
    bool Chosen = false,
    SWTrainingAccent Accent = SWTrainingAccent.Accent,
    string[]? Buttons = null,
    int Current = -1,
    int TargetIndex = -1,
    float Amount = 0f);

/// <summary>Part of a game window, built from controls: a title and its lines in the window's order.</summary>
public sealed record SWTrainingFormPanel(string Title, SWTrainingRow[] Rows) : SWTrainingPanelState
{
    /// <summary>Equal content means the same controls stay on screen between stages, so a pointer's target does not jump.</summary>
    public bool Equals(SWTrainingFormPanel? other) => other != null && Title == other.Title && Rows.SequenceEqual(other.Rows);

    public override int GetHashCode() => HashCode.Combine(Title, Rows.Length);
}

/// <summary>
/// Timelines of chapters 01 to 07 with 02А and 03А. Every shot goes from the starting state through the action to a held
/// result. Labels and messages are those of the game's windows; the people, the sector and the bill form one story:
/// Тала Вен, senator of Корусант, who equips the clinics of Дунидин.
/// </summary>
public static partial class SWTrainingSamples
{
    private const string BillName = "О клиниках нижних уровней";
    private const string DefenseMinister = "Бран Голл";
    private const string BillAuthor = "Вейл Оран";

    // One array per set of buttons: rows compare them by reference.
    private static readonly string[] DraftModes = { "Свой закон", "Шаблоны" };
    private static readonly string[] DurationButtons = { "− час", "+ час" };
    private static readonly string[] MinistryTabs = { "Министерства", "Департаменты", "Бюро" };

    /// <summary>Worlds of the sector with an active economy, in the game's order: the anchor world first, then by name.</summary>
    private static readonly string[] SectorWorlds = { "Корусант", "Дунидин", "Квенк", "Утрост" };
    private const int ChosenWorld = 1;

    /// <summary>One state and how long it stays. Under reduced motion it stays at least <see cref="MinStill"/> seconds.</summary>
    private sealed record Step(
        float Hold,
        SWTrainingVisual Visual,
        SWTrainingPanelState? Panel = null,
        SWTrainingPointer Pointer = SWTrainingPointer.None,
        string? Popup = null,
        SWTrainingAnnotation[]? Notes = null,
        bool Alternative = false,
        bool Continues = false,
        bool Resumes = false);
    private const float MinStill = 3f;
    private const float ResultHold = SWSenateTrainingScript.MinResultHold;

    private static SWTrainingStage[] Steps(params Step[] steps)
    {
        var stages = new SWTrainingStage[steps.Length];
        var normal = 0f;
        var reduced = 0f;
        for (var i = 0; i < steps.Length; i++)
        {
            var step = steps[i];

            // One console throughout these chapters: its reader stays on the post between stages and between shots.
            var visual = step.Visual.Scene == SWTrainingScene.Console
                ? step.Visual with { Scene = SWTrainingScene.ConsoleMounted }
                : step.Visual;

            // An alternative opens its case unless it is an action or explicitly carries the case on; an action of an
            // alternative case is always preceded by that case's opening stage.
            var opens = step.Alternative && !step.Continues && step.Pointer == SWTrainingPointer.None;
            stages[i] = new SWTrainingStage(normal, reduced, visual, step.Panel, step.Pointer, step.Popup, step.Notes, step.Alternative, opens, step.Resumes);
            normal += step.Hold;
            reduced += MathF.Max(MinStill, step.Hold);
        }

        return stages;
    }

    // ---- steps ------------------------------------------------------------

    private static SWTrainingVisual CardAt(int frame) =>
        new(SWTrainingScene.ConsoleMounted, new[] { new SWTrainingOverlay(SWTrainingArt.HandId, 440, 142, frame) });

    private static Step HandOut() => new(1.5f, CardAt(0));

    private static Step HandIn() => new(1.5f, CardAt(1));

    private static Step Picture(SWTrainingVisual visual, float hold = 3f, SWTrainingAnnotation[]? notes = null) => new(hold, visual, Notes: notes);

    private static Step Aim(SWTrainingPanelState panel, bool alternative = false, string? time = null) =>
        new(2f, Closeup(time), panel, SWTrainingPointer.Travel, Alternative: alternative);

    private static Step Press(SWTrainingPanelState panel, bool alternative = false, string? time = null) =>
        new(1f, Closeup(time), panel, SWTrainingPointer.Press, Alternative: alternative);

    private static Step Result(SWTrainingPanelState panel, string? popup = null, string? note = null, bool alternative = false, string? time = null, bool continues = false) =>
        new(ResultHold, Closeup(time), panel, Popup: popup, Notes: note == null ? null : ScreenNote(note), Alternative: alternative, Continues: continues);

    /// <summary>
    /// Opens another case that starts with an action: the scene as it stands before that action, with the condition
    /// said. The aim, the press and the results that follow carry this case on.
    /// </summary>
    private static Step Case(string condition, SWTrainingPanelState before, string? time = null) =>
        new(4f, Closeup(time), before, Notes: ScreenNote(condition), Alternative: true);

    private static Step Wait(SWTrainingPanelState panel, string? note = null, float hold = 4f, string? time = null) =>
        new(hold, Closeup(time), panel, Notes: note == null ? null : ScreenNote(note));

    /// <summary>
    /// Opens a shot that follows another case: the main line's state, before anything is done, with that state named.
    /// </summary>
    private static Step Back(string state, SWTrainingPanelState panel, string? time = null) =>
        new(4f, Closeup(time), panel, Notes: ScreenNote(state), Resumes: true);

    /// <summary>The same for a shot that starts on a picture or with the card: that first step names the main state.</summary>
    private static Step Back(string state, Step first) => first with { Notes = ScreenNote(state), Resumes = true };

    private static SWTrainingVisual ChartView(int frame = 0) =>
        new(SWTrainingScene.Console, new[] { new SWTrainingOverlay(SWTrainingArt.ChartFragment, 150, 20, frame) });

    private static SWTrainingOverlay Figure(SWTrainingArt art, int x, int frame = 0) => new(art, x, SWSenateTrainingArt.SlotY, frame);

    // ---- rows -------------------------------------------------------------

    private static SWTrainingRow Sec(string text) => new(SWTrainingRowKind.Section, text);

    private static SWTrainingRow Txt(string text) => new(SWTrainingRowKind.Text, text);

    private static SWTrainingRow Dim(string text) => new(SWTrainingRowKind.Dim, text);

    private static SWTrainingRow Fine(string text) => new(SWTrainingRowKind.Good, text);

    private static SWTrainingRow Warn(string text) => new(SWTrainingRowKind.Warn, text);

    private static SWTrainingRow Bad(string text) => new(SWTrainingRowKind.Bad, text);

    private static SWTrainingRow Btn(string text, bool target = false, bool disabled = false, SWTrainingAccent accent = SWTrainingAccent.Accent) =>
        new(SWTrainingRowKind.Button, text, Target: target, Disabled: disabled, Accent: accent);

    private static SWTrainingRow Fld(string label, string value, bool target = false) => new(SWTrainingRowKind.Field, label, value, target);

    private static SWTrainingRow Item(string name, string status, bool chosen = false, bool target = false) =>
        new(SWTrainingRowKind.Item, name, status, target, Chosen: chosen);

    private static SWTrainingRow Meter(float amount, SWTrainingAccent accent = SWTrainingAccent.Accent) =>
        new(SWTrainingRowKind.Bar, Amount: amount, Accent: accent);

    private static SWTrainingRow Choice(string[] labels, int current = -1, int target = -1) =>
        new(SWTrainingRowKind.Buttons, Buttons: labels, Current: current, TargetIndex: target, Target: target >= 0);

    private static SWTrainingRow WorldList(int current, bool target = false, bool open = false) =>
        new(SWTrainingRowKind.Options, "Мир", Target: target, Chosen: open, Buttons: SectorWorlds, Current: current);

    private static SWTrainingFormPanel Form(string title, params SWTrainingRow[] rows) => new(title, rows);

    // ---- shared windows ---------------------------------------------------

    private static SWTrainingFormPanel Office(string position, bool inOffice, bool aim = false) => inOffice
        ? Form("Должность", Txt(position), Fine("Статус: в должности"), Btn("Сложить полномочия", accent: SWTrainingAccent.Neutral))
        : Form("Должность", Txt(position), Dim("Статус: не в должности"), Btn("Вступить в должность", aim, accent: SWTrainingAccent.Good));

    /// <summary>The first free sector starts selected; Тала Вен picks Корусант, and only then claims it.</summary>
    private static SWTrainingFormPanel SectorChoice() => Form("СЕНАТ / ВЫБОР ПРЕДСТАВИТЕЛЬСТВА",
        Item("Тайтон", "Свободно", true),
        Item("Корусант", "Свободно", target: true),
        Dim("КАРТА СЕНАТОРА"),
        Txt("Выбрано: Тайтон"),
        Btn("Занять место"));

    private static SWTrainingFormPanel SectorSelect(bool claimed, bool aim = false) => claimed
        ? Form("СЕНАТ / ВЫБОР ПРЕДСТАВИТЕЛЬСТВА",
            Item("Тайтон", "Свободно"),
            Item("Корусант", "Место занято", true),
            Dim("КАРТА СЕНАТОРА"),
            Txt("Ваше представительство: Корусант"),
            Btn("Занять место", disabled: true))
        : Form("СЕНАТ / ВЫБОР ПРЕДСТАВИТЕЛЬСТВА",
            Item("Тайтон", "Свободно"),
            Item("Корусант", "Свободно", true),
            Dim("КАРТА СЕНАТОРА"),
            Txt("Выбрано: Корусант"),
            Btn("Занять место", aim));

    /// <summary>The whole way from the first free sector to the claimed one.</summary>
    private static Step[] ClaimSteps() => new[]
    {
        HandOut(),
        HandIn(),
        Aim(SectorChoice()),
        Press(SectorChoice()),
        Aim(SectorSelect(false, true)),
        Press(SectorSelect(false, true)),
        Result(SectorSelect(true), "Сектор закреплён: Корусант.", "Корусант теперь за Талой Вен: место занято, второй сенатор его не получит."),
    };

    // The real programme «Сеть клиник»: cost, materials, work and effect as in the prototype.
    private const string ClinicsName = "Сеть клиник";
    private const string ClinicsText = "Оснащение районных клиник на выбранном мире.";

    private static SWTrainingFormPanel ProgramOffer(int world, string budget, bool aimWorld = false, bool openWorld = false, bool aimFund = false, bool canFund = true) => Form(string.Empty,
        Txt(budget),
        WorldList(world, aimWorld, openWorld),
        Txt(ClinicsName),
        Dim(ClinicsText),
        Sec("Материалы"),
        Dim("Медицинское оборудование 0.25 ед.   Строительные модули 0.3 ед.   Медицинские расходники 0.5 ед."),
        Dim("Срок работ: 3 ч"),
        Btn("Финансировать — 3000 кр.", aimFund, !canFund));

    private static SWTrainingFormPanel ProgramRunning(string budget, string status, string supply, float received, string work, float worked) => Form(string.Empty,
        Txt(budget),
        Txt(ClinicsName),
        Dim(SectorWorlds[ChosenWorld]),
        Txt(status),
        Sec("Снабжение"),
        Dim(supply),
        Meter(received),
        Dim(work),
        Meter(worked, SWTrainingAccent.Neutral));

    private const string SupplyNone = "Оборудование 0 / 0.25   Модули 0 / 0.3   Расходники 0 / 0.5   В пути: 0.3";
    private const string SupplyHalf = "Оборудование 0.25 / 0.25   Модули 0.3 / 0.3   Расходники 0 / 0.5   В пути: 0.5";
    private const string SupplyAll = "Оборудование 0.25 / 0.25   Модули 0.3 / 0.3   Расходники 0.5 / 0.5";

    private static SWTrainingFormPanel Draft(string name, string points, string? error, bool hasChange, bool aimAdd = false, bool aimSubmit = false, string change = "Здравоохранение: +2")
    {
        return Form("Разработка",
            Choice(DraftModes, 0),
            Fld("Название законопроекта", name),
            Sec("Изменения"),
            Btn("Добавить показатель", aimAdd),
            hasChange ? Dim(change) : Dim("Изменений нет"),
            Txt(points),
            error == null ? Dim(string.Empty) : Bad(error),
            Btn("Внести законопроект", aimSubmit, error != null));
    }

    private static SWTrainingFormPanel SessionDraft(string title, string? time, bool canPropose = false, bool aim = false, string? error = null)
    {
        var rows = new SWTrainingRow[]
        {
            Txt(title),
            time == null ? Dim("Выберите время в расписании.") : Dim(time),
            Choice(DurationButtons),
            error == null ? Dim(string.Empty) : Bad(error),
            Btn("Предложить заседание", aim, !canPropose),
        };
        return Form("Расписание", rows);
    }

    private static SWTrainingFormPanel SessionPending(bool aimConfirm = false) => Form("Расписание",
        Txt($"{BillName} — ожидает подтверждения"),
        Dim("10:00–12:00"),
        Btn("Подтвердить", aimConfirm, accent: SWTrainingAccent.Good),
        Btn("Отклонить", accent: SWTrainingAccent.Bad));

    private static SWTrainingFormPanel SessionConfirmed() => Form("Расписание",
        Fine($"{BillName} — подтверждено"),
        Dim("10:00–12:00"));

    private static SWTrainingFormPanel Signature(bool hasVeto, bool aimSign = false, bool aimVeto = false)
    {
        var sign = Btn("Подписать закон", aimSign);
        return hasVeto
            ? Form("На подпись", Txt(BillName), Dim($"№ 12, автор {ExampleName}"), Dim("Здравоохранение: +2"), sign, Btn("Наложить вето", aimVeto, accent: SWTrainingAccent.Bad))
            : Form("На подпись", Txt(BillName), Dim($"№ 12, автор {ExampleName}"), Dim("Здравоохранение: +2"), sign);
    }

    /// <summary>After the claim the portfolio is taken: the list says so, and the button is off, as in the game.</summary>
    private static SWTrainingFormPanel MinistryClaimed() => Form("СЕНАТ / НАЗНАЧЕНИЕ В ИСПОЛНИТЕЛЬНУЮ ВЛАСТЬ",
        Choice(MinistryTabs, 0),
        Item("Министерство обороны", "Занято", true),
        Item("Министерство финансов", "Занято"),
        Dim($"Занимает: {DefenseMinister}"),
        Dim("КАРТА МИНИСТРА"),
        Txt("Назначение закреплено: Министерство обороны"),
        Btn("Занять портфель", disabled: true));

    private static SWTrainingFormPanel Ministries(string footer, bool aimClaim = false, bool canClaim = true, bool departments = false, bool aimTab = false)
    {
        var tabRow = Choice(MinistryTabs, departments ? 1 : 0, aimTab ? 1 : -1);
        return departments
            ? Form("СЕНАТ / НАЗНАЧЕНИЕ В ИСПОЛНИТЕЛЬНУЮ ВЛАСТЬ",
                tabRow,
                Item("Департамент здравоохранения", "Недоступно", true),
                Dim("Сначала должны быть заняты все министерства - они важнее."),
                Dim("КАРТА МИНИСТРА"),
                Txt(footer),
                Btn("Занять портфель", disabled: true))
            : Form("СЕНАТ / НАЗНАЧЕНИЕ В ИСПОЛНИТЕЛЬНУЮ ВЛАСТЬ",
                tabRow,
                Item("Министерство обороны", "Свободно", true),
                Item("Министерство финансов", "Занято"),
                Dim("КАРТА МИНИСТРА"),
                Txt(footer),
                Btn("Занять портфель", aimClaim, !canClaim));
    }

    private static readonly string[] DefconLabels = { "5 Мир", "4 Готовность", "3 Тревога", "2 Сбор сил", "1 Война" };

    private static SWTrainingFormPanel Defense(string level, int current, int aim = -1, string? pending = null)
    {
        var header = pending == null ? Txt(level) : Warn($"Ожидает одобрения Канцлера: {pending}");
        return Form("СТРАТЕГИЧЕСКОЕ КОМАНДОВАНИЕ",
            Txt(level),
            pending == null ? Dim(string.Empty) : header,
            Dim("Повышение до DEFCON 1–2 — через Канцлера."),
            Choice(DefconLabels, current, aim));
    }

    private static SWTrainingFormPanel BorderState(string status, int pressure, int weariness) => Form(string.Empty,
        Sec("Давление на границе"),
        Txt($"{status}   {pressure}/100"),
        Meter(pressure / 100f, SWTrainingAccent.Bad),
        Sec("Усталость от войны"),
        Txt($"{weariness}/100"),
        Meter(weariness / 100f, SWTrainingAccent.Neutral));

    private static SWTrainingFormPanel WarRequest(bool aimApprove = false, bool aimReject = false) => Form(string.Empty,
        Bad("Министр обороны предлагает поднять боеготовность до: Война."),
        Btn("Одобрить", aimApprove, accent: SWTrainingAccent.Bad),
        Btn("Отклонить", aimReject, accent: SWTrainingAccent.Neutral));

    // ---- 01 ---------------------------------------------------------------

    private static SWTrainingFormPanel DeskHeader(string time) => Form(string.Empty,
        Dim("РЕСПУБЛИКА / ГОЛОНЕТ"),
        Btn("Профиль"),
        Txt(time));

    /// <summary>The console first and the figure over it, as in the art owner's joint composition.</summary>
    private static SWTrainingVisual AtWorkConsole(int figureFrame, int consoleFrame) => new(SWTrainingScene.ShiftRoom, new[]
    {
        new SWTrainingOverlay(SWTrainingArt.WorkConsole, SWSenateTrainingArt.WorkConsoleX, SWSenateTrainingArt.WorkConsoleY, consoleFrame),
        new SWTrainingOverlay(SWTrainingArt.FigSpeakerConsole, SWSenateTrainingArt.WorkFigureX, SWSenateTrainingArt.WorkFigureY, figureFrame),
    });

    public static SWTrainingStage[] GeneralTime() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Coruscant, Array.Empty<SWTrainingOverlay>()), 2f),
        Picture(new SWTrainingVisual(SWTrainingScene.Hall, Array.Empty<SWTrainingOverlay>()), 2f),
        Picture(AtWorkConsole(0, 0), 1.5f),
        Picture(AtWorkConsole(1, 0), 1.5f),
        Picture(AtWorkConsole(1, 1), 2f),
        Result(DeskHeader("День 1, 09:30"), note: "Утро первого дня: рабочий день идёт, решения принимаются."),
        Result(DeskHeader("День 1: ночь"), note: "Ночью основные решения недоступны. Дождитесь утра.", alternative: true));

    public static SWTrainingStage[] GeneralCard() => Steps(
        Back("Утро первого дня, рабочий день идёт.", HandOut()),
        HandIn(),
        Result(Office("Спикер Сената", false), note: "Карта вставлена, но статус ещё прежний: должность занимают отдельно."));

    public static SWTrainingStage[] GeneralSeat() => Steps(ClaimSteps());

    public static SWTrainingStage[] GeneralStages() => Steps(
        Wait(ProgramRunning("Бюджет сектора: 2000 кр.", "Ожидает материалов", SupplyNone, 0f, "Работы: 0 / 3 ч", 0f), "Программа оплачена, но материалы ещё в пути."),
        Result(ProgramRunning("Бюджет сектора: 2000 кр.", "Ожидает материалов", SupplyHalf, 2f / 3f, "Работы: 0 / 3 ч", 0f),
            note: "Часть материалов дошла. Пока не придут все, работы не начнутся."));

    /// <summary>A cut from the wide plan to the corridor floor: both stand on it, and the speaker's gesture is towards the usher.</summary>
    public static SWTrainingStage[] GeneralColleague() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Hall, Array.Empty<SWTrainingOverlay>()), 3f),
        Picture(new SWTrainingVisual(SWTrainingScene.Entrance, new[] { Figure(SWTrainingArt.FigSpeaker, SWSenateTrainingArt.SlotAX), Figure(SWTrainingArt.FigUsher, SWSenateTrainingArt.SlotBX) }), 3f,
            ScreenNote("Спикеру нужен порядок в зале. Это забота пристава.")),
        Picture(new SWTrainingVisual(SWTrainingScene.Entrance, new[] { Figure(SWTrainingArt.FigSpeaker, SWSenateTrainingArt.SlotAX, 1), Figure(SWTrainingArt.FigUsher, SWSenateTrainingArt.SlotBX) }), ResultHold,
            ScreenNote("Спикер просит пристава закрыть галерею до начала заседания.")));

    // ---- 02 ---------------------------------------------------------------

    public static SWTrainingStage[] SenatorSeat() => Steps(ClaimSteps());

    public static SWTrainingStage[] SenatorDesk() => Steps(
        HandOut(),
        HandIn(),
        Result(Form("Должность", Txt("Сенатор сектора Корусант"), Fine("Статус: в должности")), note: "Рабочий стол узнал карту: вы говорите от имени Корусанта."));

    public static SWTrainingStage[] SenatorCompare() => Steps(
        Aim(ProgramOffer(0, "Бюджет сектора: 5000 кр.", aimWorld: true)),
        Press(ProgramOffer(0, "Бюджет сектора: 5000 кр.", aimWorld: true, openWorld: true)),
        new Step(3f, Closeup(), ProgramOffer(ChosenWorld, "Бюджет сектора: 5000 кр.", openWorld: true)),
        Result(ProgramOffer(ChosenWorld, "Бюджет сектора: 5000 кр."), note: "В Дунидине не хватает клиник. Выбор мира ещё ничего не заказывает."),
        Result(ProgramOffer(ChosenWorld, "Бюджет сектора: 5000 кр.   Ожидает утверждения", canFund: false),
            note: "Пока бюджет дня не утверждён, финансировать нельзя.", alternative: true));

    public static SWTrainingStage[] SenatorFund() => Steps(
        Back("Бюджет дня утверждён: у сектора 5000 кр, выбран Дунидин.", ProgramOffer(ChosenWorld, "Бюджет сектора: 5000 кр.")),
        Aim(ProgramOffer(ChosenWorld, "Бюджет сектора: 5000 кр.", aimFund: true)),
        Press(ProgramOffer(ChosenWorld, "Бюджет сектора: 5000 кр.", aimFund: true)),
        Result(ProgramRunning("Бюджет сектора: 2000 кр.", "Ожидает материалов", SupplyNone, 0f, "Работы: 0 / 3 ч", 0f),
            note: "3000 кр списаны с бюджета сектора, материалы отправлены в Дунидин."),
        Result(ProgramOffer(ChosenWorld, "Бюджет сектора: 500 кр.", canFund: false),
            note: "Если в бюджете меньше нужной суммы, кнопка отключена и заказа нет.", alternative: true));

    public static SWTrainingStage[] SenatorWait() => Steps(
        Back("Программа оплачена, в бюджете сектора осталось 2000 кр. Часть материалов дошла, расходники ещё в пути.",
            ProgramRunning("Бюджет сектора: 2000 кр.", "Ожидает материалов", SupplyHalf, 2f / 3f, "Работы: 0 / 3 ч", 0f)),
        Wait(ProgramRunning("Бюджет сектора: 2000 кр.", "Идут работы", SupplyAll, 1f, "Работы: 1.5 / 3 ч", 0.5f), "Всё доставлено, клиники оснащают."),
        Result(ProgramRunning("Бюджет сектора: 2000 кр.", "Завершено", SupplyAll, 1f, "Работы: 3 / 3 ч", 1f),
            note: "Работы закончены. Клиникам дальше нужны поставки лекарств и обслуживание."));

    // ---- 02А --------------------------------------------------------------

    private static readonly string[] DilemmaModes = { "Внебюджетные", "Бюджетные", "Программы", "Сводка решений" };
    private const string PlagueTitle = "Вспышка болезни";
    private const string PlagueText = "В переполненных жилых ульях нижних уровней сектора зафиксирована вспышка риммианской лихорадки.";
    private const string PlagueFail = "Пока администрация тянула с решением, лихорадка вырвалась за пределы нижних уровней - больницы переполнены, город охвачен страхом";
    private const string Kickback = "Присвоить средства (личный счёт +1000 кр)";

    /// <summary>
    /// The dilemma card as the work desk draws it: every real option with its scaled effects, the private kickback the
    /// owner also sees, and the failure warning while nothing is chosen. After a choice the buttons are off.
    /// </summary>
    private static SWTrainingFormPanel PlagueCard(int aim = -1, int chosen = -1, bool canResolve = true, bool ministry = true, string time = "Решить до 14:00")
    {
        string Mark(string label, int index) => chosen == index ? label + " (выбрано)" : label;
        var off = !canResolve || chosen >= 0;
        var rows = new System.Collections.Generic.List<SWTrainingRow>
        {
            Choice(DilemmaModes, 0),
            Txt($"{PlagueTitle}   {time}"),
            Dim(PlagueText),
            Btn(Mark("Ввести карантин", 0), aim == 0, off),
            Dim("Здоровье +4   Эконом. -2"),
            Btn(Mark("Запросить помощь Республики", 1), aim == 1, off),
            Dim("Здоровье +2   Общество +1"),
            Btn(Mark("Списать на панику", 2), aim == 2, off),
            Dim("Эконом. +1   Здоровье -2"),
            Btn(Mark("Передать профильному министерству", 3), aim == 3, off || !ministry),
            Dim(ministry ? "Решает: Министерство здравоохранения. Эффект отложен." : "Министерство здравоохранения: нет министра."),
        };
        if (!off)
            rows.Add(Btn(Kickback, accent: SWTrainingAccent.Neutral));

        // As the desk card does: while nothing is chosen, the failure and its effects are shown in advance.
        if (chosen < 0)
        {
            rows.Add(Bad("Срочно! При отсутствии решения наступит провал:"));
            rows.Add(Bad(PlagueFail));
            rows.Add(Dim("Здоровье -6   Общество -4"));
        }

        return new SWTrainingFormPanel("Дилеммы", rows.ToArray());
    }

    private static SWTrainingFormPanel DilemmaSummary(string status) => Form("Дилеммы",
        Choice(DilemmaModes, 3),
        Txt($"{PlagueTitle}   Корусант"),
        Dim(status));

    private static SWTrainingFormPanel HospitalCard(string available, int small, int large, bool canLarge, int aim = -1, int chosen = -1)
    {
        string Mark(string label, int index) => chosen == index ? label + " (выбрано)" : label;
        var off = chosen >= 0;
        return Form("Дилеммы",
            Choice(DilemmaModes, 1),
            Txt($"Доступно вам: {available} кр."),
            Txt("Строительство больницы   Решить до 14:00"),
            Dim("Коек не хватает, ближайшая современная клиника в трёх часах лёта."),
            Btn(Mark($"Малая клиника - {small} кр.", 0), aim == 0, off),
            Dim("Здоровье +2"),
            Btn(Mark($"Крупный госпиталь - {large} кр.", 1), aim == 1, off || !canLarge),
            Dim("Здоровье +4   Общество +1"));
    }

    public static SWTrainingStage[] DilemmaOpen() => Steps(
        HandOut(),
        HandIn(),
        Aim(Form("Рабочий стол", Btn("Дилеммы", true)), time: "10:10"),
        Press(Form("Рабочий стол", Btn("Дилеммы", true)), time: "10:10"),
        Result(PlagueCard(), note: "Утром в Корусанте вспыхнула лихорадка. Решать её сенатору сектора, и до 14:00.", time: "10:10"));

    public static SWTrainingStage[] DilemmaWeigh() => Steps(
        Wait(PlagueCard(), "Карантин остановит болезнь, но ударит по экономике.", 4f, "10:12"),
        Wait(PlagueCard(), "Помощь Республики мягче для жителей, но слабее против лихорадки.", 4f, "10:12"),
        Result(PlagueCard(), note: "Если списать на панику, казна сэкономит, а люди заболеют. Передать можно, только если в министерстве есть министр.", time: "10:12"),
        Result(PlagueCard(ministry: false), note: "Без министра здравоохранения передача отключена.", alternative: true, time: "10:12"));

    public static SWTrainingStage[] DilemmaChoose() => Steps(
        Back("Министр здравоохранения на месте, а решения по лихорадке ещё нет.", PlagueCard(), "10:15"),
        Aim(PlagueCard(0), time: "10:15"),
        Press(PlagueCard(0), time: "10:15"),
        Result(PlagueCard(chosen: 0), note: "Карантин выбран. Изменить решение уже нельзя, а подействует оно в 15:00.", time: "10:15"),
        Case("Пока выбор не сделан, при министре здравоохранения на месте дело можно не решать самому, а передать ему.", PlagueCard(), "10:15"),
        Aim(PlagueCard(3), true, "10:15"),
        Press(PlagueCard(3), true, "10:15"),
        Result(PlagueCard(chosen: 3, time: "Передан в профильное министерство"),
            note: "Решать будет министр здравоохранения. Пока жители ждут, общество сектора немного проседает.", alternative: true, time: "10:15", continues: true));

    /// <summary>The whole shot is the case where nobody chooses; it opens with that condition.</summary>
    public static SWTrainingStage[] DilemmaDeadline() => Steps(
        new Step(4f, Closeup("13:55"), PlagueCard(), Notes: ScreenNote("Если весь день откладывать и ничего не выбрать, к 13:55 до срока остаются минуты."), Alternative: true),
        Result(PlagueCard(canResolve: false),
            note: "В 14:00 выбор закрылся. Варианты на месте, но отключены, решения нет.", alternative: true, time: "14:00", continues: true),
        Result(DilemmaSummary("Провалено"), note: "В 15:00 провал применён полностью: здоровье и общество сектора упали.", alternative: true, time: "15:00", continues: true));

    public static SWTrainingStage[] DilemmaResult() => Steps(
        Back("Тала Вен выбрала карантин утром, до срока. Пока он только объявлен.", DilemmaSummary("Решение принято"), "14:50"),
        Result(DilemmaSummary("Применено"), note: "В 15:00 карантин применён к сектору: здоровье растёт, экономика проседает.", time: "15:00"));

    public static SWTrainingStage[] DilemmaBudget() => Steps(
        Aim(HospitalCard("4000", 2500, 5000, false, 0), time: "09:40"),
        Press(HospitalCard("4000", 2500, 5000, false, 0), time: "09:40"),
        Result(HospitalCard("1500", 2500, 5000, false, chosen: 0), note: "Малая клиника оплачена из доли сектора сразу. На госпиталь денег не хватало, его кнопка отключена.", time: "09:40"),
        Result(HospitalCard("4000", 2875, 5750, false), note: "В секторе с высокой коррупцией те же стройки обходятся дороже.", alternative: true, time: "09:40"));

    // ---- 03 ---------------------------------------------------------------

    public static SWTrainingStage[] BillOpen() => Steps(
        HandOut(),
        HandIn(),
        Aim(Form("Рабочий стол", Btn("Разработка", true))),
        Press(Form("Рабочий стол", Btn("Разработка", true))),
        Result(Draft(string.Empty, "Очки: 10 из бюджета 10", "Укажите название законопроекта.", false), note: "Раздел виден сенатору в должности. Подача идёт только днём."));

    public static SWTrainingStage[] BillEdit() => Steps(
        new Step(3f, Closeup(), Draft(BillName, "Очки: 10 из бюджета 10", "Добавьте ненулевое изменение.", false, aimAdd: true), SWTrainingPointer.Travel),
        Press(Draft(BillName, "Очки: 10 из бюджета 10", "Добавьте ненулевое изменение.", false, aimAdd: true)),
        Result(Draft(BillName, "Очки: 8 из бюджета 10", null, true), note: "Закон поддержит клиники Корусанта: здравоохранение +2 стоит два очка."));

    public static SWTrainingStage[] BillLimit() => Steps(
        Wait(Draft(BillName, "Очки: -2 из бюджета 10", "Превышен бюджет очков.", true, change: "Здравоохранение: +6   Общество: +6"), "Тала Вен добавила слишком много: кнопка отключена."),
        Result(Draft(BillName, "Очки: 8 из бюджета 10", null, true), note: "Изменения уменьшены, проект укладывается в бюджет очков."));

    public static SWTrainingStage[] BillSubmit() => Steps(
        Aim(Draft(BillName, "Очки: 8 из бюджета 10", null, true, aimSubmit: true)),
        Press(Draft(BillName, "Очки: 8 из бюджета 10", null, true, aimSubmit: true)),
        Result(Form("Повестка", Txt(BillName), Dim("Ожидает распределения в повестку")), "Законопроект внесён в повестку.",
            "Изменения небольшие, поэтому проект идёт прямо в повестку, без комитета."),
        Result(Draft("О ремонте доков", "Очки: 8 из бюджета 10", null, true), "У вас уже есть законопроект на рассмотрении.",
            "Пока первый проект на рассмотрении, второй не принимается.", true));

    private static SWTrainingFormPanel Agenda(string status, string name = BillName, string author = ExampleName, int number = 12) => Form("Повестка",
        Txt(name),
        Dim($"№ {number}, автор {author}"),
        Fine(status));

    public static SWTrainingStage[] BillPathStages() => Steps(
        Back("Законопроект о клиниках у Талы Вен единственный. Он в повестке и ждёт, пока спикер поставит его на заседание.",
            Agenda("Ожидает распределения в повестку"), "10:30"),
        Wait(Agenda("На голосовании"), "Заседание идёт, сенаторы голосуют.", 3f, "11:00"),
        Wait(Agenda("Прошёл голосование - ждёт подписи Канцлера"), "Большинство за. Закон ещё не действует.", 3f, "12:00"),
        Result(Agenda("Закон подписан и вступает в силу."), note: "Только после подписи канцлера закон начинает действовать.", time: "12:20"));

    // ---- 03А --------------------------------------------------------------

    private static readonly string[] CommitteeModes = { "Все комитеты", "Мои комитеты" };
    private const string Welfare = "Комитет благополучия";
    private const string WelfareText = "Здравоохранение, социальные расходы, развитие секторов, эпидемии.";
    private const string AidBill = "Об экстренной медицинской помощи";
    private const string CommitteeHours = "Голосование — в комитетские часы (08:00–09:00 и 14:00–15:00)";

    private static SWTrainingFormPanel CommitteeList(int seats, string chair, string roster, bool canJoin, bool aimJoin = false) => Form("Комитеты",
        Choice(CommitteeModes, 0),
        Txt($"{Welfare}   {seats}/3 мест"),
        Dim(WelfareText),
        Txt($"Председатель: {chair}"),
        Dim($"Состав: {roster}"),
        canJoin ? Btn("Вступить", aimJoin) : Btn("Выйти", accent: SWTrainingAccent.Neutral));

    private static readonly string[] CommitteeVoteButtons = { "На заседание", "Вернуть автору" };

    private const string SecondMember = "Дара Квел";
    private const string ThirdMember = "Сен Арро";
    private const string ReturnReason = "Укажите, откуда возьмутся медики";
    private const string SecondReason = "Нет медиков: впишите, кто будет лечить";

    /// <summary>«Мои комитеты» in the committee hour: the reason box above the list, the bill with its tally and the two buttons.</summary>
    private static SWTrainingFormPanel CommitteeVote(string tally, string? myVote, int aim = -1, string reason = "", bool canSendBack = true, bool aimReason = false) => new("Комитеты", new[]
    {
        Choice(CommitteeModes, 1),
        Fld("Причина возврата", reason.Length > 0 ? reason : "Что доработать...", aimReason),
        Txt($"{Welfare}   3/3 мест"),
        Txt(AidBill),
        Dim($"автор: {BillAuthor}   Здоровье +6   Общество +3"),
        Dim(tally),
        myVote == null ? Dim(string.Empty) : Txt(myVote),
        Choice(CommitteeVoteButtons, canSendBack ? -1 : 1, aim),
    });

    private static SWTrainingFormPanel CommitteeWaiting() => new("Комитеты", new[]
    {
        Choice(CommitteeModes, 1),
        Txt($"{Welfare}   3/3 мест"),
        Txt(AidBill),
        Dim($"автор: {BillAuthor}   Здоровье +6   Общество +3"),
        Dim("на заседание: 0, вернуть: 0 (нужно 2), возвраты 0/2"),
        Dim(CommitteeHours),
    });

    private static SWTrainingFormPanel CommitteeEmpty() => new("Комитеты", new[]
    {
        Choice(CommitteeModes, 1),
        Txt($"{Welfare}   3/3 мест"),
        Dim("Законов на рассмотрении нет."),
    });

    private static SWTrainingFormPanel Returned(string reason) => Form("Разработка",
        Sec("Возвращено на доработку"),
        Txt(AidBill),
        Warn($"Причина: {reason}"),
        Dim("возвраты: 1/2"),
        Btn("Доработать"),
        Btn("Внести как есть", accent: SWTrainingAccent.Neutral));

    private static SWTrainingAnnotation[] Wire(string text, string remark) => new[]
    {
        new SWTrainingAnnotation(text, SWTrainingTone.Game, 330, 176, 270, "Канал комитетов"),
        new SWTrainingAnnotation(remark, SWTrainingTone.Teaching, 580, 24, 130),
    };

    public static SWTrainingStage[] CommitteeJoin() => Steps(
        HandOut(),
        HandIn(),
        Aim(CommitteeList(0, "—", "нет членов", true, true)),
        Press(CommitteeList(0, "—", "нет членов", true, true)),
        Result(CommitteeList(1, ExampleName, $"{ExampleName} (предс.)", false),
            note: "Тала Вен вступила первой и стала председателем. Мест в комитете три, а состоять можно только в одном."));

    public static SWTrainingStage[] CommitteeReferral() => Steps(
        Wait(CommitteeList(3, ExampleName, $"{ExampleName} (предс.), {SecondMember}, {ThirdMember}", false), "К обеду в комитете трое."),
        Result(CommitteeWaiting(), note: "Проект Вейла Орана меняет показатели на 9 пунктов, больше всего здравоохранение. Поэтому он пришёл в наш комитет.", time: "11:00"),
        Result(Agenda("Ожидает распределения в повестку", AidBill, BillAuthor, 14), note: "Если бы в комитете никого не было, проект сразу ушёл бы в повестку.", alternative: true, time: "11:00"));

    public static SWTrainingStage[] CommitteeHour() => Steps(
        Back("Проект Вейла Орана лежит в нашем комитете. До комитетского часа его можно только читать.", CommitteeWaiting(), "13:55"),
        Result(CommitteeVote("на заседание: 0, вернуть: 0 (нужно 2), возвраты 0/2", null), note: "В 14:00 появились кнопки. Решение примут два голоса из трёх.", time: "14:00"));

    public static SWTrainingStage[] CommitteeReturn() => Steps(
        Aim(CommitteeVote("на заседание: 0, вернуть: 0 (нужно 2), возвраты 0/2", null, aimReason: true), time: "14:05"),
        Press(CommitteeVote("на заседание: 0, вернуть: 0 (нужно 2), возвраты 0/2", null, aimReason: true), time: "14:05"),
        Wait(CommitteeVote("на заседание: 0, вернуть: 0 (нужно 2), возвраты 0/2", null, reason: ReturnReason), "Тала Вен пишет, что доработать: в проекте нет медиков.", 3f, "14:05"),
        Aim(CommitteeVote("на заседание: 0, вернуть: 0 (нужно 2), возвраты 0/2", null, 1, ReturnReason), time: "14:05"),
        Press(CommitteeVote("на заседание: 0, вернуть: 0 (нужно 2), возвраты 0/2", null, 1, ReturnReason), time: "14:05"),
        Wait(CommitteeVote("на заседание: 0, вернуть: 1 (нужно 2), возвраты 0/2", "ваш голос: вернуть автору", reason: ReturnReason), "Один голос ещё ничего не решает.", 3f, "14:10"),
        Aim(CommitteeVote("на заседание: 0, вернуть: 1 (нужно 2), возвраты 0/2", null, aimReason: true), time: "14:15"),
        Press(CommitteeVote("на заседание: 0, вернуть: 1 (нужно 2), возвраты 0/2", null, aimReason: true), time: "14:15"),
        Wait(CommitteeVote("на заседание: 0, вернуть: 1 (нужно 2), возвраты 0/2", null, reason: SecondReason),
            "За своей консолью Дара Квел вписывает собственную причину. Автор получит причину последнего голоса за возврат.", 4f, "14:15"),
        Aim(CommitteeVote("на заседание: 0, вернуть: 1 (нужно 2), возвраты 0/2", null, 1, SecondReason), time: "14:15"),
        Press(CommitteeVote("на заседание: 0, вернуть: 1 (нужно 2), возвраты 0/2", null, 1, SecondReason), time: "14:15"),
        new Step(ResultHold, Closeup("14:15"), CommitteeEmpty(),
            Notes: Wire($"Закон «{AidBill}» возвращён автору комитетом «{Welfare}» на доработку.", "Второй голос Дары Квел решил: проект ушёл из комитета.")),
        HandOut(),
        HandIn(),
        Aim(Form("Рабочий стол", Btn("Разработка", true)), time: "14:30"),
        Press(Form("Рабочий стол", Btn("Разработка", true)), time: "14:30"),
        Result(Returned(SecondReason), note: "В «Разработке» Вейл Оран видит причину Дары Квел и решает: доработать или внести как есть.", time: "14:30"),
        Result(CommitteeVote("на заседание: 0, вернуть: 0 (нужно 2), возвраты 2/2", null, canSendBack: false),
            note: "После двух возвратов кнопка возврата отключена.", alternative: true, time: "14:20"));

    public static SWTrainingStage[] CommitteeAdvance() => Steps(
        Back("Наутро проект с медиками снова в комитете. Возврат был один из двух, Сен Арро уже проголосовал «На заседание».",
            CommitteeVote("на заседание: 1, вернуть: 0 (нужно 2), возвраты 1/2", null), "08:10"),
        Aim(CommitteeVote("на заседание: 1, вернуть: 0 (нужно 2), возвраты 1/2", null, 0), time: "08:10"),
        Press(CommitteeVote("на заседание: 1, вернуть: 0 (нужно 2), возвраты 1/2", null, 0), time: "08:10"),
        new Step(ResultHold, Closeup("08:10"), Agenda("Ожидает распределения в повестку", AidBill, BillAuthor, 14),
            Notes: Wire($"Закон «{AidBill}» одобрен комитетом «{Welfare}» и передан на пленарное заседание.", "Второй голос решил: проект ушёл на пленарное заседание.")));

    // ---- 04 ---------------------------------------------------------------

    public static SWTrainingStage[] SpeakerOffice() => Steps(
        HandOut(),
        HandIn(),
        Aim(Office("Спикер Сената", false, true)),
        Press(Office("Спикер Сената", false, true)),
        Result(Office("Спикер Сената", true), "Вы вступили в должность."));

    public static SWTrainingStage[] SpeakerPick() => Steps(
        Wait(Form("Расписание", Txt("Выберите проект и час расписания.")), "В повестке ждёт закон Талы Вен о клиниках.", 3f),
        Wait(SessionDraft($"Черновик: № 12 {BillName}", null), hold: 3f),
        Result(SessionDraft($"Черновик: № 12 {BillName}", "10:00–12:00 (2 ч)", true), note: "Два часа хватит на обсуждение и голосование. Длительность меняется от одного до четырёх часов."));

    public static SWTrainingStage[] SpeakerPropose() => Steps(
        Aim(SessionDraft($"Черновик: № 12 {BillName}", "10:00–12:00 (2 ч)", true, true)),
        Press(SessionDraft($"Черновик: № 12 {BillName}", "10:00–12:00 (2 ч)", true, true)),
        Result(SessionPending(), "Заседание добавлено в расписание."),
        Result(SessionDraft($"Черновик: № 12 {BillName}", "10:00–12:00 (2 ч)", false, false, "Выбранное время или проект недоступны."),
            note: "Если это время уже занято, заседание не появится. Выберите другой час.", alternative: true));

    public static SWTrainingStage[] SpeakerWait() => Steps(
        Back("Время 10:00–12:00 было свободно, заседание назначено. Канцлер видит его в своём расписании.",
            Picture(new SWTrainingVisual(SWTrainingScene.Terminal, new[] { Figure(SWTrainingArt.FigChancellor, SWSenateTrainingArt.SlotAX) }), 3f)),
        Aim(SessionPending(true)),
        Press(SessionPending(true)),
        Result(SessionConfirmed(), "Заседание подтверждено."));

    public static SWTrainingStage[] SpeakerLead() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.HallSpeaker, new[] { Clock() }, ClockText: "10:00"), 4f, ScreenNote("10:00, заседание открыто. Сенаторы обсуждают закон о клиниках.")),
        Picture(new SWTrainingVisual(SWTrainingScene.HallSpeaker, new[] { Clock() }, ClockText: "12:00"), ResultHold, ScreenNote("12:00, время вышло. Итог голосования смотрите в реестре.")));

    // ---- 05 ---------------------------------------------------------------

    public static SWTrainingStage[] ChancellorOffice() => Steps(
        HandOut(),
        HandIn(),
        Aim(Office("Верховный Канцлер", false, true)),
        Press(Office("Верховный Канцлер", false, true)),
        Result(Office("Верховный Канцлер", true), "Вы вступили в должность."));

    public static SWTrainingStage[] ChancellorConfirm() => Steps(
        Aim(SessionPending(true)),
        Press(SessionPending(true)),
        Result(SessionConfirmed(), "Заседание подтверждено.", "Спикер предложил 10:00–12:00, канцлер согласился."));

    public static SWTrainingStage[] ChancellorRead() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Dossier, Array.Empty<SWTrainingOverlay>())),
        Result(Signature(false), note: "Сенат принял закон о клиниках. Прочитайте, что он меняет, до подписи."));

    public static SWTrainingStage[] ChancellorSign() => Steps(
        Aim(Signature(false, true)),
        Press(Signature(false, true)),
        Result(Form("На подпись", Dim("Список на подпись пуст.")), "Закон подписан и вступает в силу."));

    public static SWTrainingStage[] ChancellorVeto() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Desk, new[] { new SWTrainingOverlay(SWTrainingArt.MarkVeto, 400, 90) })),
        Result(Signature(false), note: "Обычно у канцлера только подпись."),
        Case("Если у канцлера есть чрезвычайное полномочие, днём рядом с подписью есть и вето.", Signature(true)),
        Aim(Signature(true, aimVeto: true), true),
        Press(Signature(true, aimVeto: true), true),
        Result(Form("На подпись", Dim("Закон не подписан.")), "Вето наложено. Закон не вступит в силу.", "Закон снят с подписи и в силу не вступит.", true, continues: true));

    // ---- 06 ---------------------------------------------------------------

    public static SWTrainingStage[] MinisterCard() => Steps(
        HandOut(),
        HandIn(),
        Result(Ministries("Выбрано: Министерство обороны"), note: "Финансы уже заняты, оборона свободна."));

    public static SWTrainingStage[] MinisterCompare() => Steps(
        Aim(Ministries("Выбрано: Министерство обороны", aimTab: true)),
        Press(Ministries("Выбрано: Министерство обороны", aimTab: true)),
        Result(Ministries("Сначала должны быть заняты все министерства - они важнее.", canClaim: false, departments: true),
            note: "Департаменты откроются, когда займут все министерства."));

    public static SWTrainingStage[] MinisterClaim() => Steps(
        Aim(Ministries("Выбрано: Министерство обороны", true)),
        Press(Ministries("Выбрано: Министерство обороны", true)),
        Result(MinistryClaimed(), note: "Бран Голл возглавил оборону. Портфель закреплён за его картой."),
        Result(Form("СЕНАТ / НАЗНАЧЕНИЕ В ИСПОЛНИТЕЛЬНУЮ ВЛАСТЬ", Item("Министерство финансов", "Занято", true), Dim("КАРТА МИНИСТРА"), Txt("Портфель занят"), Btn("Занять портфель", disabled: true)),
            note: "Занятый портфель выбрать нельзя.", alternative: true));

    public static SWTrainingStage[] MinisterFixed() => Steps(
        Back("Бран Голл занял свободную оборону, портфель за его картой.", new Step(3f, CardAt(1))),
        Result(MinistryClaimed(), note: "Кнопка отключена: за этой картой уже закреплён портфель."));

    public static SWTrainingStage[] MinisterDesk() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Desk, Array.Empty<SWTrainingOverlay>())),
        Result(Form("Министерство обороны", Txt("Бюджет 3000 кр")), note: "Бюджет на день пришёл. Дальше работа по главе вашего ведомства."));

    // ---- 07 ---------------------------------------------------------------

    public static SWTrainingStage[] DefenseBorder() => Steps(
        Picture(ChartView(), 4f, ScreenNote("На внешнем рубеже неспокойно.")),
        Result(BorderState("Напряжённо", 60, 20), note: "Давление 60 из 100, война ещё не утомила Республику."));

    public static SWTrainingStage[] DefenseStance() => Steps(
        Aim(Defense("DEFCON 5   Мир", 0, 1)),
        Press(Defense("DEFCON 5   Мир", 0, 1)),
        Result(Defense("DEFCON 4   Бдительность", 1), note: "Бдительность включилась сразу."),
        Case("Министр может поднять готовность выше, до мобилизации.", Defense("DEFCON 4   Бдительность", 1)),
        Aim(Defense("DEFCON 4   Бдительность", 1, 3), true),
        Press(Defense("DEFCON 4   Бдительность", 1, 3), true),
        Result(Defense("DEFCON 4   Бдительность", 1, pending: "Мобилизация"), note: "Мобилизация ждёт решения, если канцлер или спикер на месте.", alternative: true, continues: true),
        Result(Defense("DEFCON 2   Мобилизация", 3), note: "Если нет ни канцлера, ни спикера, позиция меняется сразу.", alternative: true));

    private static SWTrainingFormPanel Upkeep(string budget, string upkeep, string groups) => Form("Министерство обороны",
        Txt($"Бюджет: {budget} кр"),
        Dim($"Содержание: {upkeep} кр/ч"),
        Txt(groups));

    public static SWTrainingStage[] DefenseUpkeep() => Steps(
        Back("Позиция осталась прежней: DEFCON 4, бдительность.", Upkeep("3000", "120", "Развёрнуто групп: 2")),
        Result(Upkeep("2520", "240", "Развёрнуто групп: 4"), note: "Две новые группы удвоили содержание. Каждый час оно уходит из бюджета."));

    public static SWTrainingStage[] DefenseRequest() => Steps(
        Aim(Defense("DEFCON 4   Бдительность", 1, 4)),
        Press(Defense("DEFCON 4   Бдительность", 1, 4)),
        new Step(3f, Closeup(), Defense("DEFCON 4   Бдительность", 1, pending: "Война")),
        Aim(WarRequest(aimApprove: true)),
        Press(WarRequest(aimApprove: true)),
        Result(Defense("DEFCON 1   Война", 4), note: "Канцлер одобрил: установлена позиция DEFCON 1."),
        Case("Канцлер может и не согласиться с войной.", WarRequest()),
        Aim(WarRequest(aimReject: true), true),
        Press(WarRequest(aimReject: true), true),
        Result(Defense("DEFCON 4   Бдительность", 1), note: "Запрос отклонён, позиция осталась прежней.", alternative: true, continues: true));

    public static SWTrainingStage[] DefenseCheck() => Steps(
        Back("Канцлер одобрил войну, Республика на DEFCON 1. Граница держится.", Picture(ChartView(1), 4f)),
        Result(BorderState("Напряжённо", 52, 26), note: "Давление снизилось, но война утомляет Республику. Следите за обоими показателями."));
}
