using System;
using System.Collections.Generic;

namespace Content.Client._SW._Senate.Training;

/// <summary>
/// Timelines of chapters 01Б, 20Б and 24А: the staff radio channels as the headsets carry them, the constitutional review
/// of the Supreme Court, and the correction and withdrawal of a published article. Labels, messages and limits are those
/// of the radio prototypes, the judicial console and the press window with their server systems.
/// </summary>
public static partial class SWTrainingSamples
{
    // ---- 01Б: staff radio --------------------------------------------------

    /// <summary>The headset's examine text and a remark beside it.</summary>
    private static SWTrainingAnnotation[] HeadsetExamine(string channels, string remark) => new[]
    {
        new SWTrainingAnnotation($"Доступные частоты: {channels}", SWTrainingTone.Game, 330, 176, 270, "Гарнитура"),
        new SWTrainingAnnotation(remark, SWTrainingTone.Teaching, 580, 24, 130),
    };

    private const string GuardChannel = ":в для канала Гвардия (205.2)";
    private const string StaffChannel = ":е для канала Персонал Сената (205.1)";
    private const string SenateChannel = ":ш для канала Сенат (205.0)";
    private const string ChanceryChannel = ":а для канала Администрация Канцлера (205.3)";

    public static SWTrainingStage[] RadioKeys() => Steps(
        Picture(AtEntrance(At(SWTrainingArt.FigGuardOfficer, true)), 3f, ScreenNote("Сая Брин осматривает свою гарнитуру офицера гвардии.")),
        Picture(AtEntrance(At(SWTrainingArt.FigGuardOfficer, true)), ResultHold,
            HeadsetExamine($"{GuardChannel}; {StaffChannel}", "У офицера два канала: гвардия и персонал Сената.")),
        new Step(ResultHold, AtEntrance(At(SWTrainingArt.FigGuardHead, true)),
            Notes: HeadsetExamine($"{GuardChannel}; {SenateChannel}; {ChanceryChannel}",
                "У главы гвардии другой набор: гвардия, Сенат и администрация канцлера. Канала персонала у него нет."),
            Alternative: true));

    public static SWTrainingStage[] RadioOthers() => Steps(
        Picture(AtEntrance(At(SWTrainingArt.FigUsher, true)), ResultHold,
            HeadsetExamine($"{GuardChannel}; {SenateChannel}; {StaffChannel}; {ChanceryChannel}",
                "Гарнитуры офицера и главы осмотрены. Гарнитура пристава шире всех: все четыре служебных канала.")) with { Resumes = true },
        new Step(ResultHold, new SWTrainingVisual(SWTrainingScene.Clinic, Array.Empty<SWTrainingOverlay>()),
            Notes: HeadsetExamine($"{SenateChannel}; {StaffChannel}", "У сенатского врача Сенат и персонал. Канала гвардии у врача нет."),
            Alternative: true));

    public static SWTrainingStage[] RadioGuard() => Steps(
        Back("У входа офицер Сая Брин. Она видит человека без пропуска и пишет в рацию: «:в У входа посетитель без пропуска».",
            Scene(3f, null, At(SWTrainingArt.FigGuardOfficer, true), At(SWTrainingArt.FigCorrespondent, false))),
        Cut(SWTrainingScene.Hall),
        Picture(AtEntrance(At(SWTrainingArt.FigGuardHead, true)), ResultHold,
            Heard("[Гвардия] Сая Брин говорит, \"У входа посетитель без пропуска.\"", null,
                "Тарен Вокс слышит это по «Гвардии». Пристав тоже: ключ гвардии есть и у него.")));

    public static SWTrainingStage[] RadioStaff() => Steps(
        Picture(new SWTrainingVisual(SWTrainingScene.Clinic, Array.Empty<SWTrainingOverlay>()), 3f,
            ScreenNote("В медпункте буянит пациент. Мира Тессе зовёт помощь: «:е Нужен офицер в медпункт».")),
        Picture(AtEntrance(At(SWTrainingArt.FigGuardOfficer, true)), ResultHold,
            Heard("[Персонал Сената] Мира Тессе говорит, \"Нужен офицер в медпункт.\"", null,
                "Офицер и пристав это слышат, глава гвардии нет: канала персонала у него нет.")),
        Scene(ResultHold, "Офицер уходит в медпункт и коротко сообщает главе по «Гвардии», где он.",
            At(SWTrainingArt.FigGuardOfficer, true, 1)));

