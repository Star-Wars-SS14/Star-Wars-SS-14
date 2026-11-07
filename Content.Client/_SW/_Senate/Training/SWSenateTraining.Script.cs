using System;
using System.Linq;

namespace Content.Client._SW._Senate.Training;

/// <summary>Unlabelled conditional fragment drawn by code on a screen; it never imitates a real console.</summary>
public enum SWTrainingSchema
{
    None,
    List,
    Columns2,
    Columns3,
    Steps3,
    Steps4,
    Meter,
    Log,
}

public enum SWTrainingFocus
{
    None,
    Judges,
    Prosecutor,
    Defender,
}

/// <summary>
/// A picture over the scene. With <paramref name="Advance"/> it moves from frame 0 to frame 1 during the shot. With
/// <paramref name="Sequence"/> it runs through all its frames in that many seconds, last to first with
/// <paramref name="Reverse"/>, and stays on the final one; Frame is then the reduced motion still.
/// </summary>
public sealed record SWTrainingOverlay(SWTrainingArt Art, int X, int Y, int Frame = 0, bool Advance = false, float Sequence = 0f, bool Reverse = false);

/// <summary>What the shot shows. <paramref name="Then"/> replaces the scene halfway through the shot.</summary>
public sealed record SWTrainingVisual(
    SWTrainingScene Scene,
    SWTrainingOverlay[] Overlays,
    SWTrainingSchema Schema = SWTrainingSchema.None,
    int Highlight = -1,
    string[]? Labels = null,
    SWTrainingFocus Focus = SWTrainingFocus.None,
    string? ClockText = null,
    SWTrainingScene? Then = null);

/// <summary>
/// Everything here is read by the player. Note is an optional extra line with a condition, a limit, a consequence or a
/// direct instruction that Title and Text do not already give; it is empty when it would only repeat them. The staging
/// remarks of the script's action column are not screen text: the picture shows them.
/// When <paramref name="Stages"/> is set, the stages are what the screen shows and Visual is only the shot's summary.
/// </summary>
public sealed record SWTrainingShot(string Title, string Text, string Note, SWTrainingVisual Visual, SWTrainingStage[]? Stages = null);

public sealed record SWTrainingChapter(string Id, string Title, string Goal, SWTrainingShot[] Shots);

/// <summary>An occupation in the chooser. It only filters the chapter list and grants nothing.</summary>
public sealed record SWTrainingRole(string Id, string? JobId, string Title, string[] ChapterIds);

/// <summary>The approved Senate training script as data: 32 chapters and 162 shots, 60 seconds each chapter.</summary>
public static class SWSenateTrainingScript
{
    public const float ChapterLength = 60f;

    /// <summary>Assumed unhurried reading pace. A shot with more text than the script's time allows stays longer.</summary>
    public const float ReadingCharsPerSecond = 14f;

    /// <summary>How long the result of a staged shot stays on screen before the shot may end.</summary>
    public const float MinResultHold = 8f;

    /// <summary>
    /// The script's own time for the shot, extended when its visible text needs longer to read and so that the last
    /// stage keeps its full hold. Under reduced motion stages start later, so such a shot lasts longer.
    /// </summary>
    public static float ShotDuration(SWTrainingChapter chapter, SWTrainingShot shot, bool reducedMotion = false)
    {
        var characters = shot.Title.Length + shot.Text.Length + shot.Note.Length;
        var duration = MathF.Max(ChapterLength / chapter.Shots.Length, characters / ReadingCharsPerSecond);
        if (shot.Stages is not { Length: > 0 } stages)
            return duration;

        var last = reducedMotion ? stages[^1].ReducedStart : stages[^1].Start;
        return MathF.Max(duration, last + MinResultHold);
    }

    private const string CycleGoal = "Для того, кто занял это ведомство: первый рабочий цикл.";
    private const string OfficeGoal = "Первые действия и пределы полномочий.";
    private const string ShiftGoal = "Первые действия и пределы полномочий. Сначала смотрите главу 01А о смене.";

    private const string SeatsNote = "Одновременно доступны один верховный судья, два судьи, один обвинитель и один защитник.";
    private const string RegisterText = "Днём вставьте личную судебную карту со своим именем и зарегистрируйте свободное место.";
    private const string ScheduleText = "Утреннее слушание идёт с девяти до тринадцати, вечернее — с пятнадцати до девятнадцати.";

    // ---- visual helpers ---------------------------------------------------

    private static SWTrainingOverlay A(SWTrainingArt art, int frame = 0) => new(art, SWSenateTrainingArt.SlotAX, SWSenateTrainingArt.SlotY, frame);

    private static SWTrainingOverlay AdvanceA(SWTrainingArt art) => new(art, SWSenateTrainingArt.SlotAX, SWSenateTrainingArt.SlotY, 0, true);

    private static SWTrainingOverlay B(SWTrainingArt art, int frame = 0) => new(art, SWSenateTrainingArt.SlotBX, SWSenateTrainingArt.SlotY, frame);

    private static SWTrainingOverlay Clock() => new(SWTrainingArt.MarkClock, 530, 28);

    private static SWTrainingOverlay Layer(SWTrainingArt art) => new(art, 0, 0);

    private static SWTrainingVisual In(SWTrainingScene scene, params SWTrainingOverlay[] overlays) => new(scene, overlays);

    private static SWTrainingVisual Con(SWTrainingSchema schema, int highlight, params SWTrainingOverlay[] overlays) =>
        new(SWTrainingScene.Console, overlays, schema, highlight);

    private static SWTrainingVisual L(params SWTrainingOverlay[] overlays) => Con(SWTrainingSchema.List, 0, overlays);

    private static SWTrainingVisual M(params SWTrainingOverlay[] overlays) => Con(SWTrainingSchema.Meter, 0, overlays);

    private static SWTrainingVisual G() => Con(SWTrainingSchema.Log, -1);

