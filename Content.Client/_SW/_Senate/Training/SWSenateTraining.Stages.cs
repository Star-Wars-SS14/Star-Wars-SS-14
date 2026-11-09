using System;

namespace Content.Client._SW._Senate.Training;

/// <summary>State of a teaching illustration built from controls. It is not the game's window and sends nothing.</summary>
public abstract record SWTrainingPanelState;

/// <summary>Illustrates the staff shift window: <c>SWStaffShiftConsoleMenu</c>.</summary>
public sealed record SWTrainingShiftPanel(string Name, bool Recognized, bool OnShift, string Notice) : SWTrainingPanelState;

/// <summary>Illustrates the "Бюджеты" tab of the finance ministry: <c>SWMinistryWorkConsoleMenu.Finance</c>. Shares are percent.</summary>
public sealed record SWTrainingFinancePanel(
    string Phase,
    int Sector,
    int Ministry,
    int Republic,
    int Reserve,
    bool ShowSubmit,
    bool AimAtSubmit = false,
    bool Stale = false,
    string? RejectReason = null) : SWTrainingPanelState;

public enum SWTrainingBudgetPhase
{
    NotSubmitted,
    Pending,
    Approved,
}

/// <summary>Illustrates the chancellor's "Финансы", "Бюджет" section: <c>SWSenateWorkConsoleMenu.BuildBudgetSection</c>.</summary>
public sealed record SWTrainingChancellorPanel(
    SWTrainingBudgetPhase Phase,
    int Sector,
    int Ministry,
    int Republic,
    int Reserve,
    string? AutoReason = null,
    string? RejectText = null) : SWTrainingPanelState;

/// <summary>
/// Only the confirmed status of the chancellor's budget section. Used where the shares and ministries depend on the
/// round, as with the automatic project of a vacant finance ministry: it has no numbers and no list, on purpose.
/// </summary>
public sealed record SWTrainingBudgetStatusPanel(SWTrainingBudgetPhase Phase, string? AutoReason = null) : SWTrainingPanelState;

/// <summary>What the teaching pointer does on the panel's single target control.</summary>
public enum SWTrainingPointer
{
    None,
    /// <summary>Goes to the control. Under reduced motion it stands there still, and the control is outlined.</summary>
    Travel,
    /// <summary>Presses the control, which gets a bright outline.</summary>
    Press,
}

public enum SWTrainingTone
{
    /// <summary>Teaching remark, visibly not game text.</summary>
    Teaching,
    /// <summary>A line the game itself can show.</summary>
    Game,
    /// <summary>The same, in a warning colour, as the game's injury lines are coloured.</summary>
    GameWarning,
}

/// <summary>Text above the picture. X, Y and Width are in the 720×270 frame.</summary>
public sealed record SWTrainingAnnotation(string Text, SWTrainingTone Tone, int X, int Y, int Width, string? Heading = null);

/// <summary>
/// One meaningful state of a shot. The stage on screen is found from the shot's time alone, so pause, back, skip and
/// replay cannot lose or reorder it. Under reduced motion the same stages are held as still pictures from
/// <paramref name="ReducedStart"/>. <paramref name="Alternative"/> marks a stage of a case other than the one the
/// shot follows; such stages always come after it, at the end of the shot. <paramref name="Opens"/> marks the first
/// stage of each such case: it states the condition and shows the scene before anything is done in that case. The
/// following stages of the same case carry it on; a neighbouring, independent case opens again.
/// <paramref name="Resumes"/> marks the first stage of a shot that follows another case: before anything is done it
/// shows the main line's state again and its remark names that state, so the shot does not look like a continuation
/// of the other case.
/// </summary>
public sealed record SWTrainingStage(
    float Start,
    float ReducedStart,
    SWTrainingVisual Visual,
    SWTrainingPanelState? Panel = null,
    SWTrainingPointer Pointer = SWTrainingPointer.None,
    string? Popup = null,
    SWTrainingAnnotation[]? Annotations = null,
    bool Alternative = false,
    bool Opens = false,
    bool Resumes = false);

