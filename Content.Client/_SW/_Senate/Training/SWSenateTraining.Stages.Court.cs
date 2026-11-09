using System;

namespace Content.Client._SW._Senate.Training;

/// <summary>
/// Timelines of chapters 20А to 27: the court offices, the correspondent and the guard staff. The court and press
/// panels use the labels of the judicial console and the press window. Hearings, speech and guard work are played by
/// people, not by buttons; those shots show people acting and what follows, and no game control stands for them.
/// </summary>
public static partial class SWTrainingSamples
{
    private const string CourtTitle = "Верховный суд Республики";
    private const string BenchTitle = "Состав верховного суда";
    private const string ProsecutorName = "Лира Ванн";
    private const string DefenderName = "Оррин Кейд";
    private const string CorrespondentName = "Ника Рэй";
    private const string UsherName = "Ден Маро";
    private const string ProsecutionHint = "[Обвинение] Собственные данные санитарных властей показывают локализацию вспышки на вторые сутки; дальнейший карантин не имел достаточных медицинских оснований.";
    private const string DefenseHint = "[Защита] При угрозе жизни приоритет осторожности является обязанностью властей, а не халатностью.";

    // One array per set of buttons: rows compare them by reference.
    private static readonly string[] PressTabs = { "Лента", "Черновики", "Релизы", "Диктофон" };
    private static readonly string[] PressEditions = { "Республиканский вестник", "Голос миров", "Корусантская трибуна", "Галактический обозреватель" };

    // ---- shared court -----------------------------------------------------

    /// <summary>The bench tab before the office is taken: the card is read, and the button is there only for a free seat.</summary>
    private static SWTrainingFormPanel BenchOffer(string role, bool aim = false, bool canTake = true) => Form(BenchTitle,
        Txt($"Карта подтверждена: {role}."),
        canTake ? Btn("Принять полномочия", aim, accent: SWTrainingAccent.Good) : Dim("Должность занята или несовместима с текущим статусом"),
        Dim("Состав суда не сформирован."));