    private static SWTrainingVisual C2(int highlight = -1) => Con(SWTrainingSchema.Columns2, highlight);

    private static SWTrainingVisual C3(int highlight = -1) => Con(SWTrainingSchema.Columns3, highlight);

    private static SWTrainingVisual S3() => Con(SWTrainingSchema.Steps3, 0);

    private static SWTrainingVisual S4(int highlight = -1) => Con(SWTrainingSchema.Steps4, highlight);

    private static SWTrainingVisual Card() =>
        In(SWTrainingScene.ConsoleMounted, new SWTrainingOverlay(SWTrainingArt.HandId, 440, 142, 0, true));

    private static SWTrainingVisual CardInserted() =>
        In(SWTrainingScene.ConsoleMounted, new SWTrainingOverlay(SWTrainingArt.HandId, 440, 142, 1));

    private static SWTrainingVisual Chart(int frame = 0, params SWTrainingOverlay[] extra)
    {
        var overlays = new SWTrainingOverlay[extra.Length + 1];
        overlays[0] = new SWTrainingOverlay(SWTrainingArt.ChartFragment, 150, 20, frame);
        for (var i = 0; i < extra.Length; i++)
            overlays[i + 1] = extra[i];

        return In(SWTrainingScene.Console, overlays);
    }

    private static SWTrainingVisual Desk(params SWTrainingOverlay[] overlays) => In(SWTrainingScene.Desk, overlays);

    private static SWTrainingVisual Term(params SWTrainingOverlay[] overlays) => In(SWTrainingScene.Terminal, overlays);

    private static SWTrainingVisual Entrance(params SWTrainingOverlay[] overlays) => In(SWTrainingScene.Entrance, overlays);

    private static SWTrainingVisual Empty(params SWTrainingOverlay[] overlays) => In(SWTrainingScene.HallEmpty, overlays);

    private static SWTrainingVisual Bench(SWTrainingArt gear) => In(SWTrainingScene.Bench, Layer(gear));

    private static SWTrainingVisual Dossier() => In(SWTrainingScene.Dossier);

    private static SWTrainingVisual Hall() => In(SWTrainingScene.Hall);

    private static SWTrainingVisual Court(SWTrainingFocus focus = SWTrainingFocus.None) =>
        new(SWTrainingScene.Court, Array.Empty<SWTrainingOverlay>(), Focus: focus);

    private static SWTrainingShot S(string title, string text, string note, SWTrainingVisual visual) => new(title, text, note, visual);

    /// <summary>A shot whose title, text and picture already say everything.</summary>
    private static SWTrainingShot S(string title, string text, SWTrainingVisual visual) => new(title, text, string.Empty, visual);

    /// <summary>A shot with stages; its summary picture is the final stage.</summary>
    private static SWTrainingShot Staged(string title, string text, string note, SWTrainingStage[] stages) =>
        new(title, text, note, stages[^1].Visual, stages);

    private static SWTrainingChapter Ch(string id, string title, string goal, params SWTrainingShot[] shots) => new(id, title, goal, shots);

    /// <summary>True when a chapter shows illustrations of game windows, which need room of their own.</summary>
    public static bool HasPanels(SWTrainingChapter chapter)
    {
        foreach (var shot in chapter.Shots)
        {
            if (shot.Stages == null)
                continue;

            foreach (var stage in shot.Stages)
            {
                if (stage.Panel != null)
                    return true;
            }
        }

        return false;
    }

    // ---- chapters ---------------------------------------------------------