/// <summary>
/// Timelines of the three first chapters with stages. Names and numbers of people and money are examples; the
/// labels and messages come from the sources named on the panel records and in the locale files.
/// </summary>
public static partial class SWTrainingSamples
{
    public const string ExampleName = "Тала Вен";
    public const string OtherExampleName = "Кир Дан";

    /// <summary>The senate doctor of 01А and 28: a member of staff, not one of the senators.</summary>
    public const string DoctorName = "Мира Тессе";

    /// <summary>The healing item's real local name and the time of one use on another character.</summary>
    public const string HealingItem = "набор для ушибов";
    public const float HealingSeconds = 3f;

    /// <summary>The hand goes from the hip to the forearm through the four frames of the sheet.</summary>
    private const float ReachSeconds = 1.2f;
    private const float ReachStart = 1.5f;
    private const float ContactStart = ReachStart + ReachSeconds;

    // ---- pictures ---------------------------------------------------------

    private static SWTrainingOverlay Clock() => new(SWTrainingArt.MarkClock, 530, 28);

    private static SWTrainingVisual Closeup(string? time = null) => time == null
        ? new SWTrainingVisual(SWTrainingScene.Console, Array.Empty<SWTrainingOverlay>())
        : new SWTrainingVisual(SWTrainingScene.Console, new[] { Clock() }, ClockText: time);

    /// <summary>Console under the doctor, so that the reaching hand is in front of it. Both share the frame's origin.</summary>
    private static SWTrainingVisual ShiftRoom(int doctorFrame, int consoleFrame, string? time = null)
    {
        var console = new SWTrainingOverlay(SWTrainingArt.ShiftConsole, SWSenateTrainingArt.ShiftConsoleX, SWSenateTrainingArt.ShiftConsoleY, consoleFrame);
        var doctor = new SWTrainingOverlay(SWTrainingArt.FigDoctorConsole, SWSenateTrainingArt.ShiftDoctorX, SWSenateTrainingArt.ShiftDoctorY, doctorFrame);
        return time == null
            ? new SWTrainingVisual(SWTrainingScene.ShiftRoom, new[] { console, doctor })
            : new SWTrainingVisual(SWTrainingScene.ShiftRoom, new[] { console, doctor, Clock() }, ClockText: time);
    }

    /// <summary>
    /// The checked treatment layer over the patient, in every shot with both of them, so the doctor neither covers a
    /// face nor jumps between shots. Frame 0 is the hand at the hip. With a sequence the hand moves through all frames;
    /// the frame is then the still for reduced motion. The patient keeps one frame: no change of the head bandage is shown.
    /// </summary>
    public static SWTrainingVisual Treating(int frame, float sequence = 0f, bool reverse = false) => new(SWTrainingScene.Clinic, new[]
    {
        new SWTrainingOverlay(SWTrainingArt.FigPatient, SWSenateTrainingArt.PatientX, SWSenateTrainingArt.PatientY, 0),
        new SWTrainingOverlay(SWTrainingArt.FigDoctorTreat, SWSenateTrainingArt.TreatDoctorX, SWSenateTrainingArt.SlotY, frame, Sequence: sequence, Reverse: reverse),
    });

    /// <summary>The same composition as treatment, with the examining pose: the doctor looks the patient over without touching.</summary>
    public static SWTrainingVisual Examining(int frame) => new(SWTrainingScene.Clinic, new[]
    {
        new SWTrainingOverlay(SWTrainingArt.FigPatient, SWSenateTrainingArt.PatientX, SWSenateTrainingArt.PatientY, 0),
        new SWTrainingOverlay(SWTrainingArt.FigDoctorExamine, SWSenateTrainingArt.ExamineDoctorX, SWSenateTrainingArt.SlotY, frame),
    });

