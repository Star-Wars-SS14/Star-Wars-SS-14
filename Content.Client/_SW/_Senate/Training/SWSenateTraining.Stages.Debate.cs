using System;
using System.Collections.Generic;

namespace Content.Client._SW._Senate.Training;

/// <summary>
/// Timelines of chapters 03Б, 03В, 03Г and 04А: the vote console, the senator's microphone and the floor queue, parties,
/// the election of a chancellor and the tie. Labels are those of the vote console, the «Партии» window and the office
/// section of the work console. Four sectors are claimed: Альдераан (Сен Арро), Кореллия (Вейл Оран), Корусант
/// (Тала Вен) and Тайтон (Дара Квел); the vote console lists them in that order.
/// </summary>
public static partial class SWTrainingSamples
{
    private const string VoteTitle = "Голосование сената";
    private const string MorningSession = "Заседание 10:00–12:00";
    private const string AfternoonSession = "Заседание 15:00–17:00";
    private const string ElectionSession = "Заседание 11:00–13:00";
    private const string PartyName = "Лига здоровых миров";
    private const string PartyText = "Клиники и лекарства для каждого сектора.";
    private const string ElectionName = "Выборы Верховного Канцлера";
    private const string OpenVote = "Голосование открыто";
    private const string TieState = "Ничья, решающий голос Канцлера";

    // One array per set of buttons: rows compare them by reference.
    private static readonly string[] VoteTabs = { "Повестка", "Голосование" };
    private static readonly string[] LeaderVoteTabs = { "Повестка", "Голосование", "Трибуна" };
    private static readonly string[] VoteButtons = { "За", "Против", "Воздержаться" };
    private static readonly string[] TiebreakButtons = { "РЕШАЮЩИЙ: ЗА", "РЕШАЮЩИЙ: ПРОТИВ" };
    private static readonly string[] QueueButtons = { "Дать слово", "Убрать" };
    private static readonly string[] PartyModes = { "Все партии", "Моя партия", "Партийные законы" };
    private static readonly string[] PoliticsApps = { "Партии", "Амбиции" };
    private static readonly string[] JoinAnswers = { "Принять", "Отклонить" };
    private static readonly string[] PartyBallot = { "За", "Против" };
    private static readonly string[] PartyActions = { "Выйти из партии", "Распустить партию" };

    private static SWTrainingVisual Room(SWTrainingScene scene) => new(scene, Array.Empty<SWTrainingOverlay>());

    /// <summary>A line the game shows to everyone, with a remark beside it.</summary>
    private static SWTrainingAnnotation[] Heard(string text, string? heading, string remark) => new[]
    {
        new SWTrainingAnnotation(text, SWTrainingTone.Game, 330, 176, 270, heading),
        new SWTrainingAnnotation(remark, SWTrainingTone.Teaching, 580, 24, 130),
    };

    // ---- vote console -----------------------------------------------------

    /// <summary>
    /// The «Голосование» tab with a bill on the floor. As the vote console draws them, the three vote buttons are always
    /// there; they are off for a card without a claimed sector and during a tie.
    /// </summary>
    private static SWTrainingFormPanel Ballot(string mine, int yes, int no, int abstain, int aim = -1, bool tabs3 = false,
        bool canVote = true, bool tie = false, string session = MorningSession, string bill = BillName, string author = ExampleName,
        int number = 12, string effects = "Здравоохранение +2", SWTrainingRow[]? extra = null)
    {
        var rows = new List<SWTrainingRow>
        {
            Choice(tabs3 ? LeaderVoteTabs : VoteTabs, 1),
            Dim(session),
            Txt(bill),
            Dim(effects.Length > 0 ? $"Документ № {number}   Автор {author}   {effects}" : $"Документ № {number}   Автор {author}"),
            Dim($"Состояние: {(tie ? TieState : OpenVote)}   Ваш голос: {mine}"),
            Txt($"ЗА {yes}   ПРОТИВ {no}   ВОЗД. {abstain}"),
        };
        rows.Add(canVote && !tie
            ? Choice(VoteButtons, -1, aim)
            : new SWTrainingRow(SWTrainingRowKind.Buttons, Buttons: VoteButtons, Disabled: true));
        if (extra != null)
            rows.AddRange(extra);
        return new SWTrainingFormPanel(VoteTitle, rows.ToArray());
    }