    public static readonly SWTrainingChapter[] Chapters =
    {
        Ch("01", "Общий выпуск", "Для всех ролей: как отличать карту, должность и время.",
            Staged("Начинается созыв", "Проверьте игровое время: днём доступны основные решения.", "У отдельных действий есть свои условия.",
                SWTrainingSamples.GeneralTime()),
            Staged("Вставьте личную карту", "Карта определяет доступ, но сама не занимает должность.", "Должность нужно занять отдельно.",
                SWTrainingSamples.GeneralCard()),
            Staged("Займите должность", "Сенатор выбирает сектор, министр — ведомство, судебный работник регистрирует место.", "Если место занято, выберите другое. Это разные консоли: «Занять место», «Занять портфель», «Принять полномочия».",
                SWTrainingSamples.GeneralSeat()),
            Staged("Читайте состояние действия", "Заказ, выполнение и завершение — разные этапы.", "Например, проект может ещё ожидать материалов.",
                SWTrainingSamples.GeneralStages()),
            Staged("Уточняйте у коллег", "Министерства, суд и Сенат решают разные вопросы.", "С вопросом обращайтесь к соответствующему должностному лицу.",
                SWTrainingSamples.GeneralColleague())),

        Ch("01А", "Смена персонала", "Для пристава, главы и офицера гвардии, сенатского врача: как начать и закончить оплачиваемую смену.",
            Staged("Откройте консоль смены", "Пристав, гвардия и врач отмечаются на консоли дежурств персонала.", string.Empty,
                SWTrainingSamples.ShiftOpen()),
            Staged("Заступите на смену", "Выберите «Заступить на смену».", "Смена будет принята, а ваше имя появится в списке «На смене:».",
                SWTrainingSamples.ShiftStart()),
            Staged("Проверьте своё имя в списке дежурных", "Имя в списке «На смене:» и кнопка «Сдать смену» показывают, что смена идёт.", "Если должность не опознана, кнопки смены нет: проверьте роль или ID персонала при себе.",
                SWTrainingSamples.ShiftCheck()),
            Staged("Зарплата начисляется в рабочий день", "На смене вы получаете зарплату каждый игровой час активного рабочего дня.", "Зарплата идёт только тем, кто на смене.",
                SWTrainingSamples.ShiftPay()),
            Staged("Завершите смену", "Когда работа окончена, выберите «Сдать смену» на той же консоли.", "Повторное действие завершает смену; вставлять карту для переключения не требуется. После сдачи начисления за смену прекращаются.",
                SWTrainingSamples.ShiftEnd())),

        Ch("02", "Сенатор: сектор и программы", "Для представителя сектора: как выбрать сектор и начать работу с миром.",
            Staged("Выберите сектор", "Вставьте карту сенатора и выберите свободный сектор.", "Занятый сектор выбрать не получится.",
                SWTrainingSamples.SenatorSeat()),
            Staged("Вставьте карту", "На рабочем столе проверьте, что выбранный сектор ваш.", "На экране будут данные вашего представительства.",
                SWTrainingSamples.SenatorDesk()),
            Staged("Выберите мир и программу", "Сравните стоимость, нужные материалы и бюджет вашего сектора.", "Выбрать можно один из миров вашего сектора.",
                SWTrainingSamples.SenatorCompare()),
            Staged("Оплатите программу", "Заказ оплачивается бюджетом сектора и появляется в очереди.", "Если денег не хватает, заказ не появится.",
                SWTrainingSamples.SenatorFund()),
            Staged("Дождитесь поставок", "Доставка и работа занимают время, а эффект зависит от снабжения.", "Проверяйте доставленные материалы и состояние программы.",
                SWTrainingSamples.SenatorWait())),

        Ch("03", "Законы и комитеты", "Для сенатора: путь проекта от предложения до голосования.",
            Staged("Откройте законопроект", "Вставьте карту сенатора с выбранным сектором и начните проект днём.", "Ночью подача недоступна.",
                SWTrainingSamples.BillOpen()),
            Staged("Задайте название и эффекты", "Введите название и добавьте хотя бы одно допустимое ненулевое изменение.", "Следите за выбранными эффектами и их стоимостью.",
                SWTrainingSamples.BillEdit()),
            Staged("Уложитесь в лимит", "Сумма эффектов не должна превышать доступный лимит.", "Если лимит превышен, исправьте проект.",
                SWTrainingSamples.BillLimit()),
            Staged("Отправьте проект", "У автора может быть только один активный законопроект.", "Проект направляется в комитет; второй активный не принимается.",
                SWTrainingSamples.BillSubmit()),
            Staged("Следите за этапами", "Рассмотрение, голосование и подпись проходят отдельно.", "Проверяйте статус: не считайте, что закон уже действует.",
                SWTrainingSamples.BillPathStages())),

        Ch("04", "Спикер", "Для спикера: работа с заседаниями.",
            Staged("Вступите в должность", "Вставьте карту спикера и вступите в должность на рабочем столе.", "Занятое место не будет выдано второму спикеру.",
                SWTrainingSamples.SpeakerOffice()),
            Staged("Выберите проект", "Найдите проект и выберите время и продолжительность заседания.", "Проверьте, свободно ли это время.",
                SWTrainingSamples.SpeakerPick()),
            Staged("Назначьте заседание", "Спикер назначает заседание в течение рабочего дня.", "Недопустимое время не создаёт заседание.",
                SWTrainingSamples.SpeakerPropose()),
            Staged("Дождитесь подтверждения", "Канцлер подтверждает назначенное заседание.", "При отсутствии канцлера его полномочия исполняет спикер.",
                SWTrainingSamples.SpeakerWait()),
            Staged("Ведите заседание", "Следите за временем обсуждения и голосования.", "Итог проверьте в реестре.",
                SWTrainingSamples.SpeakerLead())),

        Ch("05", "Канцлер", "Для канцлера: вступление в должность и действия по законам.",
            Staged("Вступите в должность", "Вставьте карту канцлера и вступите в должность.", "При занятом месте второй канцлер не вступит.",
                SWTrainingSamples.ChancellorOffice()),
            Staged("Подтвердите заседание", "Спикер назначает заседание, канцлер его подтверждает.", "При отсутствии спикера канцлер назначает сам.",
                SWTrainingSamples.ChancellorConfirm()),
            Staged("Прочитайте закон", "Изучите проект после голосования и до решения.", "Сравните содержание и последствия.",
                SWTrainingSamples.ChancellorRead()),
            Staged("Подпишите закон", "Днём подпишите закон, если согласны с ним.", "Успешная подпись меняет состояние проекта.",
                SWTrainingSamples.ChancellorSign()),
            Staged("Вето — особое право", "Вето доступно днём только при наличии соответствующего чрезвычайного полномочия.", "Без полномочия закон не получает вето; при вакансии канцлера действует спикер.",
                SWTrainingSamples.ChancellorVeto())),

        Ch("05А", "Бюджет: проект и применение", "Для министра финансов и канцлера: сроки бюджета и автоматическое применение.",
            Staged("Подготовьте проект бюджета", "Министр финансов готовит проект распределения средств.", "Подготовка ещё не перечисляет деньги.",
                SWTrainingSamples.BudgetPrepare()),
            Staged("Подайте проект канцлеру", "Поданный проект становится доступен для рассмотрения.", string.Empty,
                SWTrainingSamples.BudgetSubmit()),
            Staged("Канцлер рассматривает проект", "Канцлер может рассмотреть проект до девяти часов и принять его или отклонить с замечанием.", "При вакансии канцлера то же может сделать действующий спикер своей картой. Ручное решение не обязательно. Отклонение возвращает проект Минфину.",
                SWTrainingSamples.BudgetApprove()),
            Staged("В 09:00 бюджет применяется автоматически", "Если к девяти часам бюджет не утверждён, он применяется автоматически: последний подходящий поданный проект или стандартный бюджет.", "Если проект отклонён и остался черновиком, применяется стандартное распределение.",
                SWTrainingSamples.BudgetDeadline()),
            Staged("При вакансии Минфина проект подаётся автоматически", "С 08:00 в активный рабочий день, если Минфин вакантен, бюджет остаётся черновиком и автоподача сегодня ещё не выполнялась, стандартный проект подаётся автоматически.", "Если одновременно нет канцлера и спикера, поданный проект может быть применён до 09:00. Иначе он ждёт решения или применяется по сроку.",
                SWTrainingSamples.BudgetVacancy())),

        Ch("06", "Выбор ведомства", "Для всех министров: как выбрать одну свободную специализацию.",
            Staged("Выберите ведомство", "Вставьте карту министра в консоль выбора.", "Консоль покажет доступные ведомства.",
                SWTrainingSamples.MinisterCard()),
            Staged("Сравните направления", "У каждого ведомства свои решения и бюджет.", "Прочитайте описание до выбора.",
                SWTrainingSamples.MinisterCompare()),
            Staged("Подтвердите выбор", "Выберите свободное ведомство и подтвердите назначение.", "Занятое ведомство выбрать не получится.",
                SWTrainingSamples.MinisterClaim()),
            Staged("Выбор закреплён", "Одной картой нельзя снова выбирать ведомство как в первый раз.", "Проверьте полученную специализацию.",
                SWTrainingSamples.MinisterFixed()),
            Staged("Откройте рабочее место", "Перейдите к консоли своего ведомства и изучите бюджет и очередь.", string.Empty,
                SWTrainingSamples.MinisterDesk())),

        Ch("07", "Министерство обороны", CycleGoal,
            Staged("Оцените границу", "Посмотрите давление на границе и развёрнутые силы.", "Решите, где требуется внимание.",
                SWTrainingSamples.DefenseBorder()),
            Staged("Выберите позицию", "Позиция обороны меняется отдельно от объявления войны.", "Если есть канцлер или спикер, повышение до DEFCON 1–2 ждёт решения. Если нет обоих, позиция меняется сразу.",
                SWTrainingSamples.DefenseStance()),
            Staged("Учитывайте содержание", "Развёрнутые группы требуют средств и помогают сдерживать давление.", "Сравните силы с бюджетом.",
                SWTrainingSamples.DefenseUpkeep()),
            Staged("Рассмотрите военный запрос", "Запрос войны передаётся канцлеру, который отдельно одобряет или отклоняет его.", "При отсутствии канцлера его решение принимает спикер; без запроса решения нет. Запрос на DEFCON 2 решается так же, но это мобилизация, а не война.",
                SWTrainingSamples.DefenseRequest()),
            Staged("Проверьте обстановку", "После решения следите за изменениями границы и расходами.", string.Empty,
                SWTrainingSamples.DefenseCheck())),

        Ch("08", "Министерство внутренней безопасности", CycleGoal,
            Staged("Оцените угрозы", "Проверьте угрозу и накал: подрывную активность и общественное недовольство.", "Это общие показатели. Подробности на вкладках «Карта и группы» и «Расследования».",
                SWTrainingSamples.SecurityThreats()),
            Staged("Выберите режим", "Режим безопасности задаётся отдельно от чрезвычайного решения.", "Если есть канцлер или спикер, чрезвычайное положение ждёт решения. Если нет обоих, режим меняется сразу.",
                SWTrainingSamples.SecurityMode()),
            Staged("Рассмотрите запрос", "Канцлер отдельно одобряет или отклоняет ожидающий чрезвычайный режим.", "При отсутствии канцлера решение принимает спикер; без запроса оно недоступно.",
                SWTrainingSamples.SecurityRequest()),
            Staged("Следите за ситуацией", "Проверяйте, как события в секторах меняют угрозу.", "Сопоставляйте состояние сектора с общей обстановкой.",
                SWTrainingSamples.SecurityWatch()),
            Staged("Прочитайте результат", "После решения посмотрите, что изменилось.", "Выбранный режим не означает мгновенной победы.",
                SWTrainingSamples.SecurityOutcome())),

        Ch("09", "Министерство экономического развития", CycleGoal,
            Staged("Найдите нужный товар", "Выберите товар и посмотрите кандидатов на расширение.", "Сравните миры и рецепты.",
                SWTrainingSamples.EconomyFind()),
            Staged("Оплатите расширение", "Выберите подходящий проект и оплатите его бюджетом ведомства.", "При недостатке денег проект не будет поставлен.",
                SWTrainingSamples.EconomyFund()),
            Staged("Дождитесь доставки", "Материалы и работа нужны до запуска расширения.", "Статус ожидания не означает готовый завод.",
                SWTrainingSamples.EconomyWait()),
            Staged("Проверьте маршрут", "Расширение доступного маршрута финансируется отдельно.", "Если проект уже идёт или условия изменились, заказ не повторится.",
                SWTrainingSamples.EconomyRouteFund()),
            Staged("Следите за результатом", "Смотрите доставку материалов и завершение работ.", "Прогресс виден по обоим проектам.",
                SWTrainingSamples.EconomyBoth())),

        Ch("10", "Министерство финансов", CycleGoal,
            Staged("Проверьте финансы", "Посмотрите состояние казны, налог и ставку.", "Определите, какая мера нужна.",
                SWTrainingSamples.FinanceCheck()),
            Staged("Выберите параметры", "Налог и ставка меняются отдельно; сначала прочитайте последствия.", string.Empty,
                SWTrainingSamples.FinanceParameters()),
            Staged("Облигации создают долг", "Выпуск пополняет казну, но оставляет долг с процентами.", "Выпуск можно выбрать, когда он доступен.",
                SWTrainingSamples.FinanceBonds()),
            Staged("Погасите долг", "Долг можно погасить при наличии долга и достаточных средств.", "Без денег погашение не состоится.",
                SWTrainingSamples.FinanceRepay()),
            Staged("Эмиссия требует решения", "Объявленную эмиссию отдельно одобряет или отклоняет канцлер; при его отсутствии — спикер.", "Если нет обоих, эмиссия проходит сразу. Затем сверьте итог.",
                SWTrainingSamples.FinanceEmission())),

        Ch("11", "Министерство юстиции", CycleGoal,
            Staged("Выберите учреждение", "Откройте доступную проверку учреждения в мире Республики.", "Повторно занятое учреждение выбрать нельзя.",
                SWTrainingSamples.JusticeInstitution()),
            Staged("Соберите материалы", "Назначьте платные проверки и дождитесь результатов.", "Недостаток бюджета или занятая проверка не даст нового запуска.",
                SWTrainingSamples.JusticeChecks()),
            Staged("Сформулируйте вывод", "Для заключения нужны минимум два разных собранных материала.", "Выберите их и тип нарушения.",
                SWTrainingSamples.JusticeConclude()),
            Staged("Назначьте меру", "Мера зависит от выбранного заключения и может стоить денег.", string.Empty,
                SWTrainingSamples.JusticeRemedy()),
            Staged("Проверьте результат", "После завершения меры проведите повторную проверку.", "Неверный вывод может снизить доверие или навредить сектору.",
                SWTrainingSamples.JusticeFollowUp())),

        Ch("12", "Министерство здравоохранения", CycleGoal,
            Staged("Найдите очаг", "Оцените заражённые миры и распространение.", "Выберите цель для помощи.",
                SWTrainingSamples.HealthFind()),
            Staged("Выберите мир", "Проверьте доступную помощь и медицинские проекты мира.", "Недоступная цель не получит проект.",
                SWTrainingSamples.HealthPick()),
            Staged("Закажите помощь", "Помощь и строительство требуют действующего министра, дневного времени и ресурсов.", "Заказ появляется в разделе «Снабжение». Пока он не доставлен, второй заказ помощи этому миру не примут.",
                SWTrainingSamples.HealthOrder()),
            Staged("Следите за поставкой", "Ресурсы должны добраться до выбранного мира.", "Заявка остаётся в работе, пока поставка не завершена. Срок не фиксирован, а маршрут может оказаться недоступен.",
                SWTrainingSamples.HealthDelivery()),
            Staged("Проверьте влияние", "Смотрите состояние помощи и развитие очага.", "Проверьте, достаточно ли принятых мер.",
                SWTrainingSamples.HealthEffect())),

        Ch("12А", "Здравоохранение: вакцина и поставки", "Для министра здравоохранения: исследование и доставка вакцины.",
            Staged("Отберите пробы", "Выберите до двух проб с разных заражённых миров.", "Незаражённый или неподходящий мир не даст пробу.",
                SWTrainingSamples.VaccineSamples()),
            Staged("Финансируйте исследование", "Задайте базовый или усиленный уровень финансирования и дождитесь анализа.", "Средства расходуются во время исследования до появления кандидата.",
                SWTrainingSamples.VaccineFunding()),
            Staged("Выберите препарат", "Когда анализ завершён, выберите один из двух препаратов.", "Выбор переводит исследование к испытаниям.",
                SWTrainingSamples.VaccineCandidate()),
            Staged("Дождитесь готовности", "Запустите испытания в подходящем мире и дождитесь их успешного завершения.", "Испытания требуют денег и материалов; до готовности вакцину не заказать.",
                SWTrainingSamples.VaccineTrials()),
            Staged("Выберите производство и цель", "Выберите подходящий производящий мир и цель; закажите от одной до трёх партий.", "Без готовой вакцины или подходящего производства заказ отклонён; одна активная поставка на цель, всего не больше шести.",
                SWTrainingSamples.VaccineOrder()),
            Staged("Следите за актуальностью", "Мутация может сделать вакцину устаревшей: новая проба перезапустит исследование.", "Проверяйте доставку и актуальность препарата.",
                SWTrainingSamples.VaccineCurrency())),

        Ch("13", "Департамент здравоохранения", CycleGoal,
            Staged("Откройте «Вызовы»", "Вставьте карту и откройте вкладку вызовов.", "На вкладке видны доступные обращения и оставшееся время.",
                SWTrainingSamples.CareOpen()),
            Staged("Проверьте срок", "У вызова есть срок; по его истечении помощь не будет отправлена.", "Выбирайте обращение, срок которого ещё не истёк.",
                SWTrainingSamples.CareDeadline()),
            Staged("Сравните варианты", "Обычный выезд расходует десять запасов; его успех вероятностный.", "Интерфейс показывает текущую вероятность.",
                SWTrainingSamples.CareCompare()),
            Staged("Усиление гарантирует успех", "Усиленный выезд требует пятнадцать запасов и пятьсот кредитов.", "Нужна свободная бригада; без неё или ресурсов отправки не будет.",
                SWTrainingSamples.CareReinforced()),
            Staged("Дождитесь выезда", "Бригада занята до завершения миссии.", "Ведомство расходует запасы, свободных бригад становится меньше.",
                SWTrainingSamples.CareDispatch()),
            Staged("Прочитайте итог", "После миссии бригада возвращается, а в журнале появляется результат.", "Исход может быть успешным или неудачным.",
                SWTrainingSamples.CareResult())),

        Ch("14", "Департамент юстиции", CycleGoal,
            Staged("Распределите отряды", "В начале созыва у департамента четыре отряда и четыре поста; новых отрядов можно набрать до шести.", "Оцените резерв: свободны только отряды, не занятые на постах и в операциях.",
                SWTrainingSamples.GuardStart()),
            Staged("Укрепите пост", "На каждом посту может находиться не больше двух отрядов.", "Лишний отряд не назначается.",
                SWTrainingSamples.GuardPost()),
            Staged("Выберите ордер", "Ордер требует свободных резервных отрядов.", string.Empty,
                SWTrainingSamples.GuardWrit()),
            Staged("Отправьте отряд", "На выполнение нужен один игровой час; может потребоваться один или два отряда.", "При нехватке резерва отправка не состоится.",
                SWTrainingSamples.GuardDispatch()),
            Staged("Проверьте срок и итог", "Необработанный ордер истекает через два игровых часа.", "Когда начатая операция завершится, отряды вернутся, а результат появится в журнале.",
                SWTrainingSamples.GuardResult())),

        Ch("15", "Департамент арбитража", CycleGoal,
            Staged("Выберите дело", "Откройте дело и посмотрите текущую стадию.", "Дела поступают сами; их набор зависит от раунда.",
                SWTrainingSamples.ArbitrageOpen()),
            Staged("Оплатите этап", "Каждая из трёх стадий требует денег ведомства.", "При нехватке бюджета стадия не продвинется.",
                SWTrainingSamples.ArbitragePay()),
            Staged("Учитывайте давление", "На каждом этапе давление может сорвать дело.", "При срыве дело закрывается, независимость падает.",
                SWTrainingSamples.ArbitragePressure()),
            Staged("Продолжайте рассмотрение", "Продвигайте оставшиеся стадии, пока дело не завершено.", "Каждая успешная промежуточная стадия повышает независимость.",
                SWTrainingSamples.ArbitrageContinue()),
            Staged("Проверьте итог", "Завершённое дело приносит штраф и повышает независимость.", "Результат отражается в журнале.",
                SWTrainingSamples.ArbitrageResult())),

        Ch("16", "Департамент учёта расходов", CycleGoal,
            Staged("Выберите ведомство", "Проверить можно занятое чужое ведомство, ещё не проходившее этот аудит.", "Своё или уже проверенное ведомство недоступно.",
                SWTrainingSamples.AuditPick()),
            Staged("Оплатите проверку", "Аудит расходует средства вашего департамента.", "Без достаточного бюджета аудит не начнётся.",
                SWTrainingSamples.AuditPay()),
            Staged("Прочитайте результат", "Отчётность может оказаться чистой, с приписками или следами хищений.", "Исход читайте в журнале на вкладке «Сигналы».",
                SWTrainingSamples.AuditRead()),
            Staged("Возможен возврат", "Приписки могут вернуть часть средств из бюджета проверенного ведомства.", "Размер зависит от состояния и ограничен имеющимися средствами.",
                SWTrainingSamples.AuditRefund()),
            Staged("Передайте сведения", "Следы хищений раскрывают сведения о действующем руководителе для юстиции.", "Это результат проверки, а не судебный приговор.",
                SWTrainingSamples.AuditReport())),

        Ch("17", "Бюро кораблей и услуг", CycleGoal,
            Staged("Откройте заявку", "Посмотрите сведения о судне и грузе.", "Заявки появляются в очереди на вкладке «Стол регистрации».",
                SWTrainingSamples.ShipsOpen()),
            Staged("Осмотрите судно", "Осмотр стоит денег и доступен один раз для заявки.", "Повторный осмотр или нехватка денег не проходят.",
                SWTrainingSamples.ShipsInspect()),
            Staged("Выберите решение", "Одобрите заявку или отклоните её после изучения сведений.", "Решение убирает заявку из очереди.",
                SWTrainingSamples.ShipsDecide()),
            Staged("Взятка несёт риск", "Если предложена взятка, её принятие одобряет заявку и повышает коррупцию.", "Личные деньги не заменяют честную проверку груза.",
                SWTrainingSamples.ShipsBribe()),
            Staged("Проверьте результат", "Одобрение сомнительного судна может повлиять на безопасность.", "Регистрация или отказ отражаются в журнале.",
                SWTrainingSamples.ShipsResult())),

        Ch("18", "Бюро регулирования вооружений", CycleGoal,
            Staged("Выберите сектор", "Изъятие доступно в представленном секторе.", "Незанятый сектор выбрать нельзя.",
                SWTrainingSamples.WeaponsPick()),
            Staged("Проверьте цену", "Нужны средства бюро; повторная операция ждёт окончания задержки.", "Без денег или до окончания задержки изъятие не начинается.",
                SWTrainingSamples.WeaponsPrice()),
            Staged("Проведите изъятие", "Операция уменьшает вооружённость сектора.", "Деньги ведомства расходуются.",
                SWTrainingSamples.WeaponsSeize()),
            Staged("Учитывайте последствия", "Изъятие снижает показатель прав.", "Сравнивайте безопасность и гражданские права.",
                SWTrainingSamples.WeaponsRights()),
            Staged("Проверьте итог", "Если министерство внутренней безопасности занято, операция дополнительно влияет на угрозу и заговоры.", "Результат фиксируется в журнале.",
                SWTrainingSamples.WeaponsLog())),

        Ch("19", "Бюро путешествий и транзита", CycleGoal,
            Staged("Выберите сектор", "Транзитный режим задаётся представленному сектору.", string.Empty,
                SWTrainingSamples.TransitPick()),
            Staged("Сравните режимы", "Открытый режим, визы и кордон по-разному влияют на перемещения.", string.Empty,
                SWTrainingSamples.TransitCompare()),
            Staged("Кордон закрывает путь", "Кордон прекращает перемещения через выбранный сектор.", "Запись о закрытии появляется в журнале бюро.",
                SWTrainingSamples.TransitCordon()),
            Staged("Разберите событие", "Выберите ответ до истечения срока события.", "Просроченное событие завершается неудачей.",
                SWTrainingSamples.TransitEvent()),
            Staged("Проверьте сеть", "Следите за мобильностью, загруженностью и безопасностью.", "Так видны последствия вашей политики.",
                SWTrainingSamples.TransitNetwork())),

        Ch("20", "Верховный судья", OfficeGoal,
            Staged("Зарегистрируйте должность", RegisterText, SeatsNote,
                SWTrainingSamples.JusticeRegister()),
            Staged("Изучите расписание", ScheduleText, "Время показано игровыми часами.",
                SWTrainingSamples.JusticeSchedule()),
            Staged("Выслушайте стороны", "Обвинитель и защитник представляют свои позиции.", "Они не голосуют за вердикт.",
                SWTrainingSamples.JusticeHearing()),
            Staged("Голосуют судьи", "Голоса принимаются от судей с начала слушания до срока решения.", "Голос можно проверить по делу.",
                SWTrainingSamples.JusticeVote()),
            Staged("Закройте решение вовремя", "После слушания верховный или председательствующий закрывает голосование до четырнадцати или двадцати часов.", "К сроку голосование закрывается само. Если голосов нет, решение не будет вынесено.",
                SWTrainingSamples.JusticeClose())),

        Ch("20А", "Суд: назначение судьи", "Для канцлера, кандидата и сенаторов: как проходит назначение судьи.",
            Staged("Проверьте состав суда", "Если верховного судьи нет, а республиканский судья уже есть, сначала нужен верховный.", "Другие судейские назначения в этот момент блокируются.",
                SWTrainingSamples.AppointBench()),
            Staged("Выдвиньте кандидата", "Канцлер выдвигает кандидата; в верховные — действующего республиканского судью. В первый день назначения открываются с десяти часов.", "Одновременно рассматривается один кандидат.",
                SWTrainingSamples.AppointNominate()),
            Staged("Примите или откажитесь", "Кандидат сам принимает или отклоняет выдвижение.", "Отказ прекращает назначение.",
                SWTrainingSamples.AppointAnswer()),
            Staged("Подтвердите кандидатуру", "Сенаторы голосуют до двух активных игровых часов; большинство или все голоса могут завершить голосование раньше.", "Для утверждения требуется большинство от занятых секторов.",
                SWTrainingSamples.AppointConfirm()),
            Staged("Проверьте должность", "После назначения судья освобождает несовместимые занимаемые посты.", "Карта и судебный состав обновляются.",
                SWTrainingSamples.AppointResult())),

        Ch("21", "Республиканский судья", OfficeGoal,
            Staged("Зарегистрируйте должность", RegisterText, SeatsNote, SWTrainingSamples.JudgeRegister()),
            Staged("Изучите расписание", ScheduleText, "Время показано игровыми часами.", SWTrainingSamples.JusticeSchedule()),
            Staged("Выслушайте стороны", "Обвинитель и защитник представляют свои позиции.", "Они не голосуют за вердикт.", SWTrainingSamples.JusticeHearing()),
            Staged("Отдайте голос", "Судья голосует за истца или ответчика с начала слушания до срока решения.", "Голос можно проверить в деле и изменить до закрытия.", SWTrainingSamples.JusticeVote()),
            Staged("Дождитесь решения", "Вердикт закрывает верховный судья или председательствующий после слушания.", "Один голос ещё не означает готовый вердикт.", SWTrainingSamples.JudgeWait())),

        Ch("22", "Обвинитель Республики", OfficeGoal,
            Staged("Зарегистрируйте должность", RegisterText, SeatsNote, SWTrainingSamples.ProsecutorRegister()),
            Staged("Изучите расписание", ScheduleText, "Время показано игровыми часами.", SWTrainingSamples.JusticeSchedule()),
            Staged("Изучите дело", "Прочитайте стороны и обстоятельства текущего дела.", "Подготовьте свою позицию.", SWTrainingSamples.ProsecutorCase()),
            Staged("Представьте позицию", "Представьте позицию обвинения перед судьями на слушании.", string.Empty, SWTrainingSamples.ProsecutorSpeech()),
            Staged("Дождитесь вердикта", "Вердикт принимают судьи; срок решения — до четырнадцати или двадцати часов.", "Обвинитель и защитник не голосуют.", SWTrainingSamples.ProsecutorVerdict())),

        Ch("23", "Защитник Республики", OfficeGoal,
            Staged("Зарегистрируйте должность", RegisterText, SeatsNote, SWTrainingSamples.DefenderRegister()),
            Staged("Изучите расписание", ScheduleText, "Время показано игровыми часами.", SWTrainingSamples.JusticeSchedule()),
            Staged("Изучите дело", "Прочитайте стороны и обстоятельства текущего дела.", "Подготовьте свою позицию.", SWTrainingSamples.DefenderCase()),
            Staged("Представьте позицию", "Представьте позицию защиты перед судьями на слушании.", string.Empty, SWTrainingSamples.DefenderSpeech()),
            Staged("Дождитесь вердикта", "Вердикт принимают судьи; срок решения — до четырнадцати или двадцати часов.", "Обвинитель и защитник не голосуют.", SWTrainingSamples.DefenderVerdict())),

        Ch("24", "Корреспондент Голонета", OfficeGoal,
            Staged("Выберите редакцию", "Достаньте редакционный датапад из рюкзака и выберите редакцию для публикации.", "Открытие публичного терминала само по себе не даёт авторских прав.",
                SWTrainingSamples.PressEdition()),
            Staged("Включите запись", "Возьмите редакционный датапад в активную руку и включите запись. Это не КПК.", "Запись идёт, пока датапад в активной руке.",
                SWTrainingSamples.PressRecord()),
            Staged("Соберите цитаты", "Речь, услышанная корреспондентом, может сохраниться как цитата.", "Не вся обстановка вокруг сказанного попадает в цитату.",
                SWTrainingSamples.PressQuotes()),
            Staged("Подготовьте материал", "Создайте черновик и при желании прикрепите до трёх записанных цитат.", "Цитату можно прикрепить только к сохранённому черновику.",
                SWTrainingSamples.PressDraft()),
            Staged("Опубликуйте", "Проверьте заголовок и содержание, затем опубликуйте статью.", "Читатель увидит статью и прикреплённые цитаты.",
                SWTrainingSamples.PressPublish())),

        Ch("25", "Пристав Сената", ShiftGoal,
            Staged("Вы — пристав Сената", "Ваша карта открывает доступ Сената и гвардии.", "Проверьте доступ и гарнитуру.", SWTrainingSamples.UsherStart()),
            Staged("Проверьте пояс", "Пристав начинает с заполненным поясом охраны.", string.Empty, SWTrainingSamples.UsherBelt()),
            Staged("Подготовьте зал", "Следите за порядком в зале и согласуйте действия со спикером.", "Подойдите к ведущему заседания.", SWTrainingSamples.UsherHall()),
            Staged("Свяжитесь с гвардией", "Вопросы охраны координируйте с гвардией.", "Если офицер гвардии на месте, обратитесь к нему.", SWTrainingSamples.UsherGuard()),
            Staged("Помогайте работе Сената", "Ваша задача — порядок, а не голосование за сектор.", "Оставайтесь в доступной зоне службы.", SWTrainingSamples.UsherService())),

        Ch("26", "Глава Сенатской гвардии", ShiftGoal,
            Staged("Вы возглавляете гвардию", "Ваша карта открывает Сенат, гвардию и командование гвардии.", "Проверьте доступ.", SWTrainingSamples.GuardHeadStart()),
            Staged("Проверьте оснащение", "В стартовом наборе — броня, шлем, пояс и оружие.", string.Empty, SWTrainingSamples.GuardHeadKit()),
            Staged("Согласуйте посты", "Обсудите охрану зала с офицерами.", "Предложите распределение задач.", SWTrainingSamples.GuardHeadPosts()),
            Staged("Наблюдайте за обстановкой", "При угрозе координируйте ответ с гвардией.", "Оцените ситуацию до применения силы.", SWTrainingSamples.GuardHeadWatch()),
            Staged("Обеспечьте работу зала", "Безопасность должна позволять заседанию продолжаться.", "Поддерживайте связь с приставом и офицерами.", SWTrainingSamples.GuardHeadHall())),

        Ch("27", "Офицер Сенатской гвардии", ShiftGoal,
            Staged("Вы — офицер гвардии", "Ваша карта открывает доступ Сената и гвардии.", "Проверьте доступ.", SWTrainingSamples.OfficerStart()),
            Staged("Проверьте снаряжение", "В стартовом наборе — броня, шлем, пояс и оружие.", string.Empty, SWTrainingSamples.OfficerKit()),
            Staged("Следите за залом", "Охраняйте участников и входы, согласуя пост с главой.", "Сообщайте о своём положении.", SWTrainingSamples.OfficerPost()),
            Staged("Предупредите коллег", "Сообщайте об угрозах и необычных ситуациях.", string.Empty, SWTrainingSamples.OfficerWarn()),
            Staged("Оцените последствия", "Применяйте средства охраны с учётом ситуации.", "Офицер не подменяет собой решение Сената.", SWTrainingSamples.OfficerJudge())),

        Ch("28", "Сенатский врач", ShiftGoal,
            new SWTrainingShot("Вы — сенатский врач", "Ваша карта открывает Сенат и медслужбу.", "Проверьте доступ.", SWTrainingSamples.Treating(0), SWTrainingSamples.DoctorArrives()),
            new SWTrainingShot("Проверьте медицинский пояс", "В поясе есть два набора для ушибов, мазь, пакет крови, марлевый бинт и экстренный медипен.", string.Empty, Bench(SWTrainingArt.GearBeltMedical), SWTrainingSamples.BeltOpens()),
            new SWTrainingShot("Оцените травму", "Осмотрите пациента и определите вид повреждения.", "В примере у пациента механическая травма.", SWTrainingSamples.Examining(1), SWTrainingSamples.PatientCheck()),
            new SWTrainingShot("Примените набор для ушибов", "При механической травме примените набор для ушибов к пациенту.", "Одно применение к другому персонажу занимает 3 секунды; движение его прерывает.", SWTrainingSamples.Treating(3), SWTrainingSamples.HealingUse()),
            new SWTrainingShot("Осмотрите повторно", "Проверьте состояние после помощи и при необходимости продолжайте лечение.", "Описание может не измениться. При других повреждениях потребуется подходящее средство.", SWTrainingSamples.Examining(1), SWTrainingSamples.PatientRecheck())),
    };