    /// <summary>Right of the console in the shift room, above the floor.</summary>
    private static SWTrainingAnnotation[] RoomNote(string text) =>
        new[] { new SWTrainingAnnotation(text, SWTrainingTone.Teaching, 510, 90, 90) };

    /// <summary>Under the illustration and right of the text zone.</summary>
    private static SWTrainingAnnotation[] ScreenNote(string text) =>
        new[] { new SWTrainingAnnotation(text, SWTrainingTone.Teaching, 330, 176, 270) };

    /// <summary>Beside the patient and never over a face: it ends left of the doctor.</summary>
    private static SWTrainingAnnotation[] OpenActions() => new[]
    {
        new SWTrainingAnnotation("Подойдите вплотную и откройте действия с пациентом.", SWTrainingTone.Teaching, 130, 24, 190),
    };

    /// <summary>The verb's own name, as the game lists it; where the list appears depends on the client.</summary>
    private static SWTrainingAnnotation[] ChooseHealth() => new[]
    {
        new SWTrainingAnnotation("Здоровье", SWTrainingTone.Game, 130, 24, 190),
        new SWTrainingAnnotation("Выберите это действие осмотра.", SWTrainingTone.Teaching, 130, 56, 190),
    };

    private static SWTrainingAnnotation[] TooFar() => new[]
    {
        new SWTrainingAnnotation("Проведите базовое медицинское обследование на небольшом расстоянии.", SWTrainingTone.Game, 130, 24, 190),
        new SWTrainingAnnotation("Если пациент далеко, это действие недоступно.", SWTrainingTone.Teaching, 130, 84, 190),
    };

    private static SWTrainingAnnotation[] HealthResult(string line, string remark) => new[]
    {
        new SWTrainingAnnotation(line, SWTrainingTone.GameWarning, 130, 24, 190, "Здоровье"),
        new SWTrainingAnnotation(remark, SWTrainingTone.Teaching, 130, 84, 190),
    };

    private static SWTrainingAnnotation[] Count(int count) =>
        new[] { new SWTrainingAnnotation($"Количество: {count}", SWTrainingTone.Game, 130, 24, 190) };

    // ---- 01А --------------------------------------------------------------

    private static SWTrainingShiftPanel ShiftOff(string notice = "") => new(DoctorName, true, false, notice);

    private static SWTrainingShiftPanel ShiftOn(string notice = "") => new(DoctorName, true, true, notice);

    public static SWTrainingStage[] ShiftOpen() => new SWTrainingStage[]
    {
        new(0f, 0f, ShiftRoom(0, 0)),
        new(1.5f, 3f, ShiftRoom(1, 0)),
        new(3f, 6f, ShiftRoom(1, 1)),
        new(4f, 9f, ShiftRoom(1, 1), ShiftOff()),
    };

    public static SWTrainingStage[] ShiftStart() => new SWTrainingStage[]
    {
        new(0f, 0f, ShiftRoom(0, 1), ShiftOff(), SWTrainingPointer.Travel),
        new(2f, 3f, ShiftRoom(1, 1), ShiftOff(), SWTrainingPointer.Press),
        new(3f, 6f, ShiftRoom(1, 1), ShiftOn("Смена принята.")),
    };

    /// <summary>The main case and the other one each get a whole interval of their own.</summary>
    public static SWTrainingStage[] ShiftCheck() => new SWTrainingStage[]
    {
        new(0f, 0f, ShiftRoom(0, 1), ShiftOn()),
        new(12f, 12f, ShiftRoom(0, 1), new SWTrainingShiftPanel(OtherExampleName, false, false, string.Empty),
            Annotations: RoomNote("У Кира Дана должность не опознана, поэтому кнопки нет."), Alternative: true, Opens: true),
    };

