using System;

namespace Content.Client._SW._Senate.Training;

/// <summary>
/// Timelines of chapters 21 to 27: the other court offices, the correspondent and the guard staff. The court and press
/// panels use the labels of the judicial console and the press window. Hearings, speech and guard work are played by
/// people, not by buttons, so those shots are teaching scenes and say so; nothing in them stands for a game control.
/// </summary>
public static partial class SWTrainingSamples
{
    private const string CourtTitle = "Верховный суд Республики";
    private const string BenchTitle = "Состав верховного суда";
    private const string CaseTitle = "Дело для примера";
    private const string HearingNote = "Учебная сцена. Выступление идёт живой речью в зале, игра его не засчитывает.";

    // One array per set of buttons: rows compare them by reference.
    private static readonly string[] PressTabs = { "Лента", "Черновики", "Релизы", "Диктофон" };
    private static readonly string[] PressEditions = { "Республиканский вестник", "Голос миров", "Корусантская трибуна", "Галактический обозреватель" };

    // ---- shared court -----------------------------------------------------

    /// <summary>The bench tab before the office is taken: the card is read, and the button is there only for a free seat.</summary>
    private static SWTrainingFormPanel BenchOffer(string role, bool aim = false, bool canTake = true) => Form(BenchTitle,
        Txt($"Карта подтверждена: {role}."),
        canTake ? Btn("Принять полномочия", aim, accent: SWTrainingAccent.Good) : Dim("Должность занята или несовместима с текущим статусом"),
        Dim("Состав суда не сформирован."));

