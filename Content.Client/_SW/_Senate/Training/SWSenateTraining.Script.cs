using System;
using System.Collections.Generic;
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
/// <paramref name="Reverse"/>, and stays on the final one; Frame is then the reduced motion still. With
/// <paramref name="Backed"/> a light neutral plate sits under the picture, so a dark item stays readable on a dark scene.
/// </summary>
public sealed record SWTrainingOverlay(SWTrainingArt Art, int X, int Y, int Frame = 0, bool Advance = false, float Sequence = 0f, bool Reverse = false, bool Backed = false);

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

/// <summary>The approved Senate training script as data: 51 chapters and 258 shots, 60 seconds each chapter at least.</summary>
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
        return MathF.Max(duration, MathF.Max(last + MinResultHold, ReaderDone(shot, reducedMotion)));
    }

    // ---- playback rules, shared by the window and the contract -----------

    /// <summary>The closing fade of the last shot; the manual mode stops short of it.</summary>
    public const float EdgeFade = 0.5f;

    /// <summary>How far short of the next stage the manual mode holds the clock.</summary>
    public const float StageEpsilon = 0.01f;

    public static float StageStart(SWTrainingStage stage, bool reducedMotion) => reducedMotion ? stage.ReducedStart : stage.Start;

    /// <summary>The stage on screen follows from the shot's time alone.</summary>
    public static int StageIndexAt(SWTrainingStage[] stages, float time, bool reducedMotion)
    {
        var index = 0;
        for (var i = 1; i < stages.Length; i++)
        {
            if (time >= StageStart(stages[i], reducedMotion))
                index = i;
        }

        return index;
    }

    /// <summary>
    /// One frame of the shot's clock. A pause keeps the time. Automatic play never jumps over a stage and reports when
    /// the shot is over; the manual mode stays in the current stage, short of the next one and of the closing fade, until
    /// the player steps on.
    /// </summary>
    public static (float Time, bool Ended) Advance(SWTrainingChapter chapter, SWTrainingShot shot, float time, float delta,
        bool reducedMotion, bool manual, bool paused)
    {
        if (paused)
            return (time, false);

        var duration = ShotDuration(chapter, shot, reducedMotion);
        var nextStart = float.PositiveInfinity;
        if (shot.Stages is { } stages && StageIndexAt(stages, time, reducedMotion) + 1 < stages.Length)
            nextStart = StageStart(stages[StageIndexAt(stages, time, reducedMotion) + 1], reducedMotion);

        if (manual)
        {
            var limit = MathF.Min(duration - EdgeFade, nextStart - StageEpsilon);
            return (MathF.Min(time + delta, MathF.Max(time, limit)), false);
        }

        var next = MathF.Min(time + delta, nextStart);
        return (next, next >= duration);
    }

    /// <summary>Where «Назад» goes inside a shot when the player holds the time: the start of the stage before.</summary>
    public static float BackWithinShot(SWTrainingStage[] stages, int index, bool reducedMotion) =>
        StageStart(stages[Math.Max(0, index - 1)], reducedMotion);

    /// <summary>A stage's popup is shown for as long as that stage is on screen.</summary>
    public static bool PopupShown(SWTrainingStage[] stages, float time, bool reducedMotion) =>
        stages[StageIndexAt(stages, time, reducedMotion)].Popup != null;

    /// <summary>A popup is short-lived in the game, but here it stays until it can be read, and never less than this.</summary>
    public const float MinPopupSeconds = 4f;

    public static float PopupSeconds(string popup) => MathF.Max(MinPopupSeconds, popup.Length / ReadingCharsPerSecond);

    /// <summary>
    /// True where the data opens a branch (<see cref="SWTrainingStage.Opens"/>). A branch that begins with an action
    /// opens on a stage of its own: the scene back in the state before the action, with the condition said, and only then
    /// the aim and the press. The rest of the branch carries on without being opened again.
    /// </summary>
    public static bool OpensCase(IReadOnlyList<SWTrainingStage> stages, int index) => stages[index].Opens;

    /// <summary>What a stage says on screen, the same in both display modes: its own words, as the data gives them.</summary>
    public static IReadOnlyList<SWTrainingAnnotation> VisibleAnnotations(IReadOnlyList<SWTrainingStage> stages, int index) =>
        stages[index].Annotations ?? Array.Empty<SWTrainingAnnotation>();

    /// <summary>Seconds the stage's own words need at the reading pace: its remarks, game lines and popup.</summary>
    public static float ReadingSeconds(IReadOnlyList<SWTrainingStage> stages, int index)
    {
        var stage = stages[index];
        var characters = stage.Popup?.Length ?? 0;
        foreach (var note in VisibleAnnotations(stages, index))
            characters += note.Text.Length + (note.Heading?.Length ?? 0);
        var seconds = characters / ReadingCharsPerSecond;
        return stage.Popup == null ? seconds : MathF.Max(seconds, PopupSeconds(stage.Popup));
    }

    /// <summary>Seconds the shot's own lines need: title, text and note stay on screen for the whole shot.</summary>
    public static float BaseReadingSeconds(SWTrainingShot shot) =>
        (shot.Title.Length + shot.Text.Length + shot.Note.Length) / ReadingCharsPerSecond;

    /// <summary>
    /// When a reader is done with the shot: first its own lines, then each stage's words in turn, never before that stage
    /// appears. A stage's words have to stay until this reader is through them.
    /// </summary>
    public static float ReaderDone(SWTrainingShot shot, bool reducedMotion)
    {
        if (shot.Stages is not { Length: > 0 } stages)
            return BaseReadingSeconds(shot);

        var free = BaseReadingSeconds(shot);
        for (var i = 0; i < stages.Length; i++)
        {
            var need = ReadingSeconds(stages, i);
            if (need > 0f)
                free = MathF.Max(reducedMotion ? stages[i].ReducedStart : stages[i].Start, free) + need;
        }

        return free;
    }

    /// <summary>
    /// Keeps every stage on screen until a reader who first reads the shot's own lines has also read that stage, in both
    /// timings. So the first remark waits for the main text once, and later short stages keep their own pace. Only gaps
    /// that are too short grow; later stages move by the same amount, so pause, the manual mode and stepping back keep
    /// working on the stored starts.
    /// </summary>
    public static SWTrainingStage[] Paced(SWTrainingStage[] stages, float baseSeconds)
    {
        var paced = new SWTrainingStage[stages.Length];
        var normal = 0f;
        var reduced = 0f;
        var freeNormal = baseSeconds;
        var freeReduced = baseSeconds;
        for (var i = 0; i < stages.Length; i++)
        {
            if (i > 0)
            {
                normal = MathF.Max(normal + stages[i].Start - stages[i - 1].Start, Done(normal, ref freeNormal, i - 1));
                reduced = MathF.Max(reduced + stages[i].ReducedStart - stages[i - 1].ReducedStart, Done(reduced, ref freeReduced, i - 1));
            }

            paced[i] = stages[i] with { Start = normal, ReducedStart = reduced };
        }

        return paced;

        // The moment the reader is through stage index, which started at start; it advances the reader's own clock.
        float Done(float start, ref float free, int index)
        {
            var need = ReadingSeconds(stages, index);
            if (need <= 0f)
                return start;
            free = MathF.Max(start, free) + need;
            return free;
        }
    }

    private static SWTrainingShot PaceShot(SWTrainingShot shot) =>
        shot.Stages == null ? shot : shot with { Stages = Paced(shot.Stages, BaseReadingSeconds(shot)) };

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

    private static SWTrainingChapter Ch(string id, string title, string goal, params SWTrainingShot[] shots) =>
        new(id, title, goal, shots.Select(PaceShot).ToArray());

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
            Staged("Зарплата начисляется в рабочий день", "Кто на смене в начале игрового часа рабочего дня, тому начисляется оклад на личный счёт.", "Баланс счёта видно у консоли аренды апартаментов.",
                SWTrainingSamples.ShiftPay()),
            Staged("Завершите смену", "Когда работа окончена, выберите «Сдать смену» на той же консоли.", "Повторное действие завершает смену; вставлять карту для переключения не требуется. После сдачи начисления за смену прекращаются.",
                SWTrainingSamples.ShiftEnd())),

        Ch("01Б", "Служебная связь", "Для пристава, гвардии и сенатского врача: какие каналы у вашей гарнитуры и кто вас слышит.",
            Staged("Гарнитура гвардии", "Осмотрите гарнитуру: в описании перечислены её каналы и клавиши.", "У офицера «Гвардия» и «Персонал Сената», у главы «Гвардия», «Сенат» и «Администрация Канцлера».",
                SWTrainingSamples.RadioKeys()),
            Staged("Пристав и врач", "У пристава все четыре служебных канала, у врача «Сенат» и «Персонал Сената».", "Общих каналов, которых нет в гарнитуре, у вас нет.",
                SWTrainingSamples.RadioOthers()),
            Staged("Канал гвардии", "Начните сообщение с «:в», чтобы сказать его по «Гвардии».", "Его слышат глава, офицеры и пристав.",
                SWTrainingSamples.RadioGuard()),
            Staged("Канал персонала", "Начните сообщение с «:е», чтобы сказать его по «Персоналу Сената».", "Его слышат врач, офицеры и пристав, но не глава гвардии.",
                SWTrainingSamples.RadioStaff()),
            Staged("Канал Сената", "Начните сообщение с «:ш», чтобы сказать его по «Сенату».", "Его слышат сенаторы и руководители, глава гвардии, врач и пристав; офицер гвардии нет.",
                SWTrainingSamples.RadioSenate())),

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

        Ch("02А", "Сенатор: дилеммы сектора", "Для сенатора: как решать дела своего сектора до срока.",
            Staged("Откройте дилемму", "На рабочем столе откройте «Дилеммы», раздел «Внебюджетные».", "Утренние дела сектора решаются до 14:00, дневные до 20:00.",
                SWTrainingSamples.DilemmaOpen()),
            Staged("Взвесьте варианты", "У каждого варианта свои последствия для сектора; на карточке видно и то, что будет при провале.", "Передать дело можно, только если в профильном министерстве есть министр.",
                SWTrainingSamples.DilemmaWeigh()),
            Staged("Примите решение", "Выбрать вариант может только сенатор этого сектора, днём и до срока.", "Решение сразу закрепляется, а подействует позже.",
                SWTrainingSamples.DilemmaChoose()),
            Staged("Не пропустите срок", "Если до срока ничего не выбрано, выбор закрывается.", "Провал наступает в час исполнения, и его последствия написаны на карточке. Пропущенная бюджетная дилемма штрафа не даёт: сектор просто не получает выгоды.",
                SWTrainingSamples.DilemmaDeadline()),
            Staged("Дождитесь исполнения", "Выбранный вариант применяется к сектору в час исполнения.", "Для утренних дел это 15:00.",
                SWTrainingSamples.DilemmaResult()),
            Staged("Бюджетные дилеммы", "Бюджетные дела оплачиваются из вашей доли бюджета сразу при выборе.", "Вариант дороже доступной суммы выбрать нельзя; в коррумпированном секторе цены выше.",
                SWTrainingSamples.DilemmaBudget())),

        Ch("03", "Законы и комитеты", "Для сенатора: путь проекта от предложения до голосования.",
            Staged("Откройте законопроект", "Вставьте карту сенатора с выбранным сектором и начните проект днём.", "Ночью подача недоступна.",
                SWTrainingSamples.BillOpen()),
            Staged("Задайте название и эффекты", "Введите название и добавьте хотя бы одно допустимое ненулевое изменение.", "Следите за выбранными эффектами и их стоимостью.",
                SWTrainingSamples.BillEdit()),
            Staged("Уложитесь в лимит", "Сумма эффектов не должна превышать доступный лимит.", "Если лимит превышен, исправьте проект.",
                SWTrainingSamples.BillLimit()),
            Staged("Отправьте проект", "У автора может быть только один активный законопроект.", "Крупный проект сначала смотрит профильный комитет, небольшой сразу ждёт повестки. Второй активный не принимается.",
                SWTrainingSamples.BillSubmit()),
            Staged("Следите за этапами", "Рассмотрение, голосование и подпись проходят отдельно.", "Проверяйте статус: закон действует только после подписи.",
                SWTrainingSamples.BillPathStages())),

        Ch("03А", "Сенатор: комитеты", "Для сенатора: как комитет рассматривает крупные законопроекты.",
            Staged("Вступите в комитет", "Откройте «Комитеты», вкладку «Все комитеты», и вступите в один из них.", "Первый вступивший становится председателем; мест в комитете три.",
                SWTrainingSamples.CommitteeJoin()),
            Staged("Проект пришёл в комитет", "В комитет попадает проект, который меняет показатели в сумме на 8 и больше и относится к его теме.", "Если в комитете никого нет, проект сразу идёт в повестку.",
                SWTrainingSamples.CommitteeReferral()),
            Staged("Дождитесь комитетского часа", "Голосовать по проекту можно с 08:00 до 09:00 и с 14:00 до 15:00.", "Решение принимает большинство членов комитета.",
                SWTrainingSamples.CommitteeHour()),
            Staged("Верните проект с причиной", "Во вкладке «Мои комитеты» впишите, что доработать, и выберите «Вернуть автору».", "Автор видит причину последнего голоса за возврат. Вернуть один проект можно не больше двух раз.",
                SWTrainingSamples.CommitteeReturn()),
            Staged("Отправьте на заседание", "Когда проект готов, голосуйте «На заседание».", "Большинство за передаёт проект на пленарное заседание.",
                SWTrainingSamples.CommitteeAdvance())),

        Ch("03Б", "Сенатор: заседание и голос", "Для сенатора: как голосовать на заседании и просить слово.",
            Staged("Откройте голосование", "Вставьте карту сенатора в консоль голосования и откройте вкладку «Голосование».", "Голосовать по проекту можно только во время его заседания.",
                SWTrainingSamples.VoteOpen()),
            Staged("Подайте голос", "Выберите «За», «Против» или «Воздержаться».", "Голосует только сенатор с закреплённым сектором; воздержавшийся не считается ни за, ни против.",
                SWTrainingSamples.VoteCast()),
            Staged("Попросите слово", "Нажмите на микрофон у своего места: вы встанете в очередь на слово.", "Говорить на весь Сенат можно, только когда спикер дал слово.",
                SWTrainingSamples.FloorRequest()),
            Staged("Голос можно изменить", "Пока голосование открыто, нажмите другую кнопку: прежний голос заменится.", "Во время ничьей голоса уже не меняются.",
                SWTrainingSamples.VoteChange()),
            Staged("Итог заседания", "Когда заседание кончается, проект принят, если ЗА больше, чем ПРОТИВ.", "Принятый проект ждёт подписи канцлера; при равенстве к концу заседания он отклонён.",
                SWTrainingSamples.VoteResult())),

        Ch("03В", "Сенатор: партии", "Для сенатора: как создать партию, вступить в неё и голосовать вместе.",
            Staged("Создайте партию", "На рабочем столе откройте «Политика», затем «Партии», впишите название и нажмите «Создать партию».", "Создатель становится лидером; состоять можно только в одной партии.",
                SWTrainingSamples.PartyCreate()),
            Staged("Подайте заявку", "Во вкладке «Все партии» откройте партию кнопкой «Подробнее» и подайте заявку на вступление.", "Пока лидер не ответил, заявку можно отозвать.",
                SWTrainingSamples.PartyJoin()),
            Staged("Примите новых членов", "Лидер видит заявки во вкладке «Моя партия» и нажимает «Принять» или «Отклонить».", "При трёх членах партия сформирована; если их станет меньше, у неё около двух часов на замену.",
                SWTrainingSamples.PartyAccept()),
            Staged("Следите за сплочённостью", "Когда закон решён, сплочённость сформированной партии растёт на 1, если больше проголосовавших членов поддержали лидера, и падает на 1, если больше пошли против.", "Без голоса лидера, при равенстве или после партийного голосования она не меняется. На -5 она обнуляется, и партия голосует за случайно выдвинутого члена в лидеры: сменит лидера только большинство.",
                SWTrainingSamples.PartyCohesion()),
            Staged("Партийное голосование", "Со сплочённостью +4 лидер, уже подавший голос, может нажать «ОБЪЯВИТЬ ПАРТИЙНЫМ».", "Все члены голосуют как лидер, а сплочённость сбрасывается до нуля. На выборах кнопки нет.",
                SWTrainingSamples.PartyWhip())),

        Ch("03Г", "Выборы Верховного Канцлера", "Для сенаторов, спикера и канцлера: когда Сенат выбирает канцлера и как избранный вступает в должность.",
            Staged("Когда начинаются выборы", "Если нет ни канцлера, ни спикера, через игровой час Сенат сам назначает выборы на два часа.", "Нужно не меньше двух занятых секторов. После отстранения канцлера выборы идут и при спикере.",
                SWTrainingSamples.ElectionCall()),
            Staged("Выдвиньте себя", "Во время заседания нажмите «Выдвинуть свою кандидатуру» в консоли голосования.", "Выдвинуться можно один раз; отстранённый канцлер выдвинуться не может.",
                SWTrainingSamples.ElectionNominate()),
            Staged("Голосуйте за кандидата", "Нажмите «Голосовать» у выбранного кандидата.", "Голос можно отдать другому кандидату, пока выборы идут.",
                SWTrainingSamples.ElectionVote()),
            Staged("Итог выборов", "Выборы кончаются сразу, когда у кандидата большинство занятых секторов или когда проголосовали все сектора и впереди один кандидат.", "Иначе в конце заседания побеждает набравший больше голосов, а при равенстве тот, кто выдвинулся раньше. Без голосов выборы не состоялись.",
                SWTrainingSamples.ElectionResult()),
            Staged("Подтвердите избрание", "Избранный днём вставляет свою карту сенатора в рабочую консоль и в разделе «Должность» нажимает «Подтвердить избрание».", "Сектор избранного освобождается. Потерявший сектор до подтверждения теряет и избрание.",
                SWTrainingSamples.ElectionConfirm())),

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

        Ch("04А", "Спикер и канцлер: трибуна и ничья", "Для спикера и канцлера: очередь на слово, завершение голосования и решающий голос.",
            Staged("Откройте трибуну", "Свой микрофон спикер включает и выключает сам. Очередь на слово видна во вкладке «Трибуна».", "Вкладку видит только ведущий заседание: спикер, а без спикера канцлер.",
                SWTrainingSamples.FloorMic()),
            Staged("Дайте слово", "Нажмите «Дать слово» у сенатора в очереди.", "«Убрать» снимает сенатора из очереди без слова; «Слово канцлеру» даёт слово канцлеру.",
                SWTrainingSamples.FloorGrant()),
            Staged("Заберите слово", "Когда речь окончена, нажмите «Забрать слово».", "Пока слово ни у кого, на весь Сенат слышен только спикер с включённой трибуной.",
                SWTrainingSamples.FloorRevoke()),
            Staged("Завершите голосование", "Кнопка «Завершить голосование» появляется, когда проголосовали все или перевес уже не переломить.", "При равенстве голосов проект ждёт решающего голоса канцлера; если его нет до конца заседания, проект отклонён.",
                SWTrainingSamples.TieEnd()),
            Staged("Решающий голос", "Канцлер выбирает «РЕШАЮЩИЙ: ЗА» или «РЕШАЮЩИЙ: ПРОТИВ».", "Без канцлера решающий голос у спикера. Для особых предложений с порогом «Нужно ЗА» этого порядка нет.",
                SWTrainingSamples.TieBreak())),

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

        Ch("07А", "Оборона: флот на границе", "Для министра обороны: как отправить флот прикрыть пограничный переход.",
            Staged("Выберите флот", "Во вкладке «Флот» выберите свой флот на карте или в списке.", "Под кнопкой приказа видны место флота, его сила, урон, снабжение и подготовка.",
                SWTrainingSamples.FleetSelect()),
            Staged("Выберите переход", "Нажмите на карте пограничный мир: рядом появится переход и его состояние.", "Без задачи флот просто переместится; прикрытие перехода выбирается отдельно.",
                SWTrainingSamples.FleetTarget()),
            Staged("Задача «Граница»", "В «Задаче по прибытии» выберите «Граница»: кнопка станет «Прикрыть переход».", "Нужны министр на службе, рабочий день, разрешённый маршрут, снабжение от 15 и деньги на платный приказ.",
                SWTrainingSamples.FleetBorder()),
            Staged("Отправьте флот", "Нажмите «Прикрыть переход»: флот готовится и уходит.", "В пути можно сменить курс или вернуть флот на базу.",
                SWTrainingSamples.FleetSend()),
            Staged("Флот на переходе", "На месте флот прикрывает переход, а состояние показывает, хватает ли прикрытия.", "Прикрытие не обещает победы: при нападении всё решают силы, снабжение и подготовка.",
                SWTrainingSamples.FleetPost())),

        Ch("07Б", "Оборона: защита миров", "Для министра обороны: как ответить на запрос губернатора и построить планетарную оборону.",
            Staged("Запросы губернаторов", "Откройте «Оборона миров»: слева запросы губернаторов.", "В запросе видны мир, губернатор и причина.",
                SWTrainingSamples.WorldRequests()),
            Staged("Выберите мир", "Нажмите на запрос, чтобы открыть его мир.", "Это ещё не решение: запрос ничего не строит и не утверждает.",
                SWTrainingSamples.WorldSelect()),
            Staged("Начните строительство", "В карточке программы нажмите «Начать строительство».", "Смета списывается сразу; нужны рабочий день, министр на службе и достаточный бюджет.",
                SWTrainingSamples.WorldBuild()),
            Staged("Следите за работами", "Сначала доставляются материалы, затем идут работы.", "Оборона действует только после завершения, и её готовность зависит от снабжения.",
                SWTrainingSamples.WorldProgress()),
            Staged("Материальный резерв", "Когда оборона построена, закажите материальный резерв на случай перебоев.", "Заказать можно, только если резерв исчерпан и прежний заказ не ждёт поставки.",
                SWTrainingSamples.WorldReserve())),

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

        Ch("08А", "Безопасность: расследования", "Для министра внутренней безопасности: как довести дело до конца.",
            Staged("Откройте дело", "Во вкладке «Расследования» выберите дело.", "В досье видны место, этап и сколько улик уже собрано.",
                SWTrainingSamples.CaseOpen()),
            Staged("Выберите группу", "В разделе «Исполнитель» выберите группу; маршрут видно в «Показать на карте».", "Пока группа не выбрана, приказ недоступен; делом занимается только одна группа.",
                SWTrainingSamples.CaseTeam()),
            Staged("Соберите улику", "Нажмите «Собрать улику»: группа летит на место и работает.", "Каждая работа расходует снабжение, а с меньшим снабжением работы идут дольше.",
                SWTrainingSamples.CaseEvidence()),
            Staged("Завершите дело", "Вторую улику соберите так же, а когда улик хватает, нажмите «Завершить дело».", "Риск потери группы виден заранее; при провале улики сохраняются.",
                SWTrainingSamples.CaseFinish()),
            Staged("Диверсия по имперскому делу", "По имперскому делу можно запросить одобрение диверсии, а после одобрения запустить её.", "Запрос ничего не стоит, запуск стоит 1200 кр. Исход неизвестен до конца операции.",
                SWTrainingSamples.CaseSabotage())),

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

        Ch("09А", "Экономика: аварии производства", "Для министра экономического развития: как вернуть пострадавшую линию.",
            Staged("Найдите аварию", "В «Программах» откройте «Потрясения»: там текущая авария и её последствия.", "Видно, на сколько упала мощность и сколько осталось до естественного восстановления.",
                SWTrainingSamples.IncidentOpen()),
            Staged("Оплатите ремонт", "Нажмите «Стандартное восстановление»: 600 кр из бюджета министерства.", "Нужны утверждённый бюджет дня, рабочее время, деньги и материалы, которые успеют доехать.",
                SWTrainingSamples.IncidentStandard()),
            Staged("Доставка и работы", "Сначала везут механические узлы и электронику, затем два часа идёт ремонт.", "Ход виден в разделе «Выполнение и поставки».",
                SWTrainingSamples.IncidentDelivery()),
            Staged("Итог ремонта", "Когда работы закончены, авария уходит в «Последние потрясения».", "Оплата не гарантирует успех: без транспорта или маршрута материалы могут не успеть.",
                SWTrainingSamples.IncidentResult()),
            Staged("Ускоренное восстановление", "Если времени мало, выберите ускоренное восстановление за 1 050 кр.", "Оно везёт ещё промышленное оборудование, зато работы идут один час.",
                SWTrainingSamples.IncidentFast())),

        Ch("09Б", "Экономика: политика и стимул", "Для министра экономического развития: как управлять ростом и перегревом.",
            Staged("Прочитайте цикл", "Во вкладке «Экономика» видны темп роста, перегрев и текущая политика.", "Перегрев растёт, когда экономику разгоняют слишком долго.",
                SWTrainingSamples.PolicyRead()),
            Staged("Стимул", "Курс «Стимул» ускоряет рост, но разогревает экономику.", "Политика меняется сразу, показатели сдвигаются каждый час.",
                SWTrainingSamples.PolicyStimulus()),
            Staged("Сдерживание", "Курс «Сдерживание» замедляет рост и снижает перегрев.", "При перегреве 95 пузырь лопается, и начинается рецессия.",
                SWTrainingSamples.PolicyRestrain()),
            Staged("Запросите большой стимул", "Нажмите «Запросить большой стимул»: рост подскочит на 4, перегрев на 30.", "Решает канцлер, без канцлера спикер; если нет обоих, стимул запускается сразу.",
                SWTrainingSamples.StimulusRequest()),
            Staged("Решение канцлера", "Канцлер одобряет или отклоняет запрос в «Согласованиях».", "Стимул не решает кризис: он разгоняет рост ценой перегрева.",
                SWTrainingSamples.StimulusDecision())),

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

        Ch("10А", "Финансы: запросы секторов", "Для министра финансов: как помочь губернатору деньгами из резерва.",
            Staged("Откройте запросы", "Во вкладке «Резерв» откройте «Запросы секторов».", "В запросе видны сектор, мир, губернатор, проект и недостающая сумма.",
                SWTrainingSamples.ReserveOpen()),
            Staged("Проверьте условия", "Сравните недостающую сумму с остатком резерва.", "Выделить можно днём, министру на службе, при утверждённом бюджете дня и достаточном резерве.",
                SWTrainingSamples.ReserveRead()),
            Staged("Выделите средства", "Нажмите «Выделить средства».", "Деньги списываются, только если проект ещё нужен и средств хватает. Если условия изменились, обращение закрывается без списания.",
                SWTrainingSamples.ReserveApprove()),
            Staged("Заказ, а не готовый цех", "После решения проект заказан, а перевод виден в «Переводы и история».", "Мощность вырастет только после доставки и монтажа.",
                SWTrainingSamples.ReserveOrder()),
            Staged("Или откажите", "Нажмите «Отказать», если проект не нужен.", "Этот губернатор обратится снова не раньше следующего дня.",
                SWTrainingSamples.ReserveReject())),

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

        Ch("12Б", "Здравоохранение: карантин", "Для министра здравоохранения: как закрыть очаг и не оставить мир без помощи.",
            Staged("Выберите зону", "Во вкладке «Карантин» выберите заражённый мир и масштаб: мир, система или весь сектор.", "Ниже видны охват медпомощью и запасы пищи и лекарств в зоне.",
                SWTrainingSamples.QuarantineZone()),
            Staged("Срок и коридор", "Выберите 2, 4 или 6 игровых часов и решите, оставить ли гуманитарный коридор.", "С открытым коридором пища и лекарства продолжают поступать; конвои Минздрава проходят всегда.",
                SWTrainingSamples.QuarantineTerms()),
            Staged("Отправьте запрос", "Нажмите «Запросить карантин».", "Запрос решает канцлер, без канцлера спикер; если нет обоих, он утверждается сразу. Свой запрос утвердить нельзя.",
                SWTrainingSamples.QuarantineRequest()),
            Staged("Решение канцлера", "Канцлер одобряет или отклоняет запрос в «Согласованиях».", "Срок начинается с одобрения, обычные перевозки встают.",
                SWTrainingSamples.QuarantineDecision()),
            Staged("Снимите карантин", "Когда очаг сбит, нажмите «Снять карантин»; иначе он закончится по сроку.", "Карантин не лечит: он только сдерживает распространение.",
                SWTrainingSamples.QuarantineLift())),

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

        Ch("13А", "Здравоохранение: склады и выезд", "Для главы департамента здравоохранения: как пополнить склады, чтобы бригада смогла выехать.",
            Staged("Вызов без запасов", "Во вкладке «Вызовы» видно, хватает ли запасов на выезд.", "Обычному выезду нужно 10 запасов, усиленному 15 и 500 кр, обоим нужна свободная бригада и неистёкший вызов.",
                SWTrainingSamples.SupplyOpen()),
            Staged("Закупите медикаменты", "Во вкладке «Операции» нажмите «Выполнить» у закупки медикаментов за 800 кр.", "Повторить закупку можно через два активных игровых часа.",
                SWTrainingSamples.SupplyBuy()),
            Staged("Проверьте склады", "Запасы растут на 30 сразу.", "Больше 100 они не поднимаются.",
                SWTrainingSamples.SupplyStock()),
            Staged("Отправьте бригаду", "Вернитесь к вызову и отправьте бригаду, пока он не сгорел.", "Выезд расходует запасы и готовность; бригада занята час.",
                SWTrainingSamples.SupplyDispatch()),
            Staged("Итог выезда", "Результат выезда появится в журнале департамента.", "Шанс успеха обычного выезда зависит от готовности и запасов; усиленный надёжен.",
                SWTrainingSamples.SupplyOutcome())),

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

        Ch("16А", "Учёт расходов: сигналы", "Для главы департамента учёта расходов: как работать с сигналами о нарушениях.",
            Staged("Новый сигнал", "Во вкладке «Сигналы» появляется срочный сигнал с оставшимся временем.", "Иногда нарушитель сразу предлагает деньги, чтобы сигнал закрыли.",
                SWTrainingSamples.TipOpen()),
            Staged("Проверьте сигнал", "Нажмите «Проверить сигнал»: 300 кр из бюджета департамента.", "Подтверждение вернёт в бюджет от 600 до 1 400 кр; бывает, что сигнал не подтверждается.",
                SWTrainingSamples.TipCheck()),
            Staged("Прозрачность", "На вкладке «Работа» видна прозрачность бюджета и возвращённые деньги.", "Подтверждённый сигнал даёт +3, неподтверждённый +1.",
                SWTrainingSamples.TipTransparency()),
            Staged("Предложение скрыть", "Кнопка «Скрыть нарушение» закрывает сигнал без проверки и платит лично.", "Взятка снижает прозрачность на 5 и повышает личную коррупцию.",
                SWTrainingSamples.TipBribe()),
            Staged("Сигнал остыл", "Сигнал живёт около трёх часов.", "Если не проверить его вовремя, след остынет и прозрачность упадёт на 2.",
                SWTrainingSamples.TipCold())),

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

        Ch("18А", "Вооружения: лицензии и дроиды", "Для главы бюро регулирования вооружений: как работать с лицензиями и реестром дроидов.",
            Staged("Выберите режим", "На вкладке «Арсенал» задайте каждой категории режим: запрет, лицензии или свободно.", "Заявки и лицензионные сборы бывают только в категориях с лицензиями.",
                SWTrainingSamples.LicenseOpen()),
            Staged("Проверьте заявку", "Во вкладке «Лицензии и дроиды» нажмите «Проверка» у заявки.", "Проверка стоит 300 кр и делается один раз; после неё виден риск заявителя.",
                SWTrainingSamples.LicenseInspect()),
            Staged("Решите по заявке", "Одобрите заявку за пошлину или откажите.", "Одобрение сомнительного или подставного заявителя может увести оружие на чёрный рынок.",
                SWTrainingSamples.LicenseDecide()),
            Staged("Распределите дроидов", "Партию дроидов из резерва можно передать Обороне или придать МВБ.", "МВБ держит не больше пяти партий и может вернуть их в резерв; при пустом резерве передавать нечего.",
                SWTrainingSamples.DroidTransfer()),
            Staged("Проверьте итог", "Журнал бюро и реестр показывают, куда ушли партии.", "Переданная Обороне партия в резерв бюро не возвращается.",
                SWTrainingSamples.DroidResult())),

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

        Ch("20Б", "Суд: конституционный надзор", "Для Верховного Судьи и судей: как проверить действующий закон на конституционность.",
            Staged("Откройте законы", "Днём на вкладке «Законы» судебной консоли видны действующие законы, которые можно проверить.", "Открыть проверку может только Верховный Судья; остальные судьи только голосуют.",
                SWTrainingSamples.ReviewOpen()),
            Staged("Начните проверку", "Выберите закон и нажмите «Открыть конституционную проверку».", "Одновременно идёт только одна проверка, а закон пока продолжает действовать.",
                SWTrainingSamples.ReviewStart()),
            Staged("Голосуйте", "Каждый судья выбирает «Признать неконституционным» или «Подтвердить конституционность».", "Голосовать можно днём, пока не прошли два активных игровых часа.",
                SWTrainingSamples.ReviewVoting()),
            Staged("Итог проверки", "Когда проголосовали все судьи, решение принимается сразу: большинством, а при равенстве голосом Верховного Судьи.", "Итог виден по списку законов у Верховного Судьи: отменённый закон из него исчезает. Только решение о неконституционности отменяет закон.",
                SWTrainingSamples.ReviewResult()),
            Staged("Срок проверки", "Если голосов не хватает, проверка закрывается по истечении срока.", "Без голоса Верховного Судьи равенство голосов оставляет закон в силе.",
                SWTrainingSamples.ReviewDeadline())),

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

        Ch("24А", "Корреспондент: исправление и отзыв", "Для корреспондента Голонета: как исправить или отозвать свою опубликованную статью.",
            Staged("Откройте статью", "В «Ленте» своего издания выберите свою статью.", "Исправить и отозвать можно только свою текущую публикацию.",
                SWTrainingSamples.PubOpen()),
            Staged("Исправьте текст", "Поменяйте текст и нажмите «Исправить».", "Выходит новая версия, цитаты остаются прежними. Чтобы править или отзывать дальше, выберите статью в ленте заново.",
                SWTrainingSamples.PubCorrect()),
            Staged("История версий", "Кнопка «История» показывает все версии статьи по порядку.", "Читатели тоже видят, что статью исправляли.",
                SWTrainingSamples.PubHistory()),
            Staged("Чужая и устаревшая статья", "Чужую публикацию можно только читать.", "Если версия сменилась, пока статья была открыта, откройте её заново.",
                SWTrainingSamples.PubLimits()),
            Staged("Отзыв", "Если материал уже не спасти правкой, выберите его в ленте и нажмите «Отозвать».", "Отзыв окончателен: после него статью не исправить и историю не открыть.",
                SWTrainingSamples.PubWithdraw())),

        Ch("25", "Пристав Сената", ShiftGoal,
            Staged("Вы — пристав Сената", "Ваша карта открывает доступ Сената и гвардии.", "Проверьте доступ и гарнитуру.", SWTrainingSamples.UsherStart()),
            Staged("Проверьте пояс", "Пристав начинает с заполненным поясом охраны.", "В нём дубинка, две пары наручников, голопроектор барьеров и ручная рация.", SWTrainingSamples.UsherBelt()),
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
            new SWTrainingShot("Оцените травму", "Осмотрите пациента и определите вид повреждения.", "Пациента ударило дверью шлюза: похоже на механическую травму.", SWTrainingSamples.Examining(1), SWTrainingSamples.PatientCheck()),
            new SWTrainingShot("Примените набор для ушибов", "При механической травме примените набор для ушибов к пациенту.", "Одно применение к другому персонажу занимает 3 секунды; движение его прерывает.", SWTrainingSamples.Treating(3), SWTrainingSamples.HealingUse()),
            new SWTrainingShot("Осмотрите повторно", "Проверьте состояние после помощи и при необходимости продолжайте лечение.", "Описание может не измениться. При других повреждениях потребуется подходящее средство.", SWTrainingSamples.Examining(1), SWTrainingSamples.PatientRecheck())),
    };

    // ---- occupations ------------------------------------------------------

    public static readonly SWTrainingRole[] Roles =
    {
        new("general", null, "Общий выпуск для всех ролей", new[] { "01" }),
        new("senator", "SWSenator", "Сенатор", new[] { "01", "02", "02А", "03", "03А", "03Б", "03В", "03Г", "20А" }),
        new("speaker", "SWSpeaker", "Спикер Сената", new[] { "01", "04", "04А", "03Г" }),
        new("chancellor", "SWChancellor", "Верховный канцлер", new[] { "01", "05", "04А", "05А", "03Г", "20А" }),
        new("minister", "SWMinister", "Министр", new[] { "01", "06", "05А", "07", "07А", "07Б", "08", "08А", "09", "09А", "09Б", "10", "10А", "11", "12", "12А", "12Б", "13", "13А", "14", "15", "16", "16А", "17", "18", "18А", "19" }),
        new("chief-justice", "SWChiefJustice", "Верховный судья", new[] { "01", "20", "20Б" }),
        new("judge", "SWRepublicJudge", "Судья Республики", new[] { "01", "21", "20А", "20Б" }),
        new("prosecutor", "SWProsecutor", "Обвинитель Республики", new[] { "01", "22" }),
        new("defender", "SWDefender", "Защитник Республики", new[] { "01", "23" }),
        new("correspondent", "SWHolonetCorrespondent", "Корреспондент Голонета", new[] { "01", "24", "24А" }),
        new("usher", "SWUsher", "Пристав Сената", new[] { "01", "01А", "01Б", "25" }),
        new("guard-head", "SWSenateGuardHead", "Глава Сенатской гвардии", new[] { "01", "01А", "01Б", "26" }),
        new("guard-officer", "SWSenateGuardOfficer", "Офицер Сенатской гвардии", new[] { "01", "01А", "01Б", "27" }),
        new("doctor", "SWSenateDoctor", "Сенатский врач", new[] { "01", "01А", "01Б", "28" }),
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