    /// <summary>The account as the apartment console shows it: the tenant line is the owner of the account, lease or not.</summary>
    public static SWTrainingFormPanel ApartmentBalance(int balance) => Form("Аренда апартаментов",
        Dim("РЕСПУБЛИКА / ГОЛОНЕТ"),
        Txt($"Наниматель: {DoctorName}, личный счёт: {balance} кр."));

    /// <summary>
    /// The salary is credited at the start of a game hour of the working day to whoever is on shift at that moment: the
    /// balance before, the shift still running when the hour turns, the balance seen later. No instant payment is claimed.
    /// </summary>
    public static SWTrainingStage[] ShiftPay() => new SWTrainingStage[]
    {
        new(0f, 0f, Closeup("10:50"), ApartmentBalance(110),
            Annotations: ScreenNote("Мира Тессе на смене. Перед обходом она смотрит свой счёт у консоли аренды апартаментов."), Resumes: true),
        new(4f, 4f, ShiftRoom(0, 1, "10:59"), ShiftOn(), Annotations: RoomNote("Мира на смене, до нового часа минута.")),
        new(8f, 8f, ShiftRoom(0, 1, "11:00"), ShiftOn(), Annotations: RoomNote("11:00, она всё ещё на смене. В начале часа дежурным начисляется оклад.")),
        new(12f, 12f, Closeup("11:20"), ApartmentBalance(165), Annotations: ScreenNote("Вернувшись к консоли, Мира видит 165 кр: оклад врача за час, 55 кр, пришёл. Жильё для этого снимать не нужно.")),
        new(20f, 20f, ShiftRoom(1, 1, "11:30"), ShiftOff("Смена сдана."),
            Annotations: RoomNote("Если сдать смену посреди часа, за этот неполный час оклад может не прийти."), Alternative: true, Opens: true),
    };

    public static SWTrainingStage[] ShiftEnd() => new SWTrainingStage[]
    {
        new(0f, 0f, ShiftRoom(0, 1), ShiftOn(), Annotations: RoomNote("Посреди часа Мира смену не сдавала: она всё ещё на смене."), Resumes: true),
        new(4f, 4f, ShiftRoom(0, 1), ShiftOn(), SWTrainingPointer.Travel),
        new(6f, 7f, ShiftRoom(1, 1), ShiftOn(), SWTrainingPointer.Press),
        new(7f, 10f, ShiftRoom(1, 1), ShiftOff("Смена сдана.")),
    };

    // ---- 05А --------------------------------------------------------------

    private const string RejectExample = "Уточните доли ведомств.";

    private static SWTrainingFinancePanel Draft(int sector, int ministry, bool aimAtSubmit = false) =>
        new("Черновик", sector, ministry, 10, 10, true, aimAtSubmit);

    private static SWTrainingFinancePanel Submitted() => new("На одобрении Канцлера", 45, 35, 10, 10, false);

    private static SWTrainingChancellorPanel Chancellor(SWTrainingBudgetPhase phase, string? autoReason = null, string? rejectText = null) =>
        new(phase, 45, 35, 10, 10, autoReason, rejectText);

    public static SWTrainingStage[] BudgetPrepare() => new SWTrainingStage[]
    {
        new(0f, 0f, Closeup("07:30"), Draft(40, 40), SWTrainingPointer.Travel),
        new(2f, 3f, Closeup("07:30"), Draft(40, 40), SWTrainingPointer.Press),
        new(3f, 6f, Closeup("07:30"), Draft(45, 35), Annotations: ScreenNote("Введена только доля секторов, доля ведомств пересчиталась сама.")),
    };

    public static SWTrainingStage[] BudgetSubmit() => new SWTrainingStage[]
    {
        new(0f, 0f, Closeup(), Draft(45, 35, true), SWTrainingPointer.Travel),
        new(2f, 3f, Closeup(), Draft(45, 35, true), SWTrainingPointer.Press),
        new(3f, 6f, Closeup(), Submitted(), Popup: "Проект бюджета подан Канцлеру на одобрение."),
        new(11f, 14f, Closeup(), Draft(45, 35) with { Stale = true },
            Annotations: ScreenNote("Если проект успел измениться на сервере, сначала загрузите актуальную версию."), Alternative: true, Opens: true),
    };