    private static SWTrainingStage[] Register(string role, SWTrainingFormPanel after) => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(CourtTitle, CourtTabs, 3)),
        Press(TabRow(CourtTitle, CourtTabs, 3)),
        Aim(BenchOffer(role, true)),
        Press(BenchOffer(role, true)),
        Result(after, note: "Полномочия приняты, имя появилось в составе. " + Example),
        Result(BenchOffer(role, canTake: false), note: "Другой случай: место занято, кнопки нет.", alternative: true));

    /// <summary>What a party sees of the case: its own hints and its own outcome, never the vote.</summary>
    private static SWTrainingFormPanel SideMaterials(bool prosecution) => Form(CaseTitle,
        Choice(CaseViews, 0),
        Dim("Описание дела для примера."),
        Sec("Позиции сторон"),
        Txt(prosecution ? "[Обвинение] Довод для примера." : "[Защита] Довод для примера."),
        prosecution ? Fine("В пользу истца: исход для примера") : Warn("В пользу ответчика: исход для примера"));

    private static SWTrainingFormPanel SideDecision(bool prosecution, string? verdict) => Form("Решение суда",
        Txt(CaseTitle),
        prosecution ? Fine("В пользу истца: исход для примера") : Warn("В пользу ответчика: исход для примера"),
        verdict == null ? Dim(string.Empty) : Txt($"Вердикт: {verdict}"));

    private static SWTrainingStage[] SideCase(bool prosecution) => Steps(
        Aim(TabRow(CourtTitle, CourtTabs, 0)),
        Press(TabRow(CourtTitle, CourtTabs, 0)),
        Result(SideMaterials(prosecution), note: "Видны только доводы и исход вашей стороны. Текст дела задаёт раунд. " + Example));

    private static SWTrainingStage[] SideSpeech(SWTrainingFocus side) => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Court, Array.Empty<SWTrainingOverlay>(), Focus: side), 5f, ScreenNote(HearingNote)),
        Picture(new SWTrainingVisual(SWTrainingScene.Court, Array.Empty<SWTrainingOverlay>(), Focus: SWTrainingFocus.Judges), ResultHold,
            ScreenNote("Судьи слушают обе стороны. Решают они, а не выступающий.")));

    private static SWTrainingStage[] SideVerdict(bool prosecution) => Steps(
        new Step(4f, Closeup("13:05"), SideDecision(prosecution, null), Notes: ScreenNote("Идёт совещание судей. Кнопок голоса у вас нет.")),
        Result(SideDecision(prosecution, "В пользу истца"), note: "Решение вынесли судьи. " + Example, time: "13:30"),
        Result(SideDecision(prosecution, "Решение не вынесено"), note: "Другой случай: к сроку голосов не было.", alternative: true, time: "14:00"));

    // ---- 20А --------------------------------------------------------------

    private static readonly string[] ChanceryNav = { "На подпись", "Декрет", "Суд", "Объявления", "Согласования" };
    private static readonly string[] JudgeCandidates = { OtherExampleName };
    private static readonly string[] NomineeAnswers = { "Принять предложение", "Отказаться" };
    private static readonly string[] ConfirmButtons = { "Утвердить", "Отклонить" };

    private const string ChiefVacant = "Верховный Судья не назначен; судьи: " + OtherExampleName;
    private const string PendingAnswer = "Ожидается ответ " + OtherExampleName + " на предложение поста Верховного Судьи.";
    private const string PendingVote = "Сенат голосует по кандидатуре " + OtherExampleName + " на пост Верховного Судьи.";

    /// <summary>The chancellor's «Суд» section while the chief seat is empty and a judge sits: only a chief can be nominated.</summary>
    private static SWTrainingFormPanel CourtSection(bool aim = false) => Form("Верховный суд",
        Txt(ChiefVacant),
        Bad("Пост Верховного Судьи вакантен"),
        Txt("Кандидат на пост Верховного Судьи (из действующих судей):"),
        Choose(string.Empty, JudgeCandidates, 0),
        Btn("Выдвинуть Верховным Судьёй", aim));

    private static SWTrainingFormPanel CourtPending(string label) => Form("Верховный суд",
        Txt(ChiefVacant),
        Warn(label),
        Bad("Пост Верховного Судьи вакантен"));

    private static SWTrainingFormPanel CourtClosed() => Form("Верховный суд",
        Txt(ChiefVacant),
        Bad("Пост Верховного Судьи вакантен"),
        Dim("Выдвижение закрыто: нет вакансий или не наступило 10:00 первого дня"));

    private static SWTrainingFormPanel NomineeCard(int aim = -1) => Form("Должность",
        Txt("Канцлер предлагает вам пост Верховного Судьи Республики."),
        Dim("Назначение прекращает текущие полномочия."),
        Choice(NomineeAnswers, -1, aim));

    /// <summary>The «Голосование» tab: the session line, the confirmation card, and the line for the absent bill vote.</summary>
    private static SWTrainingFormPanel Confirmation(int yes, int no, string? mine, int aim = -1) => Form("Голосование сената",
        Dim("Нет активного заседания."),
        Txt($"Утверждение: {OtherExampleName} - Верховный Судья"),
        Dim($"ЗА {yes}, ПРОТИВ {no}" + (mine == null ? string.Empty : $", ваш голос: {mine}")),
        mine == null ? Choice(ConfirmButtons, -1, aim) : Choice(ConfirmButtons),
        Dim("Нет активного голосования."));

    /// <summary>After the vote the card is gone; with no bill on the floor only the empty lines remain.</summary>
    private static SWTrainingFormPanel VoteClosed() => Form("Голосование сената",
        Dim("Нет активного заседания."),
        Dim("Нет активного голосования."));

    /// <summary>The court's announcement is a message of its own, outside the vote window.</summary>
    private static Step Announced(string text, string remark, bool alternative = false) => new(ResultHold, Closeup(), VoteClosed(), Notes: new[]
    {
        new SWTrainingAnnotation(text, SWTrainingTone.Game, 330, 176, 270, "Верховный Суд Республики"),
        new SWTrainingAnnotation(remark, SWTrainingTone.Teaching, 580, 24, 130),
    }, Alternative: alternative);

    /// <summary>The judicial console of the sitting judge: the strip shows the empty chief seat, the bench lists the judge.</summary>
    private static SWTrainingFormPanel BenchWithVacancy() => Form(BenchTitle,
        Dim(ChiefVacant),
        Txt($"Республиканский Судья {OtherExampleName}"),
        Dim("Обвинитель: не назначен"),
        Dim("Защитник: не назначен"));

    public static SWTrainingStage[] AppointBench() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(CourtTitle, CourtTabs, 3)),
        Press(TabRow(CourtTitle, CourtTabs, 3)),
        Result(BenchWithVacancy(), note: "Судья есть, верховного нет. Пока так, другие судейские назначения закрыты: следующим выдвигают верховного. " + Example));

    public static SWTrainingStage[] AppointNominate() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow("Канцелярия", ChanceryNav, 2), time: "10:20"),
        Press(TabRow("Канцелярия", ChanceryNav, 2), time: "10:20"),
        new Step(3f, Closeup("10:20"), CourtSection(), Notes: ScreenNote("Раздел «Суд» у канцлера: выбрать можно только из действующих судей.")),
        Aim(CourtSection(true), time: "10:20"),
        Press(CourtSection(true), time: "10:20"),
        Result(CourtPending(PendingAnswer), note: "Кандидат получил уведомление. Второго кандидата до ответа не выдвинуть.", time: "10:20"),
        Result(CourtClosed(), note: "Другой случай: первый день, ещё нет десяти, выдвижение закрыто.", alternative: true, time: "09:30"));

    public static SWTrainingStage[] AppointAnswer() => Steps(
        HandOut(),
        HandIn(),
        Hold(NomineeCard(), "Предложение видно кандидату в разделе «Должность» его рабочей консоли."),
        Aim(NomineeCard(0)),
        Press(NomineeCard(0)),
        Result(CourtPending(PendingVote), note: "Кандидат согласился. Сенат объявил голосование в консоли голосования."),
        Aim(NomineeCard(1), true),
        Press(NomineeCard(1), true),
        Result(CourtSection(), note: "Другой выбор: отказ. Выдвижение снято, канцлер получает уведомление и может предложить другого.", alternative: true));

    /// <summary>The vote ends at a majority of the claimed sectors, when every sector has voted, or after two active hours.</summary>
    public static SWTrainingStage[] AppointConfirm() => Steps(
        Aim(Confirmation(0, 0, null, 0)),
        Press(Confirmation(0, 0, null, 0)),
        Hold(Confirmation(1, 0, "за"), "Голосуют сенаторы с занятым сектором. Голос можно изменить, пока голосование идёт."),
        Hold(Confirmation(2, 1, "за"), "Секторов в примере четыре, значит нужно три голоса ЗА. " + Example, 4f),
        Announced($"Сенат утвердил {OtherExampleName} в должности Верховного Судьи Республики.", "Третий голос ЗА дал большинство. Карточка исчезла."),
        Announced($"Сенат отклонил кандидатуру {OtherExampleName}.", "Другой исход: большинства нет. Канцлер может выдвинуть другого.", true));

    public static SWTrainingStage[] AppointResult() => Steps(
        Picture(AtEntrance(Figure(SWTrainingArt.FigJudge, SWSenateTrainingArt.SlotAX)), 3f, ScreenNote("Учебная сцена: утверждённый судья идёт к судебной консоли.")),
        Aim(TabRow(CourtTitle, CourtTabs, 3)),
        Press(TabRow(CourtTitle, CourtTabs, 3)),
        Hold(Form(BenchTitle, Txt($"Республиканский Судья {OtherExampleName}")), "Состав до утверждения."),
        Result(Form(BenchTitle, Txt($"Верховный Судья {OtherExampleName}"), Dim("Обвинитель: не назначен"), Dim("Защитник: не назначен")),
            note: "Назначение утверждено, судебные полномочия и доступы карты обновлены. Несовместимые посты освобождены."));

    // ---- 21 ---------------------------------------------------------------

    public static SWTrainingStage[] JudgeRegister() => Register($"Республиканский судья {OtherExampleName}",
        Form(BenchTitle, Txt($"Республиканский Судья {OtherExampleName}"), Dim("Обвинитель: не назначен"), Dim("Защитник: не назначен")));

    public static SWTrainingStage[] JudgeWait() => Steps(
        new Step(4f, Closeup("13:05"), Decision(2, "истец"), Notes: ScreenNote("Слушание окончено. Закрыть голосование может верховный или председательствующий судья.")),
        Result(Decision(2, "истец", verdict: "В пользу истца"), note: "Голосование закрыто, вердикт вынесен по голосам судей. " + Example, time: "13:30"),
        Result(Decision(0, null, verdict: "Решение не вынесено"), note: "Другой случай: к 14:00 голосов не было.", alternative: true, time: "14:00"));

    // ---- 22 ---------------------------------------------------------------

    public static SWTrainingStage[] ProsecutorRegister() => Register($"Обвинитель {ExampleName}",
        Form(BenchTitle, Dim("Состав суда не сформирован."), Txt($"Обвинитель: {ExampleName}"), Dim("Защитник: не назначен")));

    public static SWTrainingStage[] ProsecutorCase() => SideCase(true);

    public static SWTrainingStage[] ProsecutorSpeech() => SideSpeech(SWTrainingFocus.Prosecutor);

    public static SWTrainingStage[] ProsecutorVerdict() => SideVerdict(true);

    // ---- 23 ---------------------------------------------------------------

    public static SWTrainingStage[] DefenderRegister() => Register($"Защитник {OtherExampleName}",
        Form(BenchTitle, Dim("Состав суда не сформирован."), Dim("Обвинитель: не назначен"), Txt($"Защитник: {OtherExampleName}")));

    public static SWTrainingStage[] DefenderCase() => SideCase(false);

    public static SWTrainingStage[] DefenderSpeech() => SideSpeech(SWTrainingFocus.Defender);

    public static SWTrainingStage[] DefenderVerdict() => SideVerdict(false);

    // ---- 24 ---------------------------------------------------------------

    private const string RecordOff = "Начать запись (держите датапад)";
    /// <summary>The window's own label, marker included; the press window draws it this way.</summary>
    public const string RecordOn = "● Запись включена — остановить";
    private const string QuoteExample = "Кир Дан: Заседание начнём в десять.";
    private const string ArticleTitle = "Заголовок для примера";

    private static SWTrainingFormPanel PressEditionChoice(int aim = -1) => Form("Голонет: редакция и лента",
        Txt("Голонет"),
        Choice(PressEditions, -1, aim));

    private static SWTrainingFormPanel PressEditionChosen() => Form("Голонет: редакция и лента",
        Txt(PressEditions[0]),
        Btn("Покинуть издание", accent: SWTrainingAccent.Neutral));

    private static SWTrainingFormPanel Recorder(bool recording, bool quote, int aimTab = -1, bool aimRecord = false, bool canAttach = false) => Form(PressEditions[0],
        Choice(PressTabs, aimTab >= 0 ? 0 : 3, aimTab),
        Btn(recording ? RecordOn : RecordOff, aimRecord),
        quote ? Btn(QuoteExample, disabled: !canAttach) : Dim("Записей пока нет."));

    private static SWTrainingFormPanel Article(string context, string title, string body, bool aimNew = false, bool aimSave = false, bool saved = false, bool quote = false, bool aimPublish = false,
        string? header = null, bool canPublish = true) => Form(header ?? PressEditions[0],
        Btn("Черновик", aimNew),
        Dim(context),
        Fld("Заголовок", title),
        quote ? Dim("Цитата: Кир Дан (10:05)   «Заседание начнём в десять.»") : Dim(string.Empty),
        Fld("Текст материала", body),
        Btn("Сохранить", aimSave),
        Btn("Опубликовать", aimPublish, !saved || !canPublish));

    private static SWTrainingFormPanel QuoteAttach(bool aim) => Form(PressEditions[0],
        Choice(PressTabs, 3),
        Btn(RecordOn),
        Btn(QuoteExample, aim));

    /// <summary>The «Черновики» list. Choosing an entry loads its current version into the editor.</summary>
    private static SWTrainingFormPanel DraftList(bool aim) => Form(PressEditions[0],
        Choice(PressTabs, 1),
        Btn(ArticleTitle, aim));

    private static SWTrainingFormPanel Feed(bool published) => Form(PressEditions[0],
        Choice(PressTabs, 0),
        published ? Btn(ArticleTitle) : Dim("Издание пока не опубликовало статей"),
        published ? Dim($"{PressEditions[0]}, {ExampleName}, версия 1") : Dim(string.Empty));

    public static SWTrainingStage[] PressEdition() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Entrance, new[] { new SWTrainingOverlay(SWTrainingArt.FigCorrespondent, SWSenateTrainingArt.SlotAX, SWSenateTrainingArt.SlotY, 0, true) }), 3f),
        Hold(PressEditionChoice(), "Выбор редакции есть только у корреспондента с правом записи."),
        Aim(PressEditionChoice(0)),
        Press(PressEditionChoice(0)),
        Result(PressEditionChosen(), note: "Редакция выбрана, её название в заголовке окна. Публичное чтение ленты прав автора не даёт."));

    public static SWTrainingStage[] PressRecord() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Desk, new[] { new SWTrainingOverlay(SWTrainingArt.HandDatapad, 440, 142, 0, true) }), 3f),
        Aim(Recorder(false, false, aimTab: 3)),
        Press(Recorder(false, false, aimTab: 3)),
        Aim(Recorder(false, false, aimRecord: true)),
        Press(Recorder(false, false, aimRecord: true)),
        Result(Recorder(true, false), note: "Запись идёт, пока датапад в активной руке. Это не КПК."));

    public static SWTrainingStage[] PressQuotes() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Entrance, new[]
        {
            new SWTrainingOverlay(SWTrainingArt.FigCorrespondent, SWSenateTrainingArt.SlotAX, SWSenateTrainingArt.SlotY),
            new SWTrainingOverlay(SWTrainingArt.FigUsher, SWSenateTrainingArt.SlotBX, SWSenateTrainingArt.SlotY, 1),
        }), 4f, ScreenNote("Учебная сцена. Цитатой станет только услышанная вами речь.")),
        Result(Recorder(true, true), note: "Речь записана как цитата. Обстановка вокруг неё в цитату не попадает. " + Example));

    // The window separates the context with middle dots; the training writes a comma there.
    private const string SavedDraft = "Черновик, версия 1";
    private const string QuotedDraft = "Черновик, версия 2";
    private const string ArticleBody = "Текст для примера.";

    /// <summary>
    /// The window works on the version it last loaded. Saving a new draft leaves the editor empty-handed, and attaching
    /// a quote raises the version on the server, so the draft is chosen from the list again before each next step.
    /// </summary>
    public static SWTrainingStage[] PressDraft() => Steps(
        Aim(Article("Выберите публикацию или создайте черновик.", string.Empty, string.Empty, aimNew: true)),
        Press(Article("Выберите публикацию или создайте черновик.", string.Empty, string.Empty, aimNew: true)),
        Hold(Article("Новый черновик", ArticleTitle, ArticleBody), Example, 4f),
        Aim(Article("Новый черновик", ArticleTitle, ArticleBody, aimSave: true)),
        Press(Article("Новый черновик", ArticleTitle, ArticleBody, aimSave: true)),
        Hold(DraftList(false), "Черновик сохранён и появился в списке «Черновики»."),
        Aim(DraftList(true)),
        Press(DraftList(true)),
        Hold(Article(SavedDraft, ArticleTitle, ArticleBody, saved: true), "Черновик выбран, к нему можно прикрепить цитату."),
        Aim(QuoteAttach(true)),
        Press(QuoteAttach(true)),
        Aim(DraftList(true)),
        Press(DraftList(true)),
        Result(Article(QuotedDraft, ArticleTitle, ArticleBody, saved: true, quote: true),
            note: "Цитата прикреплена, версия выросла. Выберите черновик снова, чтобы видеть её. Цитат не больше трёх."));

    public static SWTrainingStage[] PressPublish() => Steps(
        Aim(Article(QuotedDraft, ArticleTitle, ArticleBody, saved: true, quote: true, aimPublish: true)),
        Press(Article(QuotedDraft, ArticleTitle, ArticleBody, saved: true, quote: true, aimPublish: true)),
        Result(Feed(true), note: "Статья вышла в ленте вашего издания вместе с цитатой. Если окно держало старую версию, публикация бы не прошла."),
        Result(Article(QuotedDraft, ArticleTitle, ArticleBody, saved: true, quote: true, header: "Голонет", canPublish: false),
            note: "Другой случай: редакция не выбрана, в заголовке просто «Голонет», кнопка «Опубликовать» отключена.", alternative: true));

    // ---- 25-27 ------------------------------------------------------------

    private const string StaffNote = "Учебная сцена. Договорённости здесь живые, игра их не отмечает.";

    private static SWTrainingVisual AtEntrance(params SWTrainingOverlay[] figures) => new(SWTrainingScene.Entrance, figures);

    private static SWTrainingVisual WideHall() => new(SWTrainingScene.Hall, Array.Empty<SWTrainingOverlay>());

    private static SWTrainingStage[] Kit(SWTrainingArt figure, SWTrainingArt bench, string note) => Steps(
        Picture(AtEntrance(Figure(figure, SWSenateTrainingArt.SlotAX)), 3f),
        Picture(new SWTrainingVisual(SWTrainingScene.Bench, new[] { new SWTrainingOverlay(bench, 0, 0) }), ResultHold, ScreenNote(note)));

    private static SWTrainingStage[] Pair(SWTrainingArt first, SWTrainingArt second, string? note = null) => Steps(
        Picture(WideHall(), 3f),
        Picture(AtEntrance(Figure(first, SWSenateTrainingArt.SlotAX), Figure(second, SWSenateTrainingArt.SlotBX)), 3f),
        Picture(AtEntrance(Figure(first, SWSenateTrainingArt.SlotAX, 1), Figure(second, SWSenateTrainingArt.SlotBX)), ResultHold, ScreenNote(note ?? StaffNote)));

    private static SWTrainingStage[] Watch(SWTrainingArt figure, string note) => Steps(
        Picture(AtEntrance(Figure(figure, SWSenateTrainingArt.SlotAX)), 4f),
        Picture(AtEntrance(Figure(figure, SWSenateTrainingArt.SlotAX, 1)), ResultHold, ScreenNote(note)));

    private const string AccessNote = "Доступы заданы ролью. Проходит ли карта в конкретную дверь, проверьте на месте.";
    private const string KitNote = "Учебная иллюстрация стартового набора. Что у вас сейчас, смотрите в инвентаре.";

    // Each social scene is a chain: who starts, what the other does in reply, and the visible state after it. Figures
    // keep their places within a plan; anyone who arrives or leaves does so across a cut to another plan.

    private static SWTrainingOverlay At(SWTrainingArt art, bool left, int frame = 0) =>
        Figure(art, left ? SWSenateTrainingArt.SlotAX : SWSenateTrainingArt.SlotBX, frame);

    private static Step Scene(float hold, string? note, params SWTrainingOverlay[] figures) =>
        Picture(AtEntrance(figures), hold, note == null ? null : ScreenNote(note));

    private static Step Cut(SWTrainingScene scene, float hold = 2f, SWTrainingFocus focus = SWTrainingFocus.None, string? note = null) =>
        Picture(new SWTrainingVisual(scene, Array.Empty<SWTrainingOverlay>(), Focus: focus), hold, note == null ? null : ScreenNote(note));

    public static SWTrainingStage[] UsherStart() => Watch(SWTrainingArt.FigUsher, AccessNote);
    public static SWTrainingStage[] UsherBelt() => Kit(SWTrainingArt.FigUsher, SWTrainingArt.GearBeltSecurity, KitNote);

    /// <summary>The empty chamber, the usher asks the speaker, the speaker answers, and the session opens.</summary>
    public static SWTrainingStage[] UsherHall() => Steps(
        Cut(SWTrainingScene.HallEmpty, 3f, note: "Зал до заседания."),
        Scene(3f, "Пристав спрашивает спикера, что подготовить.", At(SWTrainingArt.FigUsher, true, 1), At(SWTrainingArt.FigSpeaker, false)),
        Scene(3f, "Спикер отвечает.", At(SWTrainingArt.FigUsher, true), At(SWTrainingArt.FigSpeaker, false, 1)),
        Cut(SWTrainingScene.HallSpeaker, ResultHold, note: "Заседание открыто. Договорённость была разговором, игра её не отмечает."));

    /// <summary>The usher calls, the officer answers on the headset elsewhere, then arrives across a cut.</summary>
    public static SWTrainingStage[] UsherGuard() => Steps(
        Scene(3f, "Пристав зовёт офицера по гарнитуре.", At(SWTrainingArt.FigUsher, false, 1)),
        Cut(SWTrainingScene.Hall),
        Scene(3f, "Офицер слышит вызов и отвечает по гарнитуре.", At(SWTrainingArt.FigGuardOfficerRadio, true, 1)),
        Cut(SWTrainingScene.Hall),
        Scene(3f, "Офицер подошёл.", At(SWTrainingArt.FigGuardOfficerRadio, true), At(SWTrainingArt.FigUsher, false)),
        Scene(ResultHold, "Пристав объясняет, что нужно. Если офицера нет на месте, ответа может не быть.", At(SWTrainingArt.FigGuardOfficerRadio, true), At(SWTrainingArt.FigUsher, false, 1)));

    /// <summary>The usher meets a judge, shows the way, and the hearing room is ready.</summary>
    public static SWTrainingStage[] UsherService() => Steps(
        Scene(3f, "Судья пришёл ко входу.", At(SWTrainingArt.FigJudge, true), At(SWTrainingArt.FigUsherGuide, false)),
        Scene(3f, "Пристав показывает дорогу в зал суда.", At(SWTrainingArt.FigJudge, true), At(SWTrainingArt.FigUsherGuide, false, 1)),
        Cut(SWTrainingScene.Court, ResultHold, SWTrainingFocus.Judges, "Судья на месте. Пристав помогает работе, а не голосует. Учебная сцена."));

    public static SWTrainingStage[] GuardHeadStart() => Watch(SWTrainingArt.FigGuardHead, AccessNote);
    public static SWTrainingStage[] GuardHeadKit() => Kit(SWTrainingArt.FigGuardHead, SWTrainingArt.GearGuardHead, KitNote);

    /// <summary>The head names a place, the officer confirms, and stays there after the head has gone.</summary>
    public static SWTrainingStage[] GuardHeadPosts() => Steps(
        Scene(3f, "Глава указывает офицеру место у входа.", At(SWTrainingArt.FigGuardHeadPoint, true, 1), At(SWTrainingArt.FigGuardOfficer, false)),
        Scene(3f, "Офицер подтверждает.", At(SWTrainingArt.FigGuardHeadPoint, true), At(SWTrainingArt.FigGuardOfficer, false, 1)),
        Cut(SWTrainingScene.Hall),
        Scene(ResultHold, "Офицер остался на месте. Это договорённость: системы постов в игре нет.", At(SWTrainingArt.FigGuardOfficer, false)));

    /// <summary>Someone comes up, the head asks before acting, and the visitor goes on; force is not the first answer.</summary>
    public static SWTrainingStage[] GuardHeadWatch() => Steps(
        Scene(3f, "Глава у входа.", At(SWTrainingArt.FigGuardHeadPoint, true)),
        Cut(SWTrainingScene.Hall),
        Scene(3f, "Подходит посетитель.", At(SWTrainingArt.FigGuardHeadPoint, true), At(SWTrainingArt.FigCorrespondent, false)),
        Scene(3f, "Глава останавливает его жестом и спрашивает.", At(SWTrainingArt.FigGuardHeadPoint, true, 1), At(SWTrainingArt.FigCorrespondent, false)),
        Scene(ResultHold, "Посетитель объясняет цель. В этом примере угрозы нет, он проходит.", At(SWTrainingArt.FigGuardHeadPoint, true), At(SWTrainingArt.FigCorrespondent, false, 1)),
        new Step(ResultHold, AtEntrance(At(SWTrainingArt.FigGuardHeadPoint, true, 1), At(SWTrainingArt.FigGuardOfficer, false)),
            Notes: ScreenNote("Другой случай: угроза есть. Глава зовёт офицеров, а не действует один."), Alternative: true));

    /// <summary>The head and the usher agree, and the senators pass into the chamber.</summary>
    public static SWTrainingStage[] GuardHeadHall() => Steps(
        Scene(3f, "Глава согласует с приставом проход сенаторов.", At(SWTrainingArt.FigGuardHead, true, 1), At(SWTrainingArt.FigUsherGuide, false)),
        Scene(3f, "Пристав показывает, где пройдут сенаторы.", At(SWTrainingArt.FigGuardHead, true), At(SWTrainingArt.FigUsherGuide, false, 1)),
        Cut(SWTrainingScene.Hall),
        Picture(AtEntrance(new SWTrainingOverlay(SWTrainingArt.FigSenators, SWSenateTrainingArt.SlotAX, SWSenateTrainingArt.SlotY, 0, true)), ResultHold,
            ScreenNote("Сенаторы проходят в зал, заседание продолжается. Учебная сцена.")));

    public static SWTrainingStage[] OfficerStart() => Watch(SWTrainingArt.FigGuardOfficer, AccessNote);
    public static SWTrainingStage[] OfficerKit() => Kit(SWTrainingArt.FigGuardOfficer, SWTrainingArt.GearGuardOfficer, KitNote);

    /// <summary>The officer asks where to stand, the head points to the place, and the officer is there.</summary>
    public static SWTrainingStage[] OfficerPost() => Steps(
        Scene(3f, "Офицер спрашивает главу, где встать.", At(SWTrainingArt.FigGuardHeadPoint, true), At(SWTrainingArt.FigGuardOfficer, false, 1)),
        Scene(3f, "Глава указывает место у входа.", At(SWTrainingArt.FigGuardHeadPoint, true, 1), At(SWTrainingArt.FigGuardOfficer, false)),
        Cut(SWTrainingScene.Hall),
        Scene(ResultHold, "Офицер у входа. Сообщите главе, что вы на месте.", At(SWTrainingArt.FigGuardOfficer, false)));

    /// <summary>The officer notices, speaks into the headset, and across a cut the message reaches the head.</summary>
    public static SWTrainingStage[] OfficerWarn() => Steps(
        Scene(3f, "Офицер замечает необычного посетителя.", At(SWTrainingArt.FigGuardOfficerRadio, true), At(SWTrainingArt.FigCorrespondent, false)),
        Scene(3f, "Офицер сообщает по гарнитуре.", At(SWTrainingArt.FigGuardOfficerRadio, true, 1), At(SWTrainingArt.FigCorrespondent, false)),
        Cut(SWTrainingScene.Hall),
        Scene(3f, "Сообщение слышит глава гвардии.", At(SWTrainingArt.FigGuardHead, true), At(SWTrainingArt.FigUsher, false)),
        Scene(ResultHold, "Глава передаёт приставу. Что делать дальше, решают коллеги, а не игра.", At(SWTrainingArt.FigGuardHead, true, 1), At(SWTrainingArt.FigUsher, false)));
    /// <summary>
    /// A situation at the entrance, an action agreed with the head, and the visible result: the entrance is clear and the
    /// session goes on. Every step is people talking; the outcome is one possible case, not a promise of the game.
    /// </summary>
    public static SWTrainingStage[] OfficerJudge() => Steps(
        Scene(3f, "У входа посетитель спорит и не уходит, заседание вот-вот начнётся.", At(SWTrainingArt.FigGuardOfficer, true), At(SWTrainingArt.FigCorrespondent, false, 1)),
        Scene(3f, "Офицер оценивает: оружия не видно, угрозы людям нет.", At(SWTrainingArt.FigGuardOfficer, true, 1), At(SWTrainingArt.FigCorrespondent, false)),
        Cut(SWTrainingScene.Hall),
        Scene(3f, "Офицер докладывает главе.", At(SWTrainingArt.FigGuardHead, true), At(SWTrainingArt.FigGuardOfficer, false, 1)),
        Scene(3f, "Решение главы: проводить посетителя от входа словами, без силы.", At(SWTrainingArt.FigGuardHead, true, 1), At(SWTrainingArt.FigGuardOfficer, false)),
        Cut(SWTrainingScene.Hall),
        Scene(3f, "Офицер показывает посетителю путь от входа.", At(SWTrainingArt.FigGuardOfficer, true, 1), At(SWTrainingArt.FigCorrespondent, false)),
        Cut(SWTrainingScene.Hall),
        Scene(3f, "Посетитель ушёл, офицер снова на своём месте.", At(SWTrainingArt.FigGuardOfficer, true)),
        Cut(SWTrainingScene.HallSpeaker, ResultHold, note: "Вход свободен, заседание идёт. В этом примере обошлось без силы. По существу решает Сенат, а не охрана."),
        new Step(ResultHold, AtEntrance(At(SWTrainingArt.FigGuardOfficer, true, 1), At(SWTrainingArt.FigCorrespondent, false, 1)),
            Notes: ScreenNote("Другой случай: посетитель не уходит. Офицер снова зовёт главу; дальше решают вместе."), Alternative: true));
}