    // ---- occupations ------------------------------------------------------

    public static readonly SWTrainingRole[] Roles =
    {
        new("general", null, "Общий выпуск для всех ролей", new[] { "01" }),
        new("senator", "SWSenator", "Сенатор", new[] { "01", "02", "03", "20А" }),
        new("speaker", "SWSpeaker", "Спикер Сената", new[] { "01", "04" }),
        new("chancellor", "SWChancellor", "Верховный канцлер", new[] { "01", "05", "05А", "20А" }),
        new("minister", "SWMinister", "Министр", new[] { "01", "06", "05А", "07", "08", "09", "10", "11", "12", "12А", "13", "14", "15", "16", "17", "18", "19" }),
        new("chief-justice", "SWChiefJustice", "Верховный судья", new[] { "01", "20" }),
        new("judge", "SWRepublicJudge", "Судья Республики", new[] { "01", "21", "20А" }),
        new("prosecutor", "SWProsecutor", "Обвинитель Республики", new[] { "01", "22" }),
        new("defender", "SWDefender", "Защитник Республики", new[] { "01", "23" }),
        new("correspondent", "SWHolonetCorrespondent", "Корреспондент Голонета", new[] { "01", "24" }),
        new("usher", "SWUsher", "Пристав Сената", new[] { "01", "01А", "25" }),
        new("guard-head", "SWSenateGuardHead", "Глава Сенатской гвардии", new[] { "01", "01А", "26" }),
        new("guard-officer", "SWSenateGuardOfficer", "Офицер Сенатской гвардии", new[] { "01", "01А", "27" }),
        new("doctor", "SWSenateDoctor", "Сенатский врач", new[] { "01", "01А", "28" }),
        new("all", null, "Все главы", Chapters.Select(chapter => chapter.Id).ToArray()),
    };

    public static SWTrainingChapter? FindChapter(string id)
    {
        foreach (var chapter in Chapters)
        {
            if (chapter.Id == id)
                return chapter;
        }

        return null;
    }
}