    public static SWTrainingStage[] BudgetApprove() => new SWTrainingStage[]
    {
        new(0f, 0f, Closeup(), Chancellor(SWTrainingBudgetPhase.Pending),
            Annotations: ScreenNote("Проект Минфина подан без помех и ждёт решения канцлера."), Resumes: true),
        new(4f, 4f, Closeup(), Chancellor(SWTrainingBudgetPhase.Pending), SWTrainingPointer.Travel),
        new(6f, 7f, Closeup(), Chancellor(SWTrainingBudgetPhase.Pending), SWTrainingPointer.Press),
        new(7f, 10f, Closeup(), Chancellor(SWTrainingBudgetPhase.Approved), Popup: "Бюджет утверждён. Средства переданы министерствам и секторам."),
        // The other case opens on the project as it was before the approval, and only then the rejection is pressed.
        new(15f, 18f, Closeup(), Chancellor(SWTrainingBudgetPhase.Pending),
            Annotations: ScreenNote("Если канцлер не согласен, отклонить проект можно только с замечанием."), Alternative: true, Opens: true),
        new(19f, 22f, Closeup(), Chancellor(SWTrainingBudgetPhase.Pending, rejectText: RejectExample), SWTrainingPointer.Press, Alternative: true),
        // The popup goes to whoever decided, at their cursor; the finance ministry sees the returned draft separately.
        new(21f, 25f, Closeup(), Chancellor(SWTrainingBudgetPhase.NotSubmitted), Popup: "Проект бюджета отклонён. Минфин уведомлён.",
            Annotations: ScreenNote("Это сообщение видит сам канцлер, отклонивший проект."), Alternative: true),
        new(29f, 33f, Closeup(), new SWTrainingFinancePanel("Отклонён - переделайте", 45, 35, 10, 10, true, RejectReason: RejectExample),
            Annotations: ScreenNote("У министра финансов черновик вернулся с замечанием."), Alternative: true),
    };

    /// <summary>A separate outcome: nobody presses anything, and no popup is promised.</summary>
    public static SWTrainingStage[] BudgetDeadline() => new SWTrainingStage[]
    {
        new(0f, 0f, Closeup("08:59"), Chancellor(SWTrainingBudgetPhase.Pending),
            Annotations: ScreenNote("Проект подан, но канцлер занят карантином и не успел его рассмотреть: ни одобрения, ни отказа."), Resumes: true),
        new(4f, 4f, Closeup("09:00"), Chancellor(SWTrainingBudgetPhase.Approved, "Срок утверждения бюджета истёк"),
            Annotations: ScreenNote("В 09:00 поданный проект применился сам.")),
    };

    /// <summary>A separate outcome: the finance ministry has no minister.</summary>
    public static SWTrainingStage[] BudgetVacancy() => new SWTrainingStage[]
    {
        new(0f, 0f, new SWTrainingVisual(SWTrainingScene.Desk, new[] { Clock() }, ClockText: "07:59")),
        new(2f, 4f, Closeup("07:59"), new SWTrainingBudgetStatusPanel(SWTrainingBudgetPhase.NotSubmitted),
            Annotations: ScreenNote("В Минфине сегодня нет министра, проект никто не подал.")),
        new(4f, 8f, Closeup("08:00"), new SWTrainingBudgetStatusPanel(SWTrainingBudgetPhase.Pending),
            Annotations: ScreenNote("Канцлер или спикер на месте: стандартный проект подан и ждёт их решения.")),
        new(12f, 16f, Closeup("08:00"), new SWTrainingBudgetStatusPanel(SWTrainingBudgetPhase.Approved, "Нет канцлера и спикера"),
            Annotations: ScreenNote("Если нет ни канцлера, ни спикера, поданный проект применяется сразу."), Alternative: true, Opens: true),
    };