    public static SWTrainingStage[] RadioSenate() => Steps(
        Picture(AtEntrance(At(SWTrainingArt.FigUsher, true)), 3f, ScreenNote("Ден Маро объявляет сенаторам: «:ш Заседание через десять минут, галерея закрыта».")),
        Cut(SWTrainingScene.HallEmpty),
        Picture(new SWTrainingVisual(SWTrainingScene.HallEmpty, Array.Empty<SWTrainingOverlay>()), ResultHold,
            Heard("[Сенат] Ден Маро говорит, \"Заседание через десять минут, галерея закрыта.\"", null,
                "Канал «Сенат» слышат сенаторы, руководители Сената, глава гвардии и врач. Офицер гвардии его не слышит.")));

    // ---- 20Б: constitutional review ----------------------------------------

    private static readonly string[] ReviewLaws = { BillName, AidBill };
    private static readonly string[] ReviewVotes = { "Признать неконституционным", "Подтвердить конституционность" };

    private static SWTrainingFormPanel LawsPicker(string[] laws, bool aimLaw = false, bool openLaw = false, int current = 0, bool aimStart = false) => Form("Законы",
        Sec("Конституционный надзор"),
        Choose(string.Empty, laws, current, aimLaw, openLaw),
        Btn("Открыть конституционную проверку", aimStart, accent: SWTrainingAccent.Bad));

    private static SWTrainingFormPanel ReviewCard(int yes, int no, string? mine, bool canVote = true, int aim = -1)
    {
        var rows = new List<SWTrainingRow>
        {
            Sec("Конституционный надзор"),
            Txt($"Конституционная проверка: «{AidBill}»"),
            Dim($"За признание неконституционным: {yes}   |   против: {no}" + (mine == null ? string.Empty : $"   |   ваш голос: {mine}")),
        };
        if (canVote)
            rows.Add(Choice(ReviewVotes, -1, aim));
        return new SWTrainingFormPanel("Законы", rows.ToArray());
    }

    private static readonly string[] OneLaw = { BillName };

