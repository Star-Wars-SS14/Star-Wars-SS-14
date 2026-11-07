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

/// <summary>One line of a teaching illustration. Labels come from the game's windows; example values are marked on screen.</summary>
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

/// <summary>A teaching illustration of part of a game window, built from controls: a title and its lines in the window's order.</summary>
public sealed record SWTrainingFormPanel(string Title, SWTrainingRow[] Rows) : SWTrainingPanelState
{
    /// <summary>Equal content means the same controls stay on screen between stages, so a pointer's target does not jump.</summary>
    public bool Equals(SWTrainingFormPanel? other) => other != null && Title == other.Title && Rows.SequenceEqual(other.Rows);

    public override int GetHashCode() => HashCode.Combine(Title, Rows.Length);
}

/// <summary>
/// Timelines of chapters 01 to 07. Every shot goes from the starting state through the action to a held result.
/// Labels and messages are those of the game's windows and locale files; names and numbers are examples, marked as such.
/// </summary>
public static partial class SWTrainingSamples
{
    private const string Example = "Названия и числа для примера.";
    private const string BillExample = "Закон о примере";

    // One array per set of buttons: rows compare them by reference.
    private static readonly string[] DraftModes = { "Свой закон", "Шаблоны" };
    private static readonly string[] DurationButtons = { "− час", "+ час" };
    private static readonly string[] MinistryTabs = { "Министерства", "Департаменты", "Бюро" };

    /// <summary>Worlds of the example's sector with an active economy, in the game's order: the anchor world first, then by name.</summary>
    private static readonly string[] SectorWorlds = { "Корусант", "Дунидин", "Квенк", "Утрост" };
    private const int ExampleWorld = 1;

    /// <summary>One state and how long it stays. Under reduced motion it stays at least <see cref="MinStill"/> seconds.</summary>
    private sealed record Step(
        float Hold,
        SWTrainingVisual Visual,
        SWTrainingPanelState? Panel = null,
        SWTrainingPointer Pointer = SWTrainingPointer.None,
        string? Popup = null,
        SWTrainingAnnotation[]? Notes = null,
        bool Alternative = false);

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

            stages[i] = new SWTrainingStage(normal, reduced, visual, step.Panel, step.Pointer, step.Popup, step.Notes, step.Alternative);
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

    private static Step Result(SWTrainingPanelState panel, string? popup = null, string? note = null, bool alternative = false, string? time = null) =>
        new(ResultHold, Closeup(time), panel, Popup: popup, Notes: note == null ? null : ScreenNote(note), Alternative: alternative);

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