    // ---- 28 ---------------------------------------------------------------

    public static SWTrainingStage[] DoctorArrives() => new SWTrainingStage[]
    {
        new(0f, 0f, new SWTrainingVisual(SWTrainingScene.Clinic, new[]
        {
            new SWTrainingOverlay(SWTrainingArt.FigDoctor, SWSenateTrainingArt.DoctorAloneX, SWSenateTrainingArt.SlotY),
        })),
        new(4f, 4f, Treating(0)),
    };

    public static SWTrainingStage[] BeltOpens() => new SWTrainingStage[]
    {
        new(0f, 0f, new SWTrainingVisual(SWTrainingScene.Bench, new[] { new SWTrainingOverlay(SWTrainingArt.GearBeltMedicalClosed, 0, 0) })),
        new(3f, 3f, new SWTrainingVisual(SWTrainingScene.Bench, new[] { new SWTrainingOverlay(SWTrainingArt.GearBeltMedical, 0, 0) })),
    };

    /// <summary>The doctor turns to the patient, opens the actions, chooses «Здоровье», and reads one possible line.</summary>
    public static SWTrainingStage[] PatientCheck() => new SWTrainingStage[]
    {
        new(0f, 0f, Examining(0)),
        new(2f, 3f, Examining(1), Annotations: OpenActions()),
        new(5f, 6f, Examining(1), Annotations: ChooseHealth()),
        new(8f, 9f, Examining(1), Annotations: HealthResult("Он имеет несколько ушибов.","Несколько ушибов: это механическая травма, ей подходит набор для ушибов.")),
        new(16f, 17f, Examining(0), Annotations: TooFar(), Alternative: true, Opens: true),
    };

    /// <summary>One use only: the hand reaches the patient, the 3 seconds run, the stack loses one unit.</summary>
    public static SWTrainingStage[] HealingUse() => new SWTrainingStage[]
    {
        new(0f, 0f, Treating(0), Annotations: new[]
        {
            new SWTrainingAnnotation("Количество: 10", SWTrainingTone.Game, 130, 24, 190),
            new SWTrainingAnnotation("Мира Тессе стоит вплотную к пациенту.", SWTrainingTone.Teaching, 130, 56, 190),
        }, Resumes: true),
        new(ReachStart, 3f, Treating(2, ReachSeconds), Annotations: Count(10)),
        new(ContactStart, 6f, Treating(3), Annotations: new[]
        {
            new SWTrainingAnnotation("Количество: 10", SWTrainingTone.Game, 130, 24, 190),
            new SWTrainingAnnotation("Идёт применение: 3 секунды.", SWTrainingTone.Teaching, 130, 56, 190),
        }),
        new(ContactStart + HealingSeconds, 9f, Treating(3), Annotations: new[]
        {
            new SWTrainingAnnotation("Количество: 9", SWTrainingTone.Game, 130, 24, 190),
            new SWTrainingAnnotation("Применение завершено. Повреждение может уменьшиться, но остаться.", SWTrainingTone.Teaching, 130, 56, 190),
        }),
    };

    /// <summary>The hand first returns from the forearm to the hip, continuing the end of the treatment shot; reduced motion holds it at the hip.</summary>
    public static SWTrainingStage[] PatientRecheck() => new SWTrainingStage[]
    {
        new(0f, 0f, Treating(0, ReachSeconds, true)),
        new(2f, 3f, Examining(1), Annotations: OpenActions()),
        new(5f, 6f, Examining(1), Annotations: ChooseHealth()),
        new(8f, 9f, Examining(1), Annotations: HealthResult("Он имеет лёгкий ушиб.","Остался лёгкий ушиб: травма стала меньше. Если описание не меняется, продолжайте лечение.")),
    };
}