    public static SWTrainingStage[] ReviewOpen() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(CourtTitle, CourtTabs, 2), time: "10:00"),
        Press(TabRow(CourtTitle, CourtTabs, 2), time: "10:00"),
        Result(LawsPicker(ReviewLaws),
            note: "Сектор жалуется, что закон об экстренной помощи забирает медиков из клиник силой. Проверить его может только Верховный Судья.", time: "10:00"),
        Result(Form("Законы", Sec("Конституционный надзор"), Dim("Конституционную проверку может открыть только Верховный Судья.")),
            note: "Республиканский судья видит здесь только эту строку, а голосует в уже открытой проверке.", alternative: true, time: "10:00"));

    public static SWTrainingStage[] ReviewStart() => Steps(
        Back("За консолью Верховный Судья Арен Солт: открыть проверку может он.", LawsPicker(ReviewLaws), "10:02"),
        Aim(LawsPicker(ReviewLaws, aimLaw: true), time: "10:02"),
        Press(LawsPicker(ReviewLaws, aimLaw: true, openLaw: true), time: "10:02"),
        Wait(LawsPicker(ReviewLaws, current: 1), "Арен Солт выбирает закон об экстренной помощи.", 3f, "10:02"),
        Aim(LawsPicker(ReviewLaws, current: 1, aimStart: true), time: "10:02"),
        Press(LawsPicker(ReviewLaws, current: 1, aimStart: true), time: "10:02"),
        new Step(ResultHold, Closeup("10:02"), ReviewCard(0, 0, null), Popup: "Проверка закона открыта.",
            Notes: Heard($"Верховный Суд начал проверку закона «{AidBill}» на конституционность.", "Верховный Суд Республики",
                "Судьи голосуют до двух активных игровых часов. Закон пока действует.")),
        Result(ReviewCard(0, 0, null), "Проверку открыть нельзя.",
            "Вторую проверку, пока идёт первая, не открыть. Закрыть проверку кнопкой тоже нельзя: её решают голоса или срок.", true, "10:02"));

    public static SWTrainingStage[] ReviewVoting() => Steps(
        Back("Других проверок не идёт: эта открыта, голосов пока нет.", ReviewCard(0, 0, null), "10:10"),
        Aim(ReviewCard(0, 0, null, aim: 0), time: "10:10"),
        Press(ReviewCard(0, 0, null, aim: 0), time: "10:10"),
        Result(ReviewCard(1, 0, "отменить"),
            note: "Арен Солт голосует за неконституционность. Пока срок идёт, голос можно изменить.", time: "10:10"),
        HandOut(),
        HandIn(),
        Wait(ReviewCard(1, 0, null), "За своей консолью Кир Дан видит ту же проверку и голос Верховного Судьи.", 4f, "10:40"),
        Aim(ReviewCard(1, 0, null, aim: 1), time: "10:40"),
        Press(ReviewCard(1, 0, null, aim: 1), time: "10:40"),
        Result(Form("Законы", Sec("Конституционный надзор"), Dim("Конституционную проверку может открыть только Верховный Судья.")),
            note: "Кир Дан подтвердил конституционность. Это последний из двух голосов, и проверка решилась сразу: при счёте 1:1 решает голос Верховного Судьи.", time: "10:40"));

    public static SWTrainingStage[] ReviewResult() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(CourtTitle, CourtTabs, 2), time: "10:45"),
        Press(TabRow(CourtTitle, CourtTabs, 2), time: "10:45"),
        Result(LawsPicker(OneLaw),
            note: "Арен Солт открывает «Законы» у себя: закона об экстренной помощи в списке больше нет. При счёте 1:1 решил его голос за отмену, закон отменён, и его действие снято.", time: "10:45"),
        Result(LawsPicker(ReviewLaws),
            note: "Если бы Арен Солт тоже подтвердил конституционность, счёт был бы 0:2, и закон остался бы и в списке, и в силе.", alternative: true, time: "10:45"));

    /// <summary>The whole shot is the case of a bench of three where the chief does not vote; it opens with that condition.</summary>
    public static SWTrainingStage[] ReviewDeadline() => Steps(
        new Step(4f, Closeup("11:30"), ReviewCard(1, 1, null),
            Notes: ScreenNote("Если бы в суде было трое, Арен Солт и два судьи: Кир Дан голосует за отмену, Ия Сарн против, а Верховный Судья голоса не подаёт."), Alternative: true),
        new Step(3f, Closeup("12:00"), ReviewCard(1, 1, null),
            Notes: ScreenNote("Пока проголосовали не все, проверка ждёт срока: двух активных часов с 10:02."), Alternative: true, Continues: true),
        Result(LawsPicker(ReviewLaws),
            note: "В 12:02 срок вышел бы при равенстве 1:1 и без голоса Верховного Судьи, и закон остался бы. Отменяет закон только решение о неконституционности.",
            alternative: true, time: "12:02", continues: true));

    // ---- 24А: correction and withdrawal ----------------------------------

    private const string FixedBody = "Нижние уровни Корусанта закрыты. Верховный суд признал прежний карантин чрезмерным и обязал выплатить сектору компенсацию. Источник в канцелярии сектора обещает первые выплаты уже завтра.";
    private const string StaleContext = "Материал изменился. Выберите его заново перед дальнейшим редактированием.";
    private const string RivalTitle = "Совет кланов требует открыть доки";

    /// <summary>
    /// The press window on a publication: the context line, the two text fields, then «История», «Исправить» and «Отозвать».
    /// The last two need one's own current publication; after a withdrawal all three are off and the context says so.
    /// </summary>
    private static SWTrainingFormPanel PubPanel(string title, string body, string context, bool canEdit, int aim = -1, bool aimBody = false, bool withdrawn = false) => Form("Голонет",
        Choice(PressTabs, 0),
        withdrawn || !canEdit && context.StartsWith("Материал") ? Warn(context) : Dim(context),
        Fld("Заголовок", title),
        Fld("Текст", body, aimBody),
        Btn("История", aim == 0, withdrawn, SWTrainingAccent.Neutral),
        Btn("Исправить", aim == 1, !canEdit || withdrawn),
        Btn("Отозвать", aim == 2, !canEdit || withdrawn, SWTrainingAccent.Bad));

    private static string PubMeta(int version) => $"{PressEditions[0]}, {CorrespondentName}, версия {version}";

    public static SWTrainingStage[] PubOpen() => Steps(
        Aim(TabRow("Голонет", PressTabs, 0), time: "14:00"),
        Press(TabRow("Голонет", PressTabs, 0), time: "14:00"),
        Aim(Form("Голонет", Choice(PressTabs, 0), Btn(ArticleTitle, true), Dim(PubMeta(1))), time: "14:00"),
        Press(Form("Голонет", Choice(PressTabs, 0), Btn(ArticleTitle, true), Dim(PubMeta(1))), time: "14:00"),
        Result(PubPanel(ArticleTitle, ArticleBody, PubMeta(1), true),
            note: "Ника Рэй открывает свою утреннюю статью. С тех пор суд вынес решение, и фраза «суд рассмотрит иск» устарела.", time: "14:00"));

    public static SWTrainingStage[] PubCorrect() => Steps(
        Aim(PubPanel(ArticleTitle, ArticleBody, PubMeta(1), true, aimBody: true), time: "14:02"),
        Press(PubPanel(ArticleTitle, ArticleBody, PubMeta(1), true, aimBody: true), time: "14:02"),
        Wait(PubPanel(ArticleTitle, FixedBody, PubMeta(1), true), "Она пишет о решении суда и добавляет слова источника о сроке выплат.", 4f, "14:04"),
        Aim(PubPanel(ArticleTitle, FixedBody, PubMeta(1), true, 1), time: "14:05"),
        Press(PubPanel(ArticleTitle, FixedBody, PubMeta(1), true, 1), time: "14:05"),
        Result(PubPanel(ArticleTitle, FixedBody, StaleContext, false),
            note: "Версия 2 ушла в ленту. Окно просит выбрать статью заново, и до этого исправить или отозвать её нельзя; история открыта.", time: "14:05"));

    public static SWTrainingStage[] PubHistory() => Steps(
        Aim(PubPanel(ArticleTitle, FixedBody, StaleContext, false, 0), time: "14:06"),
        Press(PubPanel(ArticleTitle, FixedBody, StaleContext, false, 0), time: "14:06"),
        Result(Form("История публикации", Txt("Версия 1"), Dim(ArticleBody), Txt("Версия 2"), Dim(FixedBody)),
            note: "В истории обе версии по порядку: видно, что было в утренней статье и что в ней поменялось.", time: "14:06"));

    public static SWTrainingStage[] PubLimits() => Steps(
        Result(PubPanel(RivalTitle, "Кланы грозят забастовкой в доках Корусанта.", $"{PressEditions[1]}, Тор Валек, версия 1", false),
            note: "Чужую статью из «Голоса миров» можно прочитать и посмотреть её историю, но исправить или отозвать нельзя.", time: "14:10"),
        Result(PubPanel(ArticleTitle, ArticleBody, StaleContext, false),
            note: "Та же надпись появится, если статья была открыта у вас со старым текстом, а новая версия вышла с другого терминала. Старую правку отправить нельзя.",
            alternative: true, time: "14:10"));

    public static SWTrainingStage[] PubWithdraw() => Steps(
        Back("У Ники Рэй по-прежнему своя статья в версии 2, окно просит выбрать её заново. Вечером канцелярия сектора сообщает: срок выплат ещё не назначен, а источник отказывается от своих слов.",
            PubPanel(ArticleTitle, FixedBody, StaleContext, false), "18:00"),
        Aim(Form("Голонет", Choice(PressTabs, 0), Btn(ArticleTitle, true), Dim(PubMeta(2))), time: "18:00"),
        Press(Form("Голонет", Choice(PressTabs, 0), Btn(ArticleTitle, true), Dim(PubMeta(2))), time: "18:00"),
        Wait(PubPanel(ArticleTitle, FixedBody, PubMeta(2), true),
            "Решение суда в силе, но статья держится на ложной дате. Ника Рэй выбирает её заново и решает снять, чтобы написать новую.", 4f, "18:01"),
        Aim(PubPanel(ArticleTitle, FixedBody, PubMeta(2), true, 2), time: "18:01"),
        Press(PubPanel(ArticleTitle, FixedBody, PubMeta(2), true, 2), time: "18:01"),
        Result(PubPanel(ArticleTitle, string.Empty, "Публикация отозвана.", false, withdrawn: true),
            note: "В ленте статья помечена «[отозвано]». Это окончательно: ни исправить, ни посмотреть историю больше нельзя.", time: "18:01"));
}