    /// <summary>The first free sector starts selected; the example picks the next one, and only then claims it.</summary>
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
        Result(SectorSelect(true), "Сектор закреплён: Корусант.", Example),
    };

    private static SWTrainingFormPanel ProgramOffer(int world, string budget, bool aimWorld = false, bool openWorld = false, bool aimFund = false, bool canFund = true) => Form(string.Empty,
        Txt(budget),
        WorldList(world, aimWorld, openWorld),
        Txt("Программа для примера"),
        Sec("Материалы"),
        Dim("Материал для примера: 4 ед."),
        Dim("Срок работ: 3 ч"),
        Btn("Финансировать — 800 кр.", aimFund, !canFund));

    private static SWTrainingFormPanel ProgramRunning(string budget, int delivered, int inTransit, int worked)
    {
        var transit = inTransit > 0 ? $"   В пути: {inTransit}" : string.Empty;
        return Form(string.Empty,
            Txt(budget),
            Txt("Программа для примера"),
            Dim(SectorWorlds[ExampleWorld]),
            Sec("Снабжение"),
            Dim($"Материал для примера: {delivered} / 4 ед.{transit}"),
            Meter(delivered / 4f),
            Dim($"Работы: {worked} / 3 ч"),
            Meter(worked / 3f, SWTrainingAccent.Neutral));
    }

    private static SWTrainingFormPanel Draft(string name, string budget, string? error, bool hasChange, bool aimAdd = false, bool aimSubmit = false)
    {
        var change = hasChange ? Dim("Показатель для примера: +2") : Dim("Изменений нет");
        return Form("Разработка",
            Choice(DraftModes, 0),
            Fld("Название законопроекта", name),
            Sec("Изменения"),
            Btn("Добавить показатель", aimAdd),
            change,
            Txt(budget),
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
        Txt($"{BillExample} — ожидает подтверждения"),
        Dim("10:00–12:00"),
        Btn("Подтвердить", aimConfirm, accent: SWTrainingAccent.Good),
        Btn("Отклонить", accent: SWTrainingAccent.Bad));

    private static SWTrainingFormPanel SessionConfirmed() => Form("Расписание",
        Fine($"{BillExample} — подтверждено"),
        Dim("10:00–12:00"));

    private static SWTrainingFormPanel Signature(bool hasVeto, bool aimSign = false, bool aimVeto = false)
    {
        var sign = Btn("Подписать закон", aimSign);
        return hasVeto
            ? Form("На подпись", Txt(BillExample), Dim("№ 12, автор для примера"), sign, Btn("Наложить вето", aimVeto, accent: SWTrainingAccent.Bad))
            : Form("На подпись", Txt(BillExample), Dim("№ 12, автор для примера"), sign);
    }

    /// <summary>After the claim the portfolio is taken: the list says so, and the button is off, as in the game.</summary>
    private static SWTrainingFormPanel MinistryClaimed() => Form("СЕНАТ / НАЗНАЧЕНИЕ В ИСПОЛНИТЕЛЬНУЮ ВЛАСТЬ",
        Choice(MinistryTabs, 0),
        Item("Министерство обороны", "Занято", true),
        Item("Министерство финансов", "Занято"),
        Dim($"Занимает: {ExampleName}"),
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

    private static SWTrainingFormPanel BorderState() => Form(string.Empty,
        Sec("Давление на границе"),
        Txt("Напряжённо   60/100"),
        Meter(0.6f, SWTrainingAccent.Bad),
        Sec("Усталость от войны"),
        Txt("20/100"),
        Meter(0.2f, SWTrainingAccent.Neutral));

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
        Result(DeskHeader("День 1, 09:30"), note: "Игровое время видно в шапке рабочего стола. Время для примера."),
        Result(DeskHeader("День 1: ночь"), note: "Другой случай: ночью основные решения недоступны.", alternative: true));

    public static SWTrainingStage[] GeneralCard() => Steps(
        HandOut(),
        HandIn(),
        Result(Office("Спикер Сената", false), note: "Карта вставлена, но статус ещё прежний: должность занимают отдельно."));

    public static SWTrainingStage[] GeneralSeat() => Steps(ClaimSteps());

    public static SWTrainingStage[] GeneralStages() => Steps(
        new Step(4f, Closeup(), ProgramRunning("Бюджет сектора: 4200 кр.", 1, 2, 0), Notes: ScreenNote(Example)),
        Result(ProgramRunning("Бюджет сектора: 4200 кр.", 3, 0, 0), note: "Материалы ещё не все, поэтому работы не идут и результата нет."));

    /// <summary>A cut from the wide plan to the corridor floor: both stand on it, and the speaker's gesture is towards the usher.</summary>
    public static SWTrainingStage[] GeneralColleague() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Hall, Array.Empty<SWTrainingOverlay>()), 3f),
        Picture(new SWTrainingVisual(SWTrainingScene.Entrance, new[] { Figure(SWTrainingArt.FigSpeaker, SWSenateTrainingArt.SlotAX), Figure(SWTrainingArt.FigUsher, SWSenateTrainingArt.SlotBX) }), 3f),
        Picture(new SWTrainingVisual(SWTrainingScene.Entrance, new[] { Figure(SWTrainingArt.FigSpeaker, SWSenateTrainingArt.SlotAX, 1), Figure(SWTrainingArt.FigUsher, SWSenateTrainingArt.SlotBX) }), ResultHold));

    // ---- 02 ---------------------------------------------------------------

    public static SWTrainingStage[] SenatorSeat() => Steps(ClaimSteps());

    public static SWTrainingStage[] SenatorDesk() => Steps(
        HandOut(),
        HandIn(),
        Result(Form("Должность", Txt("Сенатор сектора Корусант"), Fine("Статус: в должности")), note: Example));

    public static SWTrainingStage[] SenatorCompare() => Steps(
        Aim(ProgramOffer(0, "Бюджет сектора: 5000 кр.", aimWorld: true)),
        Press(ProgramOffer(0, "Бюджет сектора: 5000 кр.", aimWorld: true, openWorld: true)),
        new Step(3f, Closeup(), ProgramOffer(ExampleWorld, "Бюджет сектора: 5000 кр.", openWorld: true)),
        Result(ProgramOffer(ExampleWorld, "Бюджет сектора: 5000 кр."), note: "В списке только миры вашего сектора. Выбор мира ещё ничего не заказывает. " + Example),
        Result(ProgramOffer(ExampleWorld, "Бюджет сектора: 5000 кр.   Ожидает утверждения", canFund: false),
            note: "Другой случай: бюджет не утверждён, кнопка отключена.", alternative: true));

    public static SWTrainingStage[] SenatorFund() => Steps(
        Aim(ProgramOffer(ExampleWorld, "Бюджет сектора: 5000 кр.", aimFund: true)),
        Press(ProgramOffer(ExampleWorld, "Бюджет сектора: 5000 кр.", aimFund: true)),
        Result(ProgramRunning("Бюджет сектора: 4200 кр.", 0, 2, 0), note: "Деньги списаны, материалы только в пути. " + Example),
        Result(ProgramOffer(ExampleWorld, "Бюджет сектора: 500 кр.", canFund: false),
            note: "Другой случай: денег мало, кнопка отключена, заказа нет.", alternative: true));

    public static SWTrainingStage[] SenatorWait() => Steps(
        new Step(4f, Closeup(), ProgramRunning("Бюджет сектора: 4200 кр.", 2, 2, 0), Notes: ScreenNote(Example)),
        Result(ProgramRunning("Бюджет сектора: 4200 кр.", 4, 0, 1), note: "Материалы доставлены, идут работы. Эффект появится только после их завершения."));

    // ---- 03 ---------------------------------------------------------------

    public static SWTrainingStage[] BillOpen() => Steps(
        HandOut(),
        HandIn(),
        Aim(Form("Рабочий стол", Btn("Разработка", true))),
        Press(Form("Рабочий стол", Btn("Разработка", true))),
        Result(Draft(string.Empty, "Очки: 10 из бюджета 10", "Укажите название законопроекта.", false), note: "Раздел виден сенатору в должности. Подача идёт только днём."));

    public static SWTrainingStage[] BillEdit() => Steps(
        new Step(3f, Closeup(), Draft(BillExample, "Очки: 10 из бюджета 10", "Добавьте ненулевое изменение.", false, aimAdd: true), SWTrainingPointer.Travel),
        Press(Draft(BillExample, "Очки: 10 из бюджета 10", "Добавьте ненулевое изменение.", false, aimAdd: true)),
        Result(Draft(BillExample, "Очки: 8 из бюджета 10", null, true), note: Example));

    public static SWTrainingStage[] BillLimit() => Steps(
        new Step(4f, Closeup(), Draft(BillExample, "Очки: -2 из бюджета 10", "Превышен бюджет очков.", true), Notes: ScreenNote("Пока очков не хватает, кнопка отключена. " + Example)),
        Result(Draft(BillExample, "Очки: 2 из бюджета 10", null, true), note: "Изменения уменьшены, проект укладывается в бюджет очков."));

    public static SWTrainingStage[] BillSubmit() => Steps(
        Aim(Draft(BillExample, "Очки: 2 из бюджета 10", null, true, aimSubmit: true)),
        Press(Draft(BillExample, "Очки: 2 из бюджета 10", null, true, aimSubmit: true)),
        Result(Form("Повестка", Txt(BillExample), Dim("Ожидает распределения в повестку")), "Законопроект внесён в повестку.", "Проект передан на рассмотрение в комитет."),
        Result(Draft("Второй проект", "Очки: 2 из бюджета 10", null, true), "У вас уже есть законопроект на рассмотрении.",
            "Другой случай: у автора уже есть активный проект, второй не принимается.", true));

    private static SWTrainingFormPanel BillPath(int stage)
    {
        SWTrainingRow Line(int index, string text) => index == stage ? Fine(text) : Dim(text);
        return Form("Учебная схема этапов",
            Line(0, "Ожидает распределения в повестку"),
            Line(1, "На голосовании"),
            Line(2, "Прошёл голосование - ждёт подписи Канцлера"),
            Line(3, "Закон подписан и вступает в силу."));
    }

    public static SWTrainingStage[] BillPathStages() => Steps(
        new Step(3f, Closeup(), BillPath(0)),
        new Step(3f, Closeup(), BillPath(1)),
        new Step(3f, Closeup(), BillPath(2)),
        Result(BillPath(3), note: "Этапы идут по очереди, и проект может остановиться на любом. Проверяйте статус."));

    // ---- 04 ---------------------------------------------------------------

    public static SWTrainingStage[] SpeakerOffice() => Steps(
        HandOut(),
        HandIn(),
        Aim(Office("Спикер Сената", false, true)),
        Press(Office("Спикер Сената", false, true)),
        Result(Office("Спикер Сената", true), "Вы вступили в должность."));

    public static SWTrainingStage[] SpeakerPick() => Steps(
        new Step(3f, Closeup(), Form("Расписание", Txt("Выберите проект и час расписания."))),
        new Step(3f, Closeup(), SessionDraft($"Черновик: № 12 {BillExample}", null)),
        Result(SessionDraft($"Черновик: № 12 {BillExample}", "10:00–12:00 (2 ч)", true), note: "Длительность можно менять от одного до четырёх часов. " + Example));

    public static SWTrainingStage[] SpeakerPropose() => Steps(
        Aim(SessionDraft($"Черновик: № 12 {BillExample}", "10:00–12:00 (2 ч)", true, true)),
        Press(SessionDraft($"Черновик: № 12 {BillExample}", "10:00–12:00 (2 ч)", true, true)),
        Result(SessionPending(), "Заседание добавлено в расписание."),
        Result(SessionDraft($"Черновик: № 12 {BillExample}", "10:00–12:00 (2 ч)", false, false, "Выбранное время или проект недоступны."),
            note: "Другой случай: время занято, кнопка отключена, заседания нет.", alternative: true));

    public static SWTrainingStage[] SpeakerWait() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Terminal, new[] { Figure(SWTrainingArt.FigChancellor, SWSenateTrainingArt.SlotAX) })),
        Aim(SessionPending(true)),
        Press(SessionPending(true)),
        Result(SessionConfirmed(), "Заседание подтверждено."));

    public static SWTrainingStage[] SpeakerLead() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.HallSpeaker, new[] { Clock() }, ClockText: "10:00"), 4f, ScreenNote("Заседание идёт по расписанию. Часы для примера.")),
        Picture(new SWTrainingVisual(SWTrainingScene.HallSpeaker, new[] { Clock() }, ClockText: "12:00"), ResultHold, ScreenNote("Время заседания вышло: проверьте итог. Какой он будет, зависит от голосования.")));

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
        Result(SessionConfirmed(), "Заседание подтверждено.", Example));

    public static SWTrainingStage[] ChancellorRead() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Dossier, Array.Empty<SWTrainingOverlay>())),
        Result(Signature(false), note: "Текст и последствия закона зависят от проекта. " + Example));

    public static SWTrainingStage[] ChancellorSign() => Steps(
        Aim(Signature(false, true)),
        Press(Signature(false, true)),
        Result(Form("На подпись", Dim("Список на подпись стал короче.")), "Закон подписан и вступает в силу."));

    public static SWTrainingStage[] ChancellorVeto() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Desk, new[] { new SWTrainingOverlay(SWTrainingArt.MarkVeto, 400, 90) })),
        Result(Signature(false), note: "Без чрезвычайного полномочия кнопки вето нет."),
        Aim(Signature(true, aimVeto: true), true),
        Press(Signature(true, aimVeto: true), true),
        Result(Form("На подпись", Dim("Закон не подписан.")), "Вето наложено. Закон не вступит в силу.", "Другой случай: полномочие есть, вето наложено днём.", true));

    // ---- 06 ---------------------------------------------------------------

    public static SWTrainingStage[] MinisterCard() => Steps(
        HandOut(),
        HandIn(),
        Result(Ministries("Выбрано: Министерство обороны"), note: Example));

    public static SWTrainingStage[] MinisterCompare() => Steps(
        Aim(Ministries("Выбрано: Министерство обороны", aimTab: true)),
        Press(Ministries("Выбрано: Министерство обороны", aimTab: true)),
        Result(Ministries("Сначала должны быть заняты все министерства - они важнее.", canClaim: false, departments: true),
            note: "У каждой группы свои портфели, а условия читайте в досье."));

    public static SWTrainingStage[] MinisterClaim() => Steps(
        Aim(Ministries("Выбрано: Министерство обороны", true)),
        Press(Ministries("Выбрано: Министерство обороны", true)),
        Result(MinistryClaimed(), note: "Портфель закреплён за этой картой."),
        Result(Form("СЕНАТ / НАЗНАЧЕНИЕ В ИСПОЛНИТЕЛЬНУЮ ВЛАСТЬ", Item("Министерство финансов", "Занято", true), Dim("КАРТА МИНИСТРА"), Txt("Портфель занят"), Btn("Занять портфель", disabled: true)),
            note: "Другой случай: занятый портфель выбрать нельзя.", alternative: true));

    public static SWTrainingStage[] MinisterFixed() => Steps(
        new Step(3f, CardAt(1)),
        Result(MinistryClaimed(), note: "Кнопка отключена: за этой картой уже закреплён портфель."));

    public static SWTrainingStage[] MinisterDesk() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Desk, Array.Empty<SWTrainingOverlay>())),
        Result(Form("Министерство обороны", Txt("Бюджет 3000 кр")), note: "Дальше глава вашего ведомства объяснит работу. " + Example));

    // ---- 07 ---------------------------------------------------------------

    public static SWTrainingStage[] DefenseBorder() => Steps(
        Picture(ChartView(), 4f, ScreenNote("Учебная схема, не карта игры.")),
        Result(BorderState(), note: Example));

    public static SWTrainingStage[] DefenseStance() => Steps(
        Aim(Defense("DEFCON 5   Мир", 0, 1)),
        Press(Defense("DEFCON 5   Мир", 0, 1)),
        Result(Defense("DEFCON 4   Бдительность", 1), note: "Позиция сменилась сразу."),
        Aim(Defense("DEFCON 4   Бдительность", 1, 3), true),
        Press(Defense("DEFCON 4   Бдительность", 1, 3), true),
        Result(Defense("DEFCON 4   Бдительность", 1, pending: "Мобилизация"), note: "Другой случай: канцлер или спикер на месте, рост до DEFCON 2 ждёт решения.", alternative: true),
        Result(Defense("DEFCON 2   Мобилизация", 3), note: "Другой случай: нет ни канцлера, ни спикера, позиция сменилась сразу.", alternative: true));

    public static SWTrainingStage[] DefenseUpkeep() => Steps(
        new Step(4f, Closeup(), Form("Учебная схема, без сумм", Dim("Развёрнутые силы"), Meter(0.3f), Dim("Средства ведомства"), Meter(0.8f, SWTrainingAccent.Neutral))),
        Result(Form("Учебная схема, без сумм", Dim("Развёрнутые силы"), Meter(0.7f), Dim("Средства ведомства"), Meter(0.4f, SWTrainingAccent.Neutral)),
            note: "Больше развёрнутых сил означает больше расходов на содержание."));

    public static SWTrainingStage[] DefenseRequest() => Steps(
        Aim(Defense("DEFCON 4   Бдительность", 1, 4)),
        Press(Defense("DEFCON 4   Бдительность", 1, 4)),
        new Step(3f, Closeup(), Defense("DEFCON 4   Бдительность", 1, pending: "Война")),
        Aim(WarRequest(aimApprove: true)),
        Press(WarRequest(aimApprove: true)),
        Result(Defense("DEFCON 1   Война", 4), note: "В этом примере запрос одобрен: установлена позиция DEFCON 1."),
        Aim(WarRequest(aimReject: true), true),
        Press(WarRequest(aimReject: true), true),
        Result(Defense("DEFCON 4   Бдительность", 1), note: "Другой выбор: запрос отклонён, позиция прежняя.", alternative: true));

    public static SWTrainingStage[] DefenseCheck() => Steps(
        Picture(ChartView(), 4f, ScreenNote("Учебная схема, не карта игры.")),
        Result(BorderState(), note: "Что изменится, зависит от раунда. " + Example));
}