    private static SWTrainingFormPanel NoBallot(string? session = null) => Form(VoteTitle,
        Choice(VoteTabs, 1),
        Dim(session ?? "Нет активного заседания."),
        Dim("Нет активного голосования."));

    /// <summary>«Голоса секторов» under the ballot; members of a formed party carry its tag.</summary>
    private static SWTrainingFormPanel SectorVotes(bool party, string alderaan, string corellia, string coruscant, string tython)
    {
        string Tag(string vote) => party ? $"{PartyName}   {vote}" : vote;
        return Form(VoteTitle,
            Choice(VoteTabs, 1),
            Sec("Голоса секторов"),
            Item("Альдераан", Tag(alderaan)),
            Item("Кореллия", corellia),
            Item("Корусант", Tag(coruscant)),
            Item("Тайтон", Tag(tython)));
    }

    // ---- 03Б --------------------------------------------------------------

    public static SWTrainingStage[] VoteOpen() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow(VoteTitle, VoteTabs, 1), time: "10:05"),
        Press(TabRow(VoteTitle, VoteTabs, 1), time: "10:05"),
        Result(Ballot("Не подан", 0, 1, 0), note: "Идёт заседание по закону о клиниках. Тайтон уже голосует против.", time: "10:05"),
        Result(NoBallot(), note: "Вне заседания голосовать не по чему: кнопок нет.", alternative: true, time: "09:40"));

    public static SWTrainingStage[] VoteCast() => Steps(
        Back("Заседание по закону о клиниках идёт, за картой Талы Вен закреплён Корусант.", Ballot("Не подан", 0, 1, 0), "10:10"),
        Aim(Ballot("Не подан", 0, 1, 0, 0), time: "10:10"),
        Press(Ballot("Не подан", 0, 1, 0, 0), time: "10:10"),
        Result(Ballot("За", 1, 1, 0), "Ваш голос учтён.", "Голос Корусанта учтён. Пока это один против одного.", time: "10:10"),
        Result(Ballot("Не подан", 1, 1, 0, canVote: false),
            note: "С картой без закреплённого сектора все три кнопки отключены.", alternative: true, time: "10:10"));

    public static SWTrainingStage[] FloorRequest() => Steps(
        Back("Голос Корусанта учтён. Тала Вен хочет объяснить, зачем Дунидину клиники, и у своего места нажимает на микрофон.",
            Picture(Room(SWTrainingScene.Hall), 3f)),
        Picture(Room(SWTrainingScene.Hall), 4f, Heard("Вы в очереди на слово. Позиция: 1.", null, "Микрофон ставит в очередь только сенатора, за которым закреплён сектор его карты.")),
        Picture(Room(SWTrainingScene.HallSpeaker), 4f, Heard("Слово предоставлено: Тала Вен.", null, "Спикер дал слово со своей консоли. Это сообщение видят все.")),
        Picture(Room(SWTrainingScene.Hall), ResultHold, Heard("ТРИБУНА - Тала Вен: В Дунидине одна клиника на целый уровень. Закон даст ей оборудование.", null,
            "Пока слово у Талы Вен, её речь слышит весь Сенат.")),
        new Step(ResultHold, Room(SWTrainingScene.Hall), Notes: Heard("Вы уже в очереди или у вас есть слово.", null, "Второй раз в очередь не встать."), Alternative: true));

    public static SWTrainingStage[] VoteChange() => Steps(
        Back("Тала Вен выступила с трибуны. Теперь к консоли подходит Дара Квел из Тайтона.", HandOut()),
        HandIn(),
        Wait(Ballot("Против", 1, 1, 0), "Дара Квел, выслушав речь, меняет решение.", 4f, "10:40"),
        Aim(Ballot("Против", 1, 1, 0, 0), time: "10:40"),
        Press(Ballot("Против", 1, 1, 0, 0), time: "10:40"),
        Result(Ballot("За", 2, 0, 0), "Ваш голос учтён.", "Голос Тайтона заменён. Пока голосование открыто, голос можно менять.", time: "10:40"),
        Result(Ballot("За", 2, 2, 0, tie: true), note: "Во время ничьей голос уже не изменить: решает канцлер.", alternative: true, time: "11:30"));

    public static SWTrainingStage[] VoteResult() => Steps(
        Back("Ничьей нет: Тайтон теперь за. К полудню проголосовали все четыре сектора.", SectorVotes(false, "За", "Против", "За", "За"), "11:55"),
        Result(Agenda("Прошёл голосование - ждёт подписи Канцлера"),
            note: "В 12:00 заседание закончилось. ЗА больше, чем ПРОТИВ, но закон начнёт действовать только после подписи канцлера.", time: "12:00"),
        new Step(ResultHold, Closeup("12:00"), Agenda("Отклонён"),
            Notes: Heard($"Законопроект «{BillName}» отклонён Сенатом.", "Сенат Республики", "Если ПРОТИВ больше или голоса к концу заседания равны, проект отклонён."),
            Alternative: true));

    // ---- 03В --------------------------------------------------------------

    private static SWTrainingFormPanel CreateBox(bool filled, bool aimName = false, bool aimCreate = false) => Form("Партии",
        Choice(PartyModes, 1),
        Sec("Создать партию"),
        Fld("Название партии", filled ? PartyName : "Название партии...", aimName),
        Fld("Описание", filled ? PartyText : "Описание..."),
        Btn("Создать партию", aimCreate));

    private static SWTrainingFormPanel Founded() => Form("Партии",
        Choice(PartyModes, 1),
        Txt(PartyName),
        Warn("На формировании (нужно ещё 2)  (1)"),
        Dim($"Лидер: {ExampleName}"),
        Dim(PartyText),
        Sec("Члены партии:"),
        Txt($"{ExampleName} - Корусант (лидер)"),
        Choice(PartyActions));

    /// <summary>The card in «Все партии» as another senator sees it, closed or opened with «Подробнее».</summary>
    private static SWTrainingFormPanel PartyListed(bool open, bool aimToggle = false, bool aimJoin = false, bool pending = false)
    {
        if (!open)
            return Form("Партии",
                Choice(PartyModes, 0),
                Txt(PartyName),
                Btn("Подробнее", aimToggle, accent: SWTrainingAccent.Neutral),
                Warn("На формировании (нужно ещё 2)  (1)"),
                Dim($"Лидер: {ExampleName}"));

        return Form("Партии",
            Choice(PartyModes, 0),
            Txt(PartyName),
            Warn("На формировании (нужно ещё 2)  (1)"),
            Dim($"Лидер: {ExampleName}"),
            Sec("Члены партии:"),
            Txt($"{ExampleName} - Корусант (лидер)"),
            pending ? Warn("Заявка подана, ожидает лидера.") : Btn("Подать заявку на вступление", aimJoin),
            pending ? Btn("Отозвать", accent: SWTrainingAccent.Neutral) : Dim(string.Empty));
    }

    private static SWTrainingFormPanel PartyRequests(int aim = -1) => Form("Партии",
        Choice(PartyModes, 1),
        Txt(PartyName),
        Warn("На формировании (нужно ещё 2)  (1)"),
        Dim($"Лидер: {ExampleName}"),
        Sec("Заявки на вступление:"),
        Txt($"{SecondMember} - Тайтон"),
        Choice(JoinAnswers, -1, aim));

    private static SWTrainingFormPanel PartyMembers(string status, params string[] members)
    {
        var rows = new List<SWTrainingRow>
        {
            Choice(PartyModes, 1),
            Txt(PartyName),
            status.StartsWith("Сформирована") ? Fine(status) : Warn(status),
            Dim($"Лидер: {ExampleName}"),
            Sec("Члены партии:"),
        };
        foreach (var member in members)
            rows.Add(Txt(member));
        rows.Add(Choice(PartyActions));
        return new SWTrainingFormPanel("Партии", rows.ToArray());
    }

    private static SWTrainingFormPanel PartyCohesionCard(string cohesion) => Form("Партии",
        Choice(PartyModes, 1),
        Txt(PartyName),
        Fine("Сформирована  (3)"),
        Dim($"Лидер: {ExampleName}"),
        Txt($"Сплочённость: {cohesion}"),
        Choice(PartyActions));

    private static SWTrainingFormPanel PartyLeaderVote() => Form("Партии",
        Choice(PartyModes, 1),
        Txt(PartyName),
        Fine("Сформирована  (3)"),
        Dim($"Лидер: {ExampleName}"),
        Txt("Сплочённость: 0  [нейтрально]"),
        Warn($"Голосование за нового лидера: {ThirdMember}"),
        Dim("За: 1    Против: 0    Нужно: 2"),
        Choice(PartyBallot));

    public static SWTrainingStage[] PartyCreate() => Steps(
        HandOut(),
        HandIn(),
        Aim(Form("Рабочий стол", Btn("Политика", true)), time: "09:40"),
        Press(Form("Рабочий стол", Btn("Политика", true)), time: "09:40"),
        Aim(Form("Политика", Choice(PoliticsApps, -1, 0)), time: "09:40"),
        Press(Form("Политика", Choice(PoliticsApps, -1, 0)), time: "09:40"),
        Aim(CreateBox(false, aimName: true), time: "09:40"),
        Press(CreateBox(false, aimName: true), time: "09:40"),
        Wait(CreateBox(true), "Тала Вен вписывает название и цель партии.", 3f, "09:42"),
        Aim(CreateBox(true, aimCreate: true), time: "09:42"),
        Press(CreateBox(true, aimCreate: true), time: "09:42"),
        Result(Founded(), "Партия создана и набирает представителей.", "Создатель становится лидером. Сформированной партия станет при трёх членах.", time: "09:42"));

    public static SWTrainingStage[] PartyJoin() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow("Партии", PartyModes, 0), time: "09:50"),
        Press(TabRow("Партии", PartyModes, 0), time: "09:50"),
        Aim(PartyListed(false, aimToggle: true), time: "09:50"),
        Press(PartyListed(false, aimToggle: true), time: "09:50"),
        Aim(PartyListed(true, aimJoin: true), time: "09:50"),
        Press(PartyListed(true, aimJoin: true), time: "09:50"),
        Result(PartyListed(true, pending: true), "Заявка на вступление отправлена лидеру.",
            "Дара Квел ждёт решения Талы Вен. Пока лидер не ответил, заявку можно отозвать.", time: "09:50"));

    public static SWTrainingStage[] PartyAccept() => Steps(
        HandOut(),
        HandIn(),
        Aim(TabRow("Партии", PartyModes, 1), time: "09:55"),
        Press(TabRow("Партии", PartyModes, 1), time: "09:55"),
        Aim(PartyRequests(0), time: "09:55"),
        Press(PartyRequests(0), time: "09:55"),
        Result(PartyMembers("На формировании (нужно ещё 1)  (2)", $"{ExampleName} - Корусант (лидер)", $"{SecondMember} - Тайтон"),
            "Новый член принят в партию.", "Дара Квел в партии. До формирования не хватает одного.", time: "09:55"),
        Result(PartyMembers("Сформирована  (3)", $"{ExampleName} - Корусант (лидер)", $"{SecondMember} - Тайтон", $"{ThirdMember} - Альдераан"),
            note: "Принята третья заявка: партия сформирована, и её название видно в голосах секторов.", time: "10:20"),
        Result(Form("Партии", Choice(PartyModes, 1), Txt(PartyName), Fine("Сформирована (под угрозой)  (2)"), Dim($"Лидер: {ExampleName}"),
                Bad("Меньше 3 членов - найдите ещё одного примерно за 2.0 ч, иначе партия вернётся в статус формирования.")),
            note: "Если членов осталось меньше трёх, у партии около двух игровых часов найти замену.", alternative: true, time: "13:00"));

    public static SWTrainingStage[] PartyCohesion() => Steps(
        Back("В партии трое, она сформирована и начинает со сплочённости 0.", PartyCohesionCard("0  [нейтрально]"), "10:20"),
        Result(PartyCohesionCard("+1  [сплочённость]"),
            note: "Закон о клиниках решён, и члены голосовали так же, как лидер: сплочённость выросла на 1.", time: "12:00"),
        Result(PartyCohesionCard("+4  [монолит]"),
            note: "Ещё три закона, где большинство членов голосовало как лидер. Если бы большинство шло против, сплочённость падала бы.", time: "14:30"),
        Result(PartyLeaderVote(),
            note: "На -5 сплочённость обнуляется, и партия голосует за случайно выдвинутого члена в лидеры. Лидер сменится, только если за будет большинство.", alternative: true, time: "14:30"));

    private static SWTrainingRow[] Whip(bool aim = false) => new[]
    {
        Btn("ОБЪЯВИТЬ ПАРТИЙНЫМ", aim, accent: SWTrainingAccent.Neutral),
        Dim("Единый голос партии. Сплочённость: сброс до нуля."),
    };

    private static SWTrainingFormPanel AidBallot(string mine, int yes, int no, int aim = -1, SWTrainingRow[]? extra = null) =>
        Ballot(mine, yes, no, 0, aim, session: AfternoonSession, bill: AidBill, author: BillAuthor, number: 14, effects: "Здоровье +6   Общество +3", extra: extra);

    public static SWTrainingStage[] PartyWhip() => Steps(
        Back("Лидер Лиги по-прежнему Тала Вен, сплочённость +4.", HandOut()),
        HandIn(),
        Wait(AidBallot("Не подан", 1, 1), "Кнопка партийного голосования появится, когда лидер проголосует сам.", 4f, "15:10"),
        Aim(AidBallot("Не подан", 1, 1, 0), time: "15:10"),
        Press(AidBallot("Не подан", 1, 1, 0), time: "15:10"),
        Result(AidBallot("За", 2, 1, extra: Whip()), "Ваш голос учтён.", "Голос лидера подан, кнопка на месте.", time: "15:10"),
        Aim(AidBallot("За", 2, 1, extra: Whip(true)), time: "15:12"),
        Press(AidBallot("За", 2, 1, extra: Whip(true)), time: "15:12"),
        new Step(ResultHold, Closeup("15:12"), SectorVotes(true, "За", "За", "За", "За"), Popup: "Голосование объявлено партийным.",
            Notes: Heard($"Партия «{PartyName}» объявила законопроект «{AidBill}» партийным. Голоса всех её членов отданы единогласно: За.", "Сенат Республики",
                "Голос Тайтона заменён, Альдераан проголосовал так же, как лидер.")),
        Result(Form("Партии", Choice(PartyModes, 2), Txt(AidBill), Dim($"{PartyName}, голос: За, на голосовании")),
            note: "Закон попал в «Партийные законы». Сплочённость сброшена до 0, а этот закон в неё не засчитается.", time: "15:20"),
        Result(AidBallot("За", 2, 1), note: "При сплочённости ниже +4 или без своего голоса кнопки у лидера нет. На выборах её не бывает.",
            alternative: true, time: "15:10"));

    // ---- 03Г --------------------------------------------------------------

    /// <summary>The election ballot: the nomination button while it is open to the viewer, then the candidates with their buttons.</summary>
    private static SWTrainingFormPanel ElectionBoard(string mine, bool canNominate, bool aimNominate = false, params (string Name, int Votes, string Button, bool Aim)[] candidates)
    {
        var rows = new List<SWTrainingRow>
        {
            Choice(VoteTabs, 1),
            Dim(ElectionSession),
            Txt(ElectionName),
            Dim($"Документ № 15   Автор Сенат   Ваш голос: {mine}"),
        };
        if (canNominate)
            rows.Add(Btn("Выдвинуть свою кандидатуру", aimNominate));
        rows.Add(Sec("Кандидаты"));
        if (candidates.Length == 0)
            rows.Add(Dim("Кандидатов нет."));
        foreach (var (name, votes, button, aim) in candidates)
        {
            rows.Add(Txt($"{name}  -  {votes} голосов"));
            rows.Add(Btn(button, aim, button == "Вы голосовали", button == "Вы голосовали" ? SWTrainingAccent.Neutral : SWTrainingAccent.Good));
        }

        return new SWTrainingFormPanel(VoteTitle, rows.ToArray());
    }

    private static SWTrainingFormPanel ElectCard(bool aim = false) => Form("Должность",
        Txt("Сенатор сектора Корусант"),
        Txt("Вы избраны Верховным Канцлером Республики!"),
        Dim("При принятии полномочий текущий сектор освобождается."),
        Btn("Подтвердить избрание", aim, accent: SWTrainingAccent.Good));

    public static SWTrainingStage[] ElectionCall() => Steps(
        Picture(Room(SWTrainingScene.HallEmpty), 4f, ScreenNote("Канцлера нет, спикера тоже. Сектора заняты у четырёх сенаторов.")),
        Picture(Room(SWTrainingScene.HallEmpty), 4f, ScreenNote("Через игровой час такой вакансии Сенат сам назначает выборы: двухчасовое заседание в первый свободный час.")),
        HandOut(),
        HandIn(),
        Result(ElectionBoard("Не подан", true), note: "В 11:00 заседание открылось. Документ внёс Сенат, кандидатов пока нет.", time: "11:00"),
        Result(NoBallot(), note: "Если спикер на месте, выборы сами не начинаются. Если канцлера отстранили, они идут и при спикере.",
            alternative: true, time: "11:00"));

    public static SWTrainingStage[] ElectionNominate() => Steps(
        Back("Ни канцлера, ни спикера нет: выборы идут, кандидатов пока нет.", ElectionBoard("Не подан", true), "11:05"),
        Aim(ElectionBoard("Не подан", true, true), time: "11:05"),
        Press(ElectionBoard("Не подан", true, true), time: "11:05"),
        Result(ElectionBoard("Не подан", false, false, (ExampleName, 0, "Голосовать", false)), "Ваша кандидатура выдвинута.",
            "Тала Вен в списке. Кнопки выдвижения больше нет: дважды не выдвинуться.", time: "11:05"),
        Result(ElectionBoard("Не подан", false, false, (ExampleName, 0, "Голосовать", false), (BillAuthor, 0, "Голосовать", false)),
            note: "Вейл Оран тоже в списке кандидатов.", time: "11:10"),
        Result(ElectionBoard("Не подан", false, false, (ExampleName, 0, "Голосовать", false)),
            note: "Отстранённому канцлеру кнопка выдвижения не показывается.", alternative: true, time: "11:05"));

    public static SWTrainingStage[] ElectionVote() => Steps(
        Back("Кандидатов двое, Тала Вен и Вейл Оран. За Вейла Орана уже отдан один голос.",
            ElectionBoard("Не подан", false, false, (ExampleName, 0, "Голосовать", false), (BillAuthor, 1, "Голосовать", false)), "11:15"),
        Aim(ElectionBoard("Не подан", false, false, (ExampleName, 0, "Голосовать", true), (BillAuthor, 1, "Голосовать", false)), time: "11:15"),
        Press(ElectionBoard("Не подан", false, false, (ExampleName, 0, "Голосовать", true), (BillAuthor, 1, "Голосовать", false)), time: "11:15"),
        Result(ElectionBoard(ExampleName, false, false, (ExampleName, 1, "Вы голосовали", false), (BillAuthor, 1, "Голосовать", false)),
            "Ваш голос учтён.", "За себя голосовать можно. До конца выборов голос можно отдать другому кандидату.", time: "11:15"),
        Result(ElectionBoard(ExampleName, false, false, (ExampleName, 2, "Вы голосовали", false), (BillAuthor, 1, "Голосовать", false)),
            note: "Дара Квел голосует за Талу Вен.", time: "11:30"));

    public static SWTrainingStage[] ElectionResult() => Steps(
        Wait(ElectionBoard(ExampleName, false, false, (ExampleName, 2, "Вы голосовали", false), (BillAuthor, 1, "Голосовать", false)),
            "Занятых секторов четыре, большинство три голоса.", 4f, "11:40"),
        new Step(ResultHold, Closeup("11:45"), NoBallot(ElectionSession),
            Notes: Heard($"Выборы завершены: Канцлером избран(а) {ExampleName}. Требуется подтверждение полномочий в рабочей консоли.", "Сенат",
                "Сен Арро отдаёт третий голос: большинство набрано, выборы закончились сразу.")),
        Result(NoBallot(), note: "Без большинства выборы кончаются раньше, только если проголосовали все занятые сектора и впереди один кандидат. Иначе в 13:00 побеждает набравший больше голосов, при равенстве выдвинувшийся раньше; без голосов выборы не состоялись, и через час вакансии Сенат назначит новые.",
            alternative: true, time: "13:00"));

    public static SWTrainingStage[] ElectionConfirm() => Steps(
        Back("Тала Вен избрана большинством. Избрание ждёт её подтверждения.", HandOut()),
        HandIn(),
        Aim(Form("Рабочий стол", Btn("Должность", true)), time: "12:00"),
        Press(Form("Рабочий стол", Btn("Должность", true)), time: "12:00"),
        Aim(ElectCard(true), time: "12:00"),
        Press(ElectCard(true), time: "12:00"),
        Result(Office("Верховный Канцлер", true), "Вы вступили в должность.", "Корусант освобождён: его может занять другой сенатор.", time: "12:00"),
        new Step(ResultHold, Closeup("12:00"), Office("Верховный Канцлер", true),
            Notes: Heard($"{ExampleName} вступил(а) в должность Верховного Канцлера.", "Сенат Республики", "Об этом узнаёт весь Сенат.")),
        Result(ElectCard(), note: "Подтвердить можно только днём и своей картой сенатора. Кто до этого потерял сектор, теряет и избрание.",
            alternative: true, time: "12:00"));

    // ---- 04А --------------------------------------------------------------

    /// <summary>The «Трибуна» tab, seen only by whoever runs the floor.</summary>
    private static SWTrainingFormPanel FloorTab(string? holder, string[] queue, int aim = -1, bool aimRevoke = false)
    {
        var rows = new List<SWTrainingRow>
        {
            Choice(LeaderVoteTabs, 2),
            holder == null ? Dim("Слово сейчас ни у кого.") : Txt($"СЛОВО У: {holder}"),
            Btn("Забрать слово", aimRevoke, holder == null, SWTrainingAccent.Neutral),
            Btn("Слово канцлеру"),
            Sec("Очередь на слово"),
        };
        if (queue.Length == 0)
            rows.Add(Dim("Очередь пуста."));
        for (var i = 0; i < queue.Length; i++)
        {
            rows.Add(Txt($"{i + 1}. {queue[i]}"));
            rows.Add(Choice(QueueButtons, -1, i == 0 ? aim : -1));
        }

        return new SWTrainingFormPanel(VoteTitle, rows.ToArray());
    }

    private static readonly string[] TwoInQueue = { ExampleName, BillAuthor };
    private static readonly string[] AuthorInQueue = { BillAuthor };

    public static SWTrainingStage[] FloorMic() => Steps(
        Picture(Room(SWTrainingScene.HallSpeaker), 3f, ScreenNote("Спикер открывает заседание у своего микрофона.")),
        Picture(Room(SWTrainingScene.HallSpeaker), 4f, Heard("Ваша трибуна включена.", null, "Пока трибуна включена, речь спикера слышит весь Сенат. Повторное нажатие её выключает.")),
        HandOut(),
        HandIn(),
        Aim(TabRow(VoteTitle, LeaderVoteTabs, 2), time: "10:15"),
        Press(TabRow(VoteTitle, LeaderVoteTabs, 2), time: "10:15"),
        Result(FloorTab(null, TwoInQueue), note: "Вкладку «Трибуна» видит только ведущий заседание. В очереди двое.", time: "10:15"));

    public static SWTrainingStage[] FloorGrant() => Steps(
        Aim(FloorTab(null, TwoInQueue, 0), time: "10:15"),
        Press(FloorTab(null, TwoInQueue, 0), time: "10:15"),
        new Step(ResultHold, Closeup("10:15"), FloorTab(ExampleName, AuthorInQueue),
            Notes: Heard($"Слово предоставлено: {ExampleName}.", null, "Объявление видят все, а речь Талы Вен идёт с пометкой «ТРИБУНА».")),
        Case("Вейл Оран передумал выступать и просит снять его из очереди.", FloorTab(ExampleName, AuthorInQueue), "10:16"),
        Aim(FloorTab(ExampleName, AuthorInQueue, 1), true, "10:16"),
        Press(FloorTab(ExampleName, AuthorInQueue, 1), true, "10:16"),
        Result(FloorTab(ExampleName, Array.Empty<string>()), "Сенатор убран из очереди.",
            "«Убрать» снимает сенатора из очереди без слова; Тала Вен продолжает говорить.", true, "10:16", continues: true));

    public static SWTrainingStage[] FloorRevoke() => Steps(
        Back("Вейл Оран остался в очереди. Тала Вен закончила речь.", FloorTab(ExampleName, AuthorInQueue), "10:25"),
        Aim(FloorTab(ExampleName, AuthorInQueue, aimRevoke: true), time: "10:25"),
        Press(FloorTab(ExampleName, AuthorInQueue, aimRevoke: true), time: "10:25"),
        Result(FloorTab(null, AuthorInQueue), "Слово отозвано.", "Слово ни у кого: речь снова слышна только рядом. Следующим можно дать слово Вейлу Орану.", time: "10:25"));

    private const string DocksBill = "О ремонте доков";
    private const string DocksSession = AfternoonSession;

    private static SWTrainingRow[] EndButton(bool aim = false) => new[] { Btn("Завершить голосование", aim) };

    /// <summary>The docks bill of Вейл Оран, seen by the speaker or the chancellor: neither holds a sector, so neither votes.</summary>
    private static SWTrainingFormPanel DocksBallot(int yes, int no, bool tabs3, bool tie = false, SWTrainingRow[]? extra = null) =>
        Ballot("Не подан", yes, no, 0, tabs3: tabs3, canVote: false, tie: tie, session: DocksSession, bill: DocksBill, author: BillAuthor,
            number: 16, effects: string.Empty, extra: extra);

    public static SWTrainingStage[] TieEnd() => Steps(
        Aim(TabRow(VoteTitle, LeaderVoteTabs, 1), time: "15:20"),
        Press(TabRow(VoteTitle, LeaderVoteTabs, 1), time: "15:20"),
        Wait(DocksBallot(2, 1, true), "Голосовали трое из четырёх. Перевес не решает исход, кнопки завершения нет.", 4f, "15:20"),
        Wait(DocksBallot(2, 2, true, extra: EndButton()), "Проголосовал четвёртый сектор: 2 на 2.", 4f, "15:30"),
        Aim(DocksBallot(2, 2, true, extra: EndButton(true)), time: "15:30"),
        Press(DocksBallot(2, 2, true, extra: EndButton(true)), time: "15:30"),
        Result(DocksBallot(2, 2, true, true), "Голосование завершено.", "Ничья: исход решит канцлер. Голоса сенаторов уже не изменить.", time: "15:30"),
        Result(Agenda("Отклонён", DocksBill, BillAuthor, 16), note: "Если ничью не разрешили до конца заседания, в 17:00 проект отклонён.", alternative: true, time: "17:00"));

    private static SWTrainingRow[] TieRows(int aim = -1) => new[]
    {
        Warn("Ничья. Ваш решающий голос как Канцлера:"),
        Choice(TiebreakButtons, -1, aim),
    };

    public static SWTrainingStage[] TieBreak() => Steps(
        Back("Заседание ещё идёт, ничья 2 на 2 ждёт решающего голоса канцлера.", HandOut()),
        HandIn(),
        Aim(DocksBallot(2, 2, false, true, TieRows(0)), time: "15:35"),
        Press(DocksBallot(2, 2, false, true, TieRows(0)), time: "15:35"),
        Result(Agenda("Прошёл голосование - ждёт подписи Канцлера", DocksBill, BillAuthor, 16), "Решающий голос учтён.",
            "Решающий ЗА: проект ждёт подписи. Решающий ПРОТИВ отклонил бы его.", time: "15:35"),
        Result(DocksBallot(2, 2, false, true, TieRows()),
            note: "Нет канцлера: те же кнопки у спикера. Нет спикера: канцлер сам завершает голосование и ведёт трибуну.", alternative: true, time: "15:35"));
}