    private static SWTrainingStage[] Register(string role, string who, SWTrainingFormPanel after) => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(CourtTitle, CourtTabs, 3)),
        Press(TabRow(CourtTitle, CourtTabs, 3)),
        Aim(BenchOffer(role, true)),
        Press(BenchOffer(role, true)),
        Result(after, note: $"Полномочия приняты: {who} теперь в составе суда."),
        Result(BenchOffer(role, canTake: false), note: "Если место уже занято, кнопки нет.", alternative: true));

    /// <summary>What a party sees of the case: its own hints and its own outcome, never the vote.</summary>
    private static SWTrainingFormPanel SideMaterials(bool prosecution) => Form(CourtCase,
        Choice(CaseViews, 0),
        Dim("Корусант оспаривает девять суток закрытых маршрутов из-за кореллианского гриппа."),
        Sec("Позиции сторон"),
        Txt(prosecution ? ProsecutionHint : DefenseHint),
        prosecution ? Fine($"В пользу истца: {PlaintiffWin}") : Warn($"В пользу ответчика: {DefendantWin}"));

    /// <summary>Before the verdict a party sees its own outcome; once the plaintiff wins, the plaintiff's outcome is what holds.</summary>
    private static SWTrainingFormPanel SideDecision(bool prosecution, string? verdict)
    {
        var plaintiff = verdict == "В пользу истца" || verdict == null && prosecution;
        return Form("Решение суда",
            Txt(CourtCase),
            plaintiff ? Fine($"В пользу истца: {PlaintiffWin}") : Warn($"В пользу ответчика: {DefendantWin}"),
            verdict == null ? Dim(string.Empty) : Txt($"Вердикт: {verdict}"));
    }

    private static SWTrainingStage[] SideCase(bool prosecution) => Steps(
        Aim(TabRow(CourtTitle, CourtTabs, 0)),
        Press(TabRow(CourtTitle, CourtTabs, 0)),
        Result(SideMaterials(prosecution), note: prosecution
            ? "Лира Ванн видит доводы обвинения и исход в пользу Корусанта. Из них она строит речь."
            : "Оррин Кейд видит доводы защиты и исход в пользу Республики."));

    private static SWTrainingStage[] SideSpeech(SWTrainingFocus side) => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Court, Array.Empty<SWTrainingOverlay>(), Focus: side), 5f, ScreenNote(side == SWTrainingFocus.Prosecutor
            ? "Лира Ванн: вспышку локализовали на вторые сутки, а маршруты держали закрытыми ещё неделю."
            : "Оррин Кейд: при угрозе жизни власти обязаны быть осторожными.")),
        Picture(new SWTrainingVisual(SWTrainingScene.Court, Array.Empty<SWTrainingOverlay>(), Focus: SWTrainingFocus.Judges), ResultHold,
            ScreenNote("Судьи выслушали речь. Решать будут они.")));

    private static SWTrainingStage[] SideVerdict(bool prosecution) => Steps(
        new Step(4f, Closeup("13:05"), SideDecision(prosecution, null), Notes: ScreenNote("Судьи совещаются. Сторона ждёт решения.")),
        Result(SideDecision(prosecution, "В пользу истца"), note: prosecution
            ? "Судьи поддержали Корусант: карантин признан чрезмерным, сектор получит компенсацию."
            : "Судьи поддержали Корусант, позиция защиты не прошла: карантин признан чрезмерным, сектор получит компенсацию.", time: "13:30"),
        Result(SideDecision(prosecution, "Решение не вынесено"), note: "Если к 14:00 судьи не проголосовали, решения не будет.", alternative: true, time: "14:00"));

    // ---- 20А --------------------------------------------------------------

    private static readonly string[] ChanceryNav = { "На подпись", "Декрет", "Суд", "Объявления", "Согласования" };
    private static readonly string[] JudgeCandidates = { ChiefName };
    private static readonly string[] NomineeAnswers = { "Принять предложение", "Отказаться" };
    private static readonly string[] ConfirmButtons = { "Утвердить", "Отклонить" };

    private const string ChiefVacant = "Верховный Судья не назначен; судьи: " + ChiefName;
    // Game lines insert the stored name as it is; only the training's own remarks decline it.
    private const string PendingAnswer = "Ожидается ответ " + ChiefName + " на предложение поста Верховного Судьи.";
    private const string PendingVote = "Сенат голосует по кандидатуре " + ChiefName + " на пост Верховного Судьи.";

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
        Txt($"Утверждение: {ChiefName} - Верховный Судья"),
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
        Txt($"Республиканский Судья {ChiefName}"),
        Dim("Обвинитель: не назначен"),
        Dim("Защитник: не назначен"));

    public static SWTrainingStage[] AppointBench() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(CourtTitle, CourtTabs, 3)),
        Press(TabRow(CourtTitle, CourtTabs, 3)),
        Result(BenchWithVacancy(), note: "Арен Солт единственный судья, верховного нет. Пока так, другие судейские назначения закрыты."));

    public static SWTrainingStage[] AppointNominate() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow("Канцелярия", ChanceryNav, 2), time: "10:20"),
        Press(TabRow("Канцелярия", ChanceryNav, 2), time: "10:20"),
        new Step(3f, Closeup("10:20"), CourtSection(), Notes: ScreenNote("Раздел «Суд» у канцлера: выбрать можно только из действующих судей.")),
        Aim(CourtSection(true), time: "10:20"),
        Press(CourtSection(true), time: "10:20"),
        Result(CourtPending(PendingAnswer), note: "Кандидат получил уведомление. Второго кандидата до ответа не выдвинуть.", time: "10:20"),
        Result(CourtClosed(), note: "В первый день до 10:00 выдвижение закрыто.", alternative: true, time: "09:30"));

    public static SWTrainingStage[] AppointAnswer() => Steps(
        Back("В 10:20 выдвижение было открыто: канцлер выдвинул Арена Солта, и тот получил уведомление.", HandOut()),
        HandIn(),
        Hold(NomineeCard(), "Арен Солт открыл раздел «Должность» своей рабочей консоли."),
        Aim(NomineeCard(0)),
        Press(NomineeCard(0)),
        Result(CourtPending(PendingVote), note: "Арен Солт согласился. Сенат объявил голосование в консоли голосования."),
        Case("Кандидат вправе и отказаться от поста.", NomineeCard()),
        Aim(NomineeCard(1), true),
        Press(NomineeCard(1), true),
        Result(CourtSection(), note: "Выдвижение снято. Канцлер получает уведомление, и в его разделе «Суд» снова можно выдвинуть судью.", alternative: true, continues: true));

    /// <summary>The vote ends at a majority of the claimed sectors, when every sector has voted, or after two active hours.</summary>
    public static SWTrainingStage[] AppointConfirm() => Steps(
        Back("Арен Солт принял предложение. Сенат голосует по его кандидатуре.", Confirmation(0, 0, null)),
        Aim(Confirmation(0, 0, null, 0)),
        Press(Confirmation(0, 0, null, 0)),
        Hold(Confirmation(1, 0, "за"), "Голосуют сенаторы с занятым сектором. Голос можно изменить, пока голосование идёт."),
        Hold(Confirmation(2, 1, "за"), "Занятых секторов четыре, значит нужно три голоса ЗА.", 4f),
        Hold(Confirmation(2, 1, null), $"Последним голосует {ThirdMember}, у своей консоли.", 3f),
        Aim(Confirmation(2, 1, null, 0)),
        Press(Confirmation(2, 1, null, 0)),
        Hold(VoteClosed(), "Это третий голос ЗА из четырёх занятых секторов: большинство набрано, и утверждение решено сразу.", 4f),
        Announced($"Сенат утвердил {ChiefName} в должности Верховного Судьи Республики.", "Арена Солта утвердили со счётом 3:1."),
        Announced($"Сенат отклонил кандидатуру {ChiefName}.", "Без большинства кандидатура отклонена. Канцлер может выдвинуть другого.", true));

    public static SWTrainingStage[] AppointResult() => Steps(
        Back("Сенат утвердил Арена Солта. Он идёт к судебной консоли.", Picture(AtEntrance(Figure(SWTrainingArt.FigJudge, SWSenateTrainingArt.SlotAX)), 3f)),
        Aim(TabRow(CourtTitle, CourtTabs, 3)),
        Press(TabRow(CourtTitle, CourtTabs, 3)),
        Hold(Form(BenchTitle, Txt($"Республиканский Судья {ChiefName}")), "Состав до утверждения."),
        Result(Form(BenchTitle, Txt($"Верховный Судья {ChiefName}"), Dim("Обвинитель: не назначен"), Dim("Защитник: не назначен")),
            note: "Назначение утверждено, судебные полномочия и доступы карты обновлены. Несовместимые посты освобождены."));

    // ---- 21 ---------------------------------------------------------------

    public static SWTrainingStage[] JudgeRegister() => Register($"Республиканский судья {OtherExampleName}", OtherExampleName,
        Form(BenchTitle, Txt($"Верховный Судья {ChiefName}"), Txt($"Республиканский Судья {OtherExampleName}"), Dim("Обвинитель: не назначен"), Dim("Защитник: не назначен")));

    public static SWTrainingStage[] JudgeWait() => Steps(
        new Step(4f, Closeup("13:05"), Decision(2, "истец"), Notes: ScreenNote("Слушание окончено. Закрыть голосование может Арен Солт как верховный судья.")),
        Result(Decision(2, "истец", verdict: "В пользу истца"), note: "Арен Солт закрыл голосование. Решили оба голоса, а не один.", time: "13:30"),
        Result(Decision(0, null, verdict: "Решение не вынесено"), note: "Если к 14:00 голосов нет, решения не будет.", alternative: true, time: "14:00"));

    // ---- 22 ---------------------------------------------------------------

    public static SWTrainingStage[] ProsecutorRegister() => Register($"Обвинитель {ProsecutorName}", ProsecutorName,
        Form(BenchTitle, Dim("Состав суда не сформирован."), Txt($"Обвинитель: {ProsecutorName}"), Dim("Защитник: не назначен")));

    public static SWTrainingStage[] ProsecutorCase() => SideCase(true);

    public static SWTrainingStage[] ProsecutorSpeech() => SideSpeech(SWTrainingFocus.Prosecutor);

    public static SWTrainingStage[] ProsecutorVerdict() => SideVerdict(true);

    // ---- 23 ---------------------------------------------------------------

    public static SWTrainingStage[] DefenderRegister() => Register($"Защитник {DefenderName}", DefenderName,
        Form(BenchTitle, Dim("Состав суда не сформирован."), Dim($"Обвинитель: {ProsecutorName}"), Txt($"Защитник: {DefenderName}")));

    public static SWTrainingStage[] DefenderCase() => SideCase(false);

    public static SWTrainingStage[] DefenderSpeech() => SideSpeech(SWTrainingFocus.Defender);

    public static SWTrainingStage[] DefenderVerdict() => SideVerdict(false);

    // ---- 24 ---------------------------------------------------------------

    private const string RecordOff = "Начать запись (держите датапад)";
    /// <summary>The window's own label, marker included; the press window draws it this way.</summary>
    public const string RecordOn = "● Запись включена — остановить";
    private const string QuoteExample = UsherName + ": Галерея закрыта до начала заседания.";
    private const string ArticleTitle = "Карантин в Корусанте: что решил Сенат";

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
        quote ? Dim($"Цитата: {UsherName} (09:55)   «Галерея закрыта до начала заседания.»") : Dim(string.Empty),
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
        published ? Dim($"{PressEditions[0]}, {CorrespondentName}, версия 1") : Dim(string.Empty));

    public static SWTrainingStage[] PressEdition() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Entrance, new[] { new SWTrainingOverlay(SWTrainingArt.FigCorrespondent, SWSenateTrainingArt.SlotAX, SWSenateTrainingArt.SlotY, 0, true) }), 3f,
            ScreenNote("Ника Рэй достаёт редакционный датапад: сегодня она пишет о карантине.")),
        Hold(PressEditionChoice(), "Выбор редакции есть только у корреспондента с правом записи."),
        Aim(PressEditionChoice(0)),
        Press(PressEditionChoice(0)),
        Result(PressEditionChosen(), note: "Она пишет для «Республиканского вестника». Название издания теперь в заголовке окна."));

    public static SWTrainingStage[] PressRecord() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Desk, new[] { new SWTrainingOverlay(SWTrainingArt.HandDatapad, 440, 142, 0, true) }), 3f,
            ScreenNote("Датапад в активной руке.")),
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
        }), 4f, ScreenNote("Пристав Ден Маро объявляет: галерея закрыта до начала заседания.")),
        Result(Recorder(true, true), note: "Его слова записаны как цитата. Что было вокруг, в цитату не попало."));

    // The window separates the context with middle dots; the training writes a comma there.
    private const string SavedDraft = "Черновик, версия 1";
    private const string QuotedDraft = "Черновик, версия 2";
    private const string ArticleBody = "Нижние уровни Корусанта закрыты, суд рассмотрит иск сектора.";

    /// <summary>
    /// The window works on the version it last loaded. Saving a new draft leaves the editor empty-handed, and attaching
    /// a quote raises the version on the server, so the draft is chosen from the list again before each next step.
    /// </summary>
    public static SWTrainingStage[] PressDraft() => Steps(
        Aim(Article("Выберите публикацию или создайте черновик.", string.Empty, string.Empty, aimNew: true)),
        Press(Article("Выберите публикацию или создайте черновик.", string.Empty, string.Empty, aimNew: true)),
        Hold(Article("Новый черновик", ArticleTitle, ArticleBody), "Ника вводит заголовок и текст.", 4f),
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
        Result(Feed(true), note: "Статья вышла в «Республиканском вестнике» вместе с цитатой пристава."),
        Result(Article(QuotedDraft, ArticleTitle, ArticleBody, saved: true, quote: true, header: "Голонет", canPublish: false),
            note: "Без выбранной редакции в заголовке просто «Голонет», и кнопка «Опубликовать» отключена.", alternative: true));

    // ---- 25-27 ------------------------------------------------------------

    private static SWTrainingVisual AtEntrance(params SWTrainingOverlay[] figures) => new(SWTrainingScene.Entrance, figures);

    private static SWTrainingVisual WideHall() => new(SWTrainingScene.Hall, Array.Empty<SWTrainingOverlay>());

    private static SWTrainingStage[] Kit(SWTrainingArt figure, SWTrainingArt bench, string note) => Steps(
        Picture(AtEntrance(Figure(figure, SWSenateTrainingArt.SlotAX)), 3f),
        Picture(new SWTrainingVisual(SWTrainingScene.Bench, new[] { new SWTrainingOverlay(bench, 0, 0) }), ResultHold, ScreenNote(note)));

    private static SWTrainingStage[] Watch(SWTrainingArt figure, string first, string note) => Steps(
        Picture(AtEntrance(Figure(figure, SWSenateTrainingArt.SlotAX)), 4f, ScreenNote(first)),
        Picture(AtEntrance(Figure(figure, SWSenateTrainingArt.SlotAX, 1)), ResultHold, ScreenNote(note)));

    private const string AccessNote = "КПК и гарнитура на месте. Доступы даёт роль, но какая дверь откроется, проверяйте у двери.";

    // Each social scene is a chain: who starts, what the other does in reply, and the visible state after it. Figures
    // keep their places within a plan; anyone who arrives or leaves does so across a cut to another plan.

    private static SWTrainingOverlay At(SWTrainingArt art, bool left, int frame = 0) =>
        Figure(art, left ? SWSenateTrainingArt.SlotAX : SWSenateTrainingArt.SlotBX, frame);

    private static Step Scene(float hold, string? note, params SWTrainingOverlay[] figures) =>
        Picture(AtEntrance(figures), hold, note == null ? null : ScreenNote(note));

    private static Step Cut(SWTrainingScene scene, float hold = 2f, SWTrainingFocus focus = SWTrainingFocus.None, string? note = null) =>
        Picture(new SWTrainingVisual(scene, Array.Empty<SWTrainingOverlay>(), Focus: focus), hold, note == null ? null : ScreenNote(note));

    public static SWTrainingStage[] UsherStart() => Watch(SWTrainingArt.FigUsher, "Ден Маро заступает приставом перед утренним заседанием.", AccessNote);
    /// <summary>The usher's filled security belt, item by item: what each one is for.</summary>
    public static SWTrainingStage[] UsherBelt() => Steps(
        Picture(AtEntrance(Figure(SWTrainingArt.FigUsher, SWSenateTrainingArt.SlotAX)), 3f, ScreenNote("Перед заседанием Ден Маро открывает пояс охраны.")),
        Picture(UsherBench(SWTrainingArt.GearItemStunbaton), 4f, ScreenNote("Оглушающая дубинка: чтобы остановить того, кто нападает или рвётся в зал.")),
        Picture(UsherBench(SWTrainingArt.GearItemHandcuffs), 4f, ScreenNote("Две пары наручников: задержанного передают гвардии.")),
        Picture(UsherBench(SWTrainingArt.GearItemHoloprojector), 4f, ScreenNote("Голопроектор ставит временный барьер: им перекрывают проход к галерее или трибуне.")),
        Picture(UsherBench(SWTrainingArt.GearItemRadio), ResultHold, ScreenNote("Ручная рация говорит на канале «Безопасность». В гарнитурах Сената его нет, поэтому с гвардией связывайтесь гарнитурой.")));

    /// <summary>The belt, and beside it the one item being explained, drawn from its own sprite.</summary>
    private static SWTrainingVisual UsherBench(SWTrainingArt item) => new(SWTrainingScene.Bench, new[]
    {
        new SWTrainingOverlay(SWTrainingArt.GearBeltSecurity, 0, 0),
        new SWTrainingOverlay(item, SWSenateTrainingArt.BeltItemX, SWSenateTrainingArt.BeltItemY, Backed: true),
    });

    private const string GuardKitNote = "Штурмовой пояс гвардии выдают пустым: в него убирают своё снаряжение. Если что-то выдано позже, смотрите инвентарь.";

    /// <summary>The empty chamber, the usher asks the speaker, the speaker answers, and the session opens.</summary>
    public static SWTrainingStage[] UsherHall() => Steps(
        Cut(SWTrainingScene.HallEmpty, 3f, note: "До заседания полчаса, зал пуст."),
        Scene(3f, "Пристав спрашивает спикера, что подготовить.", At(SWTrainingArt.FigUsher, true, 1), At(SWTrainingArt.FigSpeaker, false)),
        Scene(3f, "Спикер просит закрыть галерею: обсуждают карантин.", At(SWTrainingArt.FigUsher, true), At(SWTrainingArt.FigSpeaker, false, 1)),
        Cut(SWTrainingScene.HallSpeaker, ResultHold, note: "Галерея закрыта, заседание открыто вовремя."));

    /// <summary>The usher calls, the officer answers on the headset elsewhere, then arrives across a cut.</summary>
    public static SWTrainingStage[] UsherGuard() => Steps(
        Scene(3f, "Пристав зовёт офицера по гарнитуре.", At(SWTrainingArt.FigUsher, false, 1)),
        Cut(SWTrainingScene.Hall),
        Scene(3f, "Офицер слышит вызов и отвечает по гарнитуре.", At(SWTrainingArt.FigGuardOfficerRadio, true, 1)),
        Cut(SWTrainingScene.Hall),
        Scene(3f, "Офицер подошёл.", At(SWTrainingArt.FigGuardOfficerRadio, true), At(SWTrainingArt.FigUsher, false)),
        Scene(ResultHold, "Пристав просит офицера встать у галереи. Офицер остаётся рядом.", At(SWTrainingArt.FigGuardOfficerRadio, true), At(SWTrainingArt.FigUsher, false, 1)));

    /// <summary>The usher meets a judge, shows the way, and the hearing room is ready.</summary>
    public static SWTrainingStage[] UsherService() => Steps(
        Scene(3f, "Судья Кир Дан пришёл ко входу перед слушанием.", At(SWTrainingArt.FigJudge, true), At(SWTrainingArt.FigUsherGuide, false)),
        Scene(3f, "Пристав показывает дорогу в зал суда.", At(SWTrainingArt.FigJudge, true), At(SWTrainingArt.FigUsherGuide, false, 1)),
        Cut(SWTrainingScene.Court, ResultHold, SWTrainingFocus.Judges, "Судья на месте, слушание начнётся вовремя. Пристав помогает работе, а не голосует."));

    public static SWTrainingStage[] GuardHeadStart() => Watch(SWTrainingArt.FigGuardHead, "Глава гвардии Тарен Вокс перед сменой проверяет КПК.", AccessNote);
    public static SWTrainingStage[] GuardHeadKit() => Kit(SWTrainingArt.FigGuardHead, SWTrainingArt.GearGuardHead, GuardKitNote);

    /// <summary>The head names a place, the officer confirms, and stays there after the head has gone.</summary>
    public static SWTrainingStage[] GuardHeadPosts() => Steps(
        Scene(3f, "Глава указывает офицеру место у входа.", At(SWTrainingArt.FigGuardHeadPoint, true, 1), At(SWTrainingArt.FigGuardOfficer, false)),
        Scene(3f, "Офицер подтверждает.", At(SWTrainingArt.FigGuardHeadPoint, true), At(SWTrainingArt.FigGuardOfficer, false, 1)),
        Cut(SWTrainingScene.Hall),
        Scene(ResultHold, "Глава ушёл, офицер остался у входа, где договорились.", At(SWTrainingArt.FigGuardOfficer, false)));

    /// <summary>Someone comes up, the head asks before acting, and the visitor goes on; force is not the first answer.</summary>
    public static SWTrainingStage[] GuardHeadWatch() => Steps(
        Scene(3f, "Глава у входа.", At(SWTrainingArt.FigGuardHeadPoint, true)),
        Cut(SWTrainingScene.Hall),
        Scene(3f, "Подходит посетитель.", At(SWTrainingArt.FigGuardHeadPoint, true), At(SWTrainingArt.FigCorrespondent, false)),
        Scene(3f, "Глава останавливает его жестом и спрашивает.", At(SWTrainingArt.FigGuardHeadPoint, true, 1), At(SWTrainingArt.FigCorrespondent, false)),
        Scene(ResultHold, "Это корреспондент с пропуском прессы. Угрозы нет, она проходит.", At(SWTrainingArt.FigGuardHeadPoint, true), At(SWTrainingArt.FigCorrespondent, false, 1)),
        new Step(ResultHold, AtEntrance(At(SWTrainingArt.FigGuardHeadPoint, true, 1), At(SWTrainingArt.FigGuardOfficer, false)),
            Notes: ScreenNote("Если угроза есть, глава зовёт офицеров, а не действует один."), Alternative: true));

    /// <summary>The head and the usher agree, and the senators pass into the chamber.</summary>
    public static SWTrainingStage[] GuardHeadHall() => Steps(
        Back("Угрозы не было, корреспондент прошла. Глава согласует с приставом проход сенаторов.",
            Scene(3f, null, At(SWTrainingArt.FigGuardHead, true, 1), At(SWTrainingArt.FigUsherGuide, false))),
        Scene(3f, "Пристав показывает, где пройдут сенаторы.", At(SWTrainingArt.FigGuardHead, true), At(SWTrainingArt.FigUsherGuide, false, 1)),
        Cut(SWTrainingScene.Hall),
        Picture(AtEntrance(new SWTrainingOverlay(SWTrainingArt.FigSenators, SWSenateTrainingArt.SlotAX, SWSenateTrainingArt.SlotY, 0, true)), ResultHold,
            ScreenNote("Сенаторы проходят в зал спокойно, заседание продолжается.")));

    public static SWTrainingStage[] OfficerStart() => Watch(SWTrainingArt.FigGuardOfficer, "Офицер Сая Брин заступает в утреннюю смену.", AccessNote);
    public static SWTrainingStage[] OfficerKit() => Kit(SWTrainingArt.FigGuardOfficer, SWTrainingArt.GearGuardOfficer, GuardKitNote);

    /// <summary>The officer asks where to stand, the head points to the place, and the officer is there.</summary>
    public static SWTrainingStage[] OfficerPost() => Steps(
        Scene(3f, "Офицер спрашивает главу, где встать.", At(SWTrainingArt.FigGuardHeadPoint, true), At(SWTrainingArt.FigGuardOfficer, false, 1)),
        Scene(3f, "Глава указывает место у входа.", At(SWTrainingArt.FigGuardHeadPoint, true, 1), At(SWTrainingArt.FigGuardOfficer, false)),
        Cut(SWTrainingScene.Hall),
        Scene(ResultHold, "Офицер у входа. Сообщите главе, что вы на месте.", At(SWTrainingArt.FigGuardOfficer, false)));

    /// <summary>The officer notices, speaks into the headset, the head asks the usher to check the pass, and the visitor goes on.</summary>
    public static SWTrainingStage[] OfficerWarn() => Steps(
        Scene(3f, "Офицер замечает: посетитель идёт к галерее, пропуска на груди не видно.", At(SWTrainingArt.FigGuardOfficerRadio, true), At(SWTrainingArt.FigCorrespondent, false)),
        Scene(3f, "Офицер сообщает по гарнитуре.", At(SWTrainingArt.FigGuardOfficerRadio, true, 1), At(SWTrainingArt.FigCorrespondent, false)),
        Cut(SWTrainingScene.Hall),
        Scene(3f, "Сообщение слышит глава гвардии.", At(SWTrainingArt.FigGuardHead, true), At(SWTrainingArt.FigUsher, false)),
        Scene(3f, "Глава просит пристава проверить пропуск у галереи.", At(SWTrainingArt.FigGuardHead, true, 1), At(SWTrainingArt.FigUsher, false)),
        Cut(SWTrainingScene.Hall),
        Scene(3f, "Пристав спрашивает пропуск.", At(SWTrainingArt.FigUsher, true, 1), At(SWTrainingArt.FigCorrespondent, false)),
        Scene(ResultHold, "Это корреспондент с пропуском прессы. Угрозы нет, она проходит дальше.", At(SWTrainingArt.FigUsher, true), At(SWTrainingArt.FigCorrespondent, false, 1)));
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
        Cut(SWTrainingScene.HallSpeaker, ResultHold, note: "Вход свободен, заседание идёт. Обошлось без силы. Решения принимает Сенат, а не охрана."),
        new Step(ResultHold, AtEntrance(At(SWTrainingArt.FigGuardOfficer, true, 1), At(SWTrainingArt.FigCorrespondent, false, 1)),
            Notes: ScreenNote("Если посетитель не уходит, офицер снова зовёт главу, и дальше они решают вместе."), Alternative: true));
}
