using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Core;

public sealed class SWBureauTransitSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWMinistryInternalSecuritySystem _security = default!;
    [Dependency] private readonly SWMinistryHealthSystem _health = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;

    public const string BureauId = "SWBureauTransit";

    public const int StatusOpen = 0;
    public const int StatusVisa = 1;
    public const int StatusCordon = 2;

    private const float TickInterval = 1f;
    private const float DilInterval = 2f;
    private const float EventLife = 2f;
    private const int EventCap = 3;
    private const float SafetyDecay = 0.8f;

    private float _mobility = 50f;
    private float _congestion = 30f;
    private float _safety = 70f;
    private readonly Dictionary<string, int> _status = new();

    private readonly List<ActiveEvent> _events = new();
    private int _nextEventId = 1;
    private readonly SWSenateWorkQueue _arrivalQueue = new(.75f, 1f);

    private readonly Dictionary<int, double> _opCooldowns = new();
    private readonly List<string> _log = new();
    private float _income;

    private int _dilIndex = -1;
    private readonly SWSenateWorkQueue _dilemmaQueue = new(3f, 4f, 1.5f, 2.5f);
    private float _dilLife;

    private int _day = -1;
    private float _accum;

    public int Mobility => (int) MathF.Round(_mobility);
    public int Congestion => (int) MathF.Round(_congestion);
    public int Safety => (int) MathF.Round(_safety);

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _accum = 0f;
        _mobility = 50f;
        _congestion = 30f;
        _safety = 70f;
        _status.Clear();
        _events.Clear();
        _nextEventId = 1;
        _arrivalQueue.Reset();
        _opCooldowns.Clear();
        _log.Clear();
        _income = 0f;
        _dilIndex = -1;
        _dilemmaQueue.Reset();
        _dilLife = 0f;
        _day = -1;
    }

    private void AddLog(string text)
    {
        _log.Insert(0, $"[{_time.CurrentHour:00}:00] {text}");
        if (_log.Count > 8)
            _log.RemoveRange(8, _log.Count - 8);
    }

    public List<string> GetLog() => new(_log);

    private void AddMobility(float d) => _mobility = Math.Clamp(_mobility + d, 0f, 100f);
    private void AddCongestion(float d) => _congestion = Math.Clamp(_congestion + d, 0f, 100f);
    private void AddSafety(float d) => _safety = Math.Clamp(_safety + d, 0f, 100f);

    private void AddBudget(int amount) =>
        _dilemma.SetMinistryBudget(BureauId, Math.Max(0, _dilemma.GetMinistryBudget(BureauId) + amount));

    public string MobilityStatus() => _mobility switch
    {
        >= 75f => "Галактика в движении",
        >= 55f => "Оживлённые линии",
        >= 35f => "Редкие рейсы",
        _ => "Линии стоят",
    };

    public string CongestionStatus() => _congestion switch
    {
        >= 75f => "Коллапс узлов",
        >= 55f => "Заторы",
        >= 30f => "Рабочая нагрузка",
        _ => "Свободно",
    };

    public string SafetyStatus() => _safety switch
    {
        >= 70f => "Линии безопасны",
        >= 40f => "Тревожные сводки",
        _ => "Полоса катастроф",
    };

    public int TicketIncome()
    {
        var open = ClaimedStatuses().Count(kv => kv.Value == StatusOpen);
        return (int) MathF.Round(_mobility * 0.6f + open * 12f);
    }

    public int GetStatus(string sectorId) => _status.TryGetValue(sectorId, out var s) ? s : StatusOpen;

    public bool SetStatus(string sectorId, int status)
    {
        if (!_senate.IsClaimed(sectorId) || status is < StatusOpen or > StatusCordon)
            return false;

        var old = GetStatus(sectorId);
        if (old == status)
            return true;

        _status[sectorId] = status;
        var name = SectorName(sectorId);

        if (status == StatusCordon)
            AddLog($"Кордон: {name} закрыт для перемещений.");
        else if (old == StatusCordon)
            AddLog($"Кордон снят: {name} снова принимает рейсы.");
        else
            AddLog($"{name}: режим «{StatusLabel(status)}».");

        return true;
    }

    public static string StatusLabel(int status) => status switch
    {
        StatusVisa => "Визовый режим",
        StatusCordon => "Кордон",
        _ => "Открыт",
    };

    private List<KeyValuePair<string, int>> ClaimedStatuses() =>
        _senate.SectorHolders.Keys.Select(id => new KeyValuePair<string, int>(id, GetStatus(id))).ToList();

    private string SectorName(string sectorId) =>
        _proto.TryIndex<SWSectorPrototype>(sectorId, out var proto) ? proto.Name : sectorId;

    // --- Sector board ---

    public List<SWTransitSectorEntry> BuildSectors()
    {
        var list = new List<SWTransitSectorEntry>();
        var unrest = _security.Unrest;

        foreach (var (sectorId, _) in _senate.SectorHolders)
        {
            if (!_proto.TryIndex<SWSectorPrototype>(sectorId, out var proto))
                continue;

            var status = GetStatus(sectorId);
            var flags = new List<string>();

            var infection = _health.PathogenActive ? _health.SectorInfection(sectorId) : 0f;
            if (infection >= 5f)
                flags.Add($"заражение {(int) infection}%");
            if (_health.IsQuarantined(sectorId))
                flags.Add("карантин МЗ");
            if (unrest >= 55 && status == StatusOpen)
                flags.Add("волнения на узлах");

            list.Add(new SWTransitSectorEntry
            {
                Id = sectorId,
                Name = proto.Name,
                Color = proto.Color,
                Status = status,
                FlagsLabel = string.Join(", ", flags),
            });
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        return list;
    }

    // --- Line events ---

    private readonly record struct EvtOpt(string Label, string Desc, int Cost, int Bribe, (string Name, float Delta)[] Show);
    private readonly record struct EvtDef(string Name, string Desc, EvtOpt[] Options);

    private static readonly EvtDef[] Events =
    {
        new("Застрявший лайнер",
            "Пассажирский лайнер с пятью тысячами пассажиров потерял ход между системами. Запаса топлива хватит на сутки жизнеобеспечения.",
            new[]
            {
                new EvtOpt("Выслать буксир", "Спасательная операция за счёт бюро.", 600, 0,
                    new[] { ("Мобильность", 3f), ("Мораль", 1f) }),
                new EvtOpt("Оставить в ожидании", "Сигнал бедствия передан ближайшим рейсам без выделения ресурсов бюро.", 0, 0,
                    new[] { ("Мораль", -2f), ("Мобильность", -2f) }),
            }),
        new("Свадебный караван",
            "Два знатных дома с Альдераана просят приоритетный коридор для свадебного каравана в сотню яхт.",
            new[]
            {
                new EvtOpt("Приоритетный коридор", "Повысит престиж, но перегрузит узлы.", 0, 0,
                    new[] { ("Мораль", 2f), ("Загруженность", 3f) }),
                new EvtOpt("В общую очередь", "Караван будет обработан по общему регламенту.", 0, 0,
                    new[] { ("Загруженность", 1f) }),
            }),
        new("Прорыв карантина",
            "Чартер пытается вылететь из заражённого сектора в обход санитарного контроля.",
            new[]
            {
                new EvtOpt("Перехватить", "Патруль вернёт нарушителя на стоянку.", 400, 0,
                    new[] { ("Безопасность", 3f), ("Угроза ВБ", -1f) }),
                new EvtOpt("Пропустить", "Передать ситуацию медицинским службам без вмешательства бюро.", 0, 0,
                    new[] { ("Заражение", 4f), ("Угроза ВБ", 2f) }),
            }),
        new("Чартер без манифеста",
            "Частный чартер запрашивает транзит без списка пассажиров. Капитан избегает уточнений.",
            new[]
            {
                new EvtOpt("Полный досмотр", "Задержать и проверить всех на борту.", 300, 0,
                    new[] { ("Угроза ВБ", -2f) }),
                new EvtOpt("Пропустить за подношение", "Капитан очень убедителен.", 0, 900,
                    new[] { ("Угроза ВБ", 2f) }),
            }),
        new("Диверсия на узле",
            "Неизвестные вывели из строя диспетчерский узел пересадочной станции.",
            new[]
            {
                new EvtOpt("Ремонтная бригада", "Полное восстановление и проверка.", 800, 0,
                    new[] { ("Безопасность", 6f) }),
                new EvtOpt("Временная заплатка", "Держится на честном слове.", 0, 0,
                    new[] { ("Безопасность", -6f) }),
            }),
        new("Звёздный цирк Умбары",
            "Гастрольный флот цирка просит окно в расписании. Дети трёх секторов ждут.",
            new[]
            {
                new EvtOpt("Выделить окно", "Праздник для всей Республики.", 0, 0,
                    new[] { ("Мораль", 3f), ("Загруженность", 4f) }),
                new EvtOpt("Отказать", "Расписание не резиновое.", 0, 0,
                    new[] { ("Мораль", -1f) }),
            }),
        new("Давка на пересадочном узле",
            "Задержки рейсов собрали на станции толпу. Люди спят на полу третьи сутки.",
            new[]
            {
                new EvtOpt("Сверхурочные смены", "Разогнать очередь за счёт бюро.", 500, 0,
                    new[] { ("Загруженность", -8f), ("Мораль", 1f) }),
                new EvtOpt("Оставить в очереди", "Очередь будет обработана в штатном порядке.", 0, 0,
                    new[] { ("Загруженность", 4f), ("Мораль", -2f) }),
            }),
        new("Опаздывающий сенатор",
            "Сенатор требует коридор вне очереди - голосование начнётся в ближайшее время.",
            new[]
            {
                new EvtOpt("Коридор вне очереди", "Сектор сенатора оценит услугу.", 0, 0,
                    new[] { ("Загруженность", 2f), ("Сектор", 2f) }),
                new EvtOpt("В общий поток", "Перед реестром все равны.", 0, 0,
                    new[] { ("Сектор", -2f) }),
            }),
        new("След работорговцев",
            "Диспетчеры заметили борт с глушёными отсеками жизнеобеспечения. Похоже на живой груз.",
            new[]
            {
                new EvtOpt("Совместный рейд с ВБ", "Перехватить и освободить.", 600, 0,
                    new[] { ("Угроза ВБ", -4f), ("Мораль", 2f) }),
                new EvtOpt("Зафиксировать без вмешательства", "Бюро фиксирует инцидент без вмешательства.", 0, 0,
                    new[] { ("Угроза ВБ", 3f), ("Права", -2f) }),
            }),
        new("Гонщики на гиперлинии",
            "Свуп-банда устроила гонки прямо на пассажирской трассе.",
            new[]
            {
                new EvtOpt("Оштрафовать и конфисковать", "Штрафы пополнят казну бюро.", 0, 0,
                    new[] { ("Казна бюро", 400f), ("Безопасность", 2f) }),
                new EvtOpt("Отвернуться", "Молодость бывает раз в жизни.", 0, 0,
                    new[] { ("Безопасность", -4f) }),
            }),
    };

    public List<SWTransitEventEntry> BuildEvents()
    {
        var budget = _dilemma.GetMinistryBudget(BureauId);
        var list = new List<SWTransitEventEntry>();

        foreach (var active in _events)
        {
            var def = Events[active.DefIndex];
            var entry = new SWTransitEventEntry
            {
                Id = active.Id,
                Name = def.Name,
                Description = def.Desc,
                TimeLabel = $"Решить в течение ~{MathF.Max(0f, active.LifeHours):0.0} ч",
            };

            foreach (var opt in def.Options)
            {
                var oe = new SWBureauDilemmaOptionEntry
                {
                    Label = opt.Bribe > 0 ? $"{opt.Label} (+{opt.Bribe} кр лично)" : opt.Label,
                    Description = opt.Desc,
                    Cost = opt.Cost,
                    Affordable = opt.Cost <= budget,
                    Corrupt = opt.Bribe > 0,
                };
                foreach (var (name, delta) in opt.Show)
                    oe.Effects.Add(new SWBillEffectEntry { StatName = name, Color = ShowColor(name, delta), Delta = delta });
                entry.Options.Add(oe);
            }

            list.Add(entry);
        }

        return list;
    }

    public bool ResolveEvent(int eventId, int optionIndex)
    {
        var active = _events.FirstOrDefault(e => e.Id == eventId);
        if (active == null)
            return false;

        var def = Events[active.DefIndex];
        if (optionIndex < 0 || optionIndex >= def.Options.Length)
            return false;

        var opt = def.Options[optionIndex];
        if (opt.Cost > 0 && !_dilemma.SpendMinistry(BureauId, opt.Cost))
            return false;

        if (opt.Bribe > 0)
        {
            if (_ministry.GetHolder(BureauId) is not { } holder)
                return false;
            _corruption.TakeBribe(holder, active.SectorId, opt.Bribe);
        }

        _events.Remove(active);
        ApplyEvent(active, optionIndex);
        return true;
    }

    private void ApplyEvent(ActiveEvent active, int opt)
    {
        switch (active.DefIndex, opt)
        {
            case (0, 0):
                AddMobility(3f);
                _senate.AddPolicy("SWStatMorale", 1f);
                AddLog("Буксир дотащил лайнер до узла. Пассажиры аплодируют.");
                break;
            case (0, 1):
                _senate.AddPolicy("SWStatMorale", -2f);
                AddMobility(-2f);
                break;

            case (1, 0):
                _senate.AddPolicy("SWStatMorale", 2f);
                AddCongestion(3f);
                AddLog("Свадебный караван прошёл с эскортом. Голонет в восторге.");
                break;
            case (1, 1):
                AddCongestion(1f);
                break;

            case (2, 0):
                AddSafety(3f);
                _security.AddThreat(-1f);
                AddLog("Нарушитель карантина перехвачен на выходе из сектора.");
                break;
            case (2, 1):
                if (_health.PathogenActive && active.SectorId != null)
                    _health.AddInfection(active.SectorId, 4f);
                _security.AddThreat(2f);
                break;

            case (3, 0):
                _security.AddThreat(-2f);
                break;
            case (3, 1):
                _security.AddThreat(2f);
                AddLog("Чартер без манифеста растворился на линиях.");
                break;

            case (4, 0):
                AddSafety(6f);
                AddLog("Диспетчерский узел восстановлен и укреплён.");
                break;
            case (4, 1):
                AddSafety(-6f);
                break;

            case (5, 0):
                _senate.AddPolicy("SWStatMorale", 3f);
                AddCongestion(4f);
                AddLog("Звёздный цирк гастролирует по открытым секторам.");
                break;
            case (5, 1):
                _senate.AddPolicy("SWStatMorale", -1f);
                break;

            case (6, 0):
                AddCongestion(-8f);
                _senate.AddPolicy("SWStatMorale", 1f);
                break;
            case (6, 1):
                AddCongestion(4f);
                _senate.AddPolicy("SWStatMorale", -2f);
                break;

            case (7, 0):
                AddCongestion(2f);
                if (active.SectorId != null && _senate.IsClaimed(active.SectorId))
                    _senate.AddSectorBias(active.SectorId, "SWGlobalSociety", 2f);
                break;
            case (7, 1):
                if (active.SectorId != null && _senate.IsClaimed(active.SectorId))
                    _senate.AddSectorBias(active.SectorId, "SWGlobalSociety", -2f);
                break;

            case (8, 0):
                _security.AddThreat(-4f);
                _senate.AddPolicy("SWStatMorale", 2f);
                AddLog("Рейд с ВБ: невольничий борт перехвачен, экипаж под арестом.");
                break;
            case (8, 1):
                _security.AddThreat(3f);
                _senate.AddPolicy("SWStatRights", -2f);
                break;

            case (9, 0):
                AddBudget(400);
                AddSafety(2f);
                AddLog("Свуп-гонщики оштрафованы, байки на штрафстоянке.");
                break;
            case (9, 1):
                AddSafety(-4f);
                break;
        }
    }

    private void FailEvent(ActiveEvent active)
    {
        AddMobility(-2f);
        _senate.AddPolicy("SWStatMorale", -1f);
        AddLog($"«{Events[active.DefIndex].Name}»: диспетчеры не дождались решения.");
    }

    // --- Express operations ---

    private static readonly (string Name, string Desc, int Cost, int Cd)[] Ops =
    {
        ("Субсидированные рейсы", "Дотации перевозчикам: мобильность +12.", 1000, 3),
        ("Тотальная проверка билетов", "Совместные посты с ВБ: угроза и линии под контролем, поток проседает.", 600, 3),
        ("Разгрузка узлов", "Дополнительные стыковочные окна: загруженность -15.", 800, 3),
        ("Ремонт линий", "Плановое обслуживание трасс: безопасность +20.", 900, 2),
        ("Эвакуационный конвой", "Вывезти жителей из наиболее неблагополучного сектора.", 1500, 6),
        ("Продажа манифестов", "Слить пассажирские списки брокерам: +1800 кр.", 0, 6),
    };

    public List<SWShipsOpEntry> BuildOps()
    {
        var budget = _dilemma.GetMinistryBudget(BureauId);
        var list = new List<SWShipsOpEntry>();
        for (var i = 0; i < Ops.Length; i++)
        {
            var (name, desc, cost, _) = Ops[i];
            var entry = new SWShipsOpEntry { Id = i, Name = name, Description = desc, Cost = cost };

            if (_opCooldowns.TryGetValue(i, out var ready) && _time.ActiveGameHours < ready)
                entry.Status = $"готовность через {SWSenateDuration.FormatHours(ready - _time.ActiveGameHours)}";
            else if (cost > budget)
                entry.Status = "недостаточно бюджета";
            else
                entry.Available = true;

            list.Add(entry);
        }

        return list;
    }

    public bool LaunchOp(int idx)
    {
        if (idx < 0 || idx >= Ops.Length)
            return false;
        if (_opCooldowns.TryGetValue(idx, out var ready) && _time.ActiveGameHours < ready)
            return false;

        var (_, _, cost, cd) = Ops[idx];
        if (cost > 0 && !_dilemma.SpendMinistry(BureauId, cost))
            return false;

        _opCooldowns[idx] = _time.ActiveGameHours + cd;

        switch (idx)
        {
            case 0:
                AddMobility(12f);
                AddLog("Субсидированные рейсы вышли на линии.");
                break;
            case 1:
                _security.AddThreat(-3f);
                AddSafety(5f);
                AddMobility(-4f);
                AddLog("Тотальная проверка билетов: линии прочёсаны.");
                break;
            case 2:
                AddCongestion(-15f);
                AddLog("Узлы разгружены дополнительными окнами.");
                break;
            case 3:
                AddSafety(20f);
                AddLog("Плановый ремонт трасс завершён.");
                break;
            case 4:
                EvacuateWorstSector();
                break;
            case 5:
                AddBudget(1800);
                _security.AddThreat(2f);
                _senate.AddPolicy("SWStatRights", -2f);
                AddLog("Пассажирские манифесты проданы неизвестным брокерам.");
                break;
        }

        return true;
    }

    private void EvacuateWorstSector()
    {
        string? worst = null;
        var worstValue = float.MaxValue;
        foreach (var (sectorId, globals) in _senate.SectorGlobals)
        {
            if (!_senate.IsClaimed(sectorId) || !globals.TryGetValue("SWGlobalSociety", out var value))
                continue;
            if (value < worstValue)
            {
                worstValue = value;
                worst = sectorId;
            }
        }

        if (worst == null)
            return;

        _senate.AddSectorBias(worst, "SWGlobalSociety", 4f);
        _senate.AddSectorBias(worst, "SWGlobalHealthcare", 2f);
        AddCongestion(6f);
        AddLog($"Эвакуационный конвой вывез жителей из {SectorName(worst)}.");
    }

    // --- Bureau dilemmas ---

    private readonly record struct DilOpt(string Label, string Desc, int Cost, (string Name, float Delta)[] Show);
    private readonly record struct DilDef(string Name, string Desc, DilOpt[] Options);

    private static readonly DilDef[] Dilemmas =
    {
        new("Церемония нового маршрута",
            "Готов к открытию прямой маршрут Корусант-Тайтон. Канцелярия предлагает официальную церемонию.",
            new[]
            {
                new DilOpt("Провести церемонию", "Публичное открытие повысит интерес к маршруту и продажи билетов.", 1500,
                    new[] { ("Мобильность", 8f), ("Мораль", 2f) }),
                new DilOpt("Открыть без церемонии", "Маршрут начнёт работу без дополнительных расходов и публичного эффекта.", 0,
                    new[] { ("Мораль", -2f) }),
            }),
        new("Эпидемиологический протокол",
            "Министерство здравоохранения требует ужесточить санитарный контроль на всех узлах.",
            new[]
            {
                new DilOpt("Жёсткий протокол", "Полный санитарный контроль снизит пассажиропоток и риск распространения инфекции.", 0,
                    new[] { ("Мобильность", -6f), ("Заражение", -4f) }),
                new DilOpt("Мягкий протокол", "Выборочные проверки сохранят пассажиропоток, но оставят риск заражения.", 0,
                    new[] { ("Мобильность", 4f), ("Заражение", 3f) }),
            }),
        new("Хаттский экспресс",
            "Картель хочет запустить казино-лайнер по кольцевому маршруту через центральные сектора.",
            new[]
            {
                new DilOpt("Лицензировать", "Бюро получит лицензионный сбор, но контроль за деятельностью лайнера будет ограничен.", 0,
                    new[] { ("Казна бюро", 2500f), ("Угроза ВБ", 2f) }),
                new DilOpt("Запретить", "Республика не должна поощрять азартные схемы на пассажирских линиях.", 0,
                    new[] { ("Угроза ВБ", -1f), ("Мобильность", -3f) }),
            }),
        new("Забастовка диспетчеров",
            "Профсоюз диспетчеров требует индексации. Без них линии встанут за час.",
            new[]
            {
                new DilOpt("Индексация", "Диспетчеры возвращаются к пультам.", 1200,
                    new[] { ("Мобильность", 2f) }),
                new DilOpt("Отказать", "Линии продолжат работу за счёт временного персонала.", 0,
                    new[] { ("Загруженность", 15f), ("Мобильность", -8f) }),
            }),
        new("Дипломат инкогнито",
            "Имперский дипломат просит негласный транзит через территорию Республики - «во имя переговоров».",
            new[]
            {
                new DilOpt("Пропустить негласно", "Жест доброй воли повысит дипломатический контакт, но оставит имперский след в линиях.", 0,
                    new[] { ("Договоры", 2f), ("Угроза ВБ", 2f) }),
                new DilOpt("Отказать в транзите", "Дипломат будет направлен через нейтральное пространство.", 0,
                    new[] { ("Договоры", -2f), ("Угроза ВБ", -2f) }),
            }),
        new("Паломничество на Тайтон",
            "Десятки тысяч паломников хотят попасть на священную луну к празднику Единения.",
            new[]
            {
                new DilOpt("Спецрейсы", "Организовать паломнические конвои.", 800,
                    new[] { ("Мораль", 4f), ("Загруженность", 6f) }),
                new DilOpt("Ограничить квотой", "Не больше тысячи в день.", 0,
                    new[] { ("Мораль", -3f), ("Права", -1f) }),
            }),
        new("Крушение экспресса",
            "Скоростной экспресс сошёл с гиперкоридора. Десятки жертв. Голонет требует ответов.",
            new[]
            {
                new DilOpt("Открытое расследование", "Найти причину, наказать виновных, укрепить линии.", 1000,
                    new[] { ("Безопасность", 8f), ("Мораль", 1f) }),
                new DilOpt("Закрыть внутренним отчётом", "Оформить происшествие как форс-мажор без публичного расследования.", 0,
                    new[] { ("Безопасность", -4f), ("Мораль", -2f) }),
            }),
        new("Чёрный рынок билетов",
            "Перекупщики скупили билеты на месяц вперёд и продают их по завышенным ценам.",
            new[]
            {
                new DilOpt("Облава", "Аннулировать спекулятивные брони.", 600,
                    new[] { ("Загруженность", -3f), ("Угроза ВБ", -2f) }),
                new DilOpt("Ввести сбор", "Перепродажа останется под контролем бюро и будет обложена сбором.", 0,
                    new[] { ("Казна бюро", 1200f), ("Права", -2f), ("Угроза ВБ", 2f) }),
            }),
        new("Сироты со станции «Бентор»",
            "После аварии станции без крова остались десятки детей. Им нужен транзит и новый дом.",
            new[]
            {
                new DilOpt("Бесплатный транзит и размещение", "Республика берёт на себя гуманитарную эвакуацию.", 500,
                    new[] { ("Мораль", 3f), ("Права", 1f) }),
                new DilOpt("Передать профильным службам", "Вопрос будет передан без участия бюро транзита.", 0,
                    new[] { ("Мораль", -4f), ("Права", -3f) }),
            }),
    };

    public SWBureauDilemmaEntry? BuildDilemma()
    {
        if (_dilIndex < 0 || _dilIndex >= Dilemmas.Length)
            return null;

        var def = Dilemmas[_dilIndex];
        var budget = _dilemma.GetMinistryBudget(BureauId);
        var entry = new SWBureauDilemmaEntry
        {
            Name = def.Name,
            Description = def.Desc,
            TimeLabel = $"Решить в течение ~{MathF.Max(0f, _dilLife):0.0} ч",
        };

        foreach (var opt in def.Options)
        {
            var oe = new SWBureauDilemmaOptionEntry
            {
                Label = opt.Label,
                Description = opt.Desc,
                Cost = opt.Cost,
                Affordable = opt.Cost <= budget,
            };
            foreach (var (name, delta) in opt.Show)
                oe.Effects.Add(new SWBillEffectEntry { StatName = name, Color = ShowColor(name, delta), Delta = delta });
            entry.Options.Add(oe);
        }

        return entry;
    }

    private static Color ShowColor(string name, float delta)
    {
        if (name == "Казна бюро")
            return Color.FromHex("#B7950B");

        var inverted = name is "Угроза ВБ" or "Загруженность" or "Заражение";
        var good = inverted ? delta < 0f : delta > 0f;
        return good ? Color.FromHex("#43D16A") : Color.FromHex("#E0556B");
    }

    public bool ResolveDilemma(int optionIndex)
    {
        if (_dilIndex < 0 || _dilIndex >= Dilemmas.Length)
            return false;
        if (optionIndex < 0 || optionIndex >= Dilemmas[_dilIndex].Options.Length)
            return false;
        if (Dilemmas[_dilIndex].Options[optionIndex].Cost > _dilemma.GetMinistryBudget(BureauId))
            return false;

        ApplyDilemma(_dilIndex, optionIndex);
        _dilIndex = -1;
        _dilemmaQueue.Reset(false);
        return true;
    }

    private void ApplyDilemma(int dil, int opt)
    {
        switch (dil, opt)
        {
            case (0, 0):
                if (_dilemma.SpendMinistry(BureauId, 1500))
                {
                    AddMobility(8f);
                    _senate.AddPolicy("SWStatMorale", 2f);
                    AddLog("Маршрут Корусант-Тайтон открыт с церемонией.");
                }
                break;
            case (0, 1):
                _senate.AddPolicy("SWStatMorale", -2f);
                break;

            case (1, 0):
                AddMobility(-6f);
                if (_health.PathogenActive)
                    _health.AddInfectionAll(-4f);
                AddLog("Жёсткий санитарный протокол введён на всех узлах.");
                break;
            case (1, 1):
                AddMobility(4f);
                if (_health.PathogenActive)
                    _health.AddInfectionAll(3f);
                break;

            case (2, 0):
                AddBudget(2500);
                _security.AddThreat(2f);
                AddLog("Хаттский экспресс лицензирован. Казна пополнена.");
                break;
            case (2, 1):
                _security.AddThreat(-1f);
                AddMobility(-3f);
                break;

            case (3, 0):
                if (_dilemma.SpendMinistry(BureauId, 1200))
                    AddMobility(2f);
                break;
            case (3, 1):
                AddCongestion(15f);
                AddMobility(-8f);
                AddLog("Диспетчеры бастуют. Узлы захлёбываются.");
                break;

            case (4, 0):
                _senate.AddPolicy("SWStatTreaties", 2f);
                _security.AddThreat(2f);
                break;
            case (4, 1):
                _senate.AddPolicy("SWStatTreaties", -2f);
                _security.AddThreat(-2f);
                break;

            case (5, 0):
                if (_dilemma.SpendMinistry(BureauId, 800))
                {
                    _senate.AddPolicy("SWStatMorale", 4f);
                    AddCongestion(6f);
                    AddLog("Паломнические конвои идут на Тайтон.");
                }
                break;
            case (5, 1):
                _senate.AddPolicy("SWStatMorale", -3f);
                _senate.AddPolicy("SWStatRights", -1f);
                break;

            case (6, 0):
                if (_dilemma.SpendMinistry(BureauId, 1000))
                {
                    AddSafety(8f);
                    _senate.AddPolicy("SWStatMorale", 1f);
                    AddLog("Расследование крушения: виновные под судом, линии укреплены.");
                }
                break;
            case (6, 1):
                AddSafety(-4f);
                _senate.AddPolicy("SWStatMorale", -2f);
                break;

            case (7, 0):
                if (_dilemma.SpendMinistry(BureauId, 600))
                {
                    AddCongestion(-3f);
                    _security.AddThreat(-2f);
                    AddLog("Облава на перекупщиков: брони аннулированы.");
                }
                break;
            case (7, 1):
                AddBudget(1200);
                _senate.AddPolicy("SWStatRights", -2f);
                _security.AddThreat(2f);
                break;

            case (8, 0):
                if (_dilemma.SpendMinistry(BureauId, 500))
                {
                    _senate.AddPolicy("SWStatMorale", 3f);
                    _senate.AddPolicy("SWStatRights", 1f);
                    AddLog("Сироты со станции «Бентор» размещены в новых домах.");
                }
                break;
            case (8, 1):
                _senate.AddPolicy("SWStatMorale", -4f);
                _senate.AddPolicy("SWStatRights", -3f);
                break;
        }
    }

    private void FailDilemma()
    {
        AddMobility(-4f);
        _senate.AddPolicy("SWStatMorale", -1.5f);
        AddLog("Нерешённый вопрос бюро истёк с неблагоприятными последствиями.");
    }

    // --- Tick ---

    public override void Update(float frameTime)
    {
        frameTime = _time.ActiveSeconds(frameTime);
        if (frameTime <= 0f)
            return;
        if (!_time.ModeActive)
            return;

        _accum += frameTime;
        if (_accum < TickInterval)
            return;

        var dt = _accum;
        _accum = 0f;

        if (_time.Day != _day)
        {
            _day = _time.Day;
        }

        if (!_time.DayActive)
            return;

        var gameHours = dt * (1f / SWSenateTimeSystem.RealSecondsPerGameHour);
        var claimed = _ministry.IsClaimed(BureauId);

        PruneStatuses();
        DriftMeters(gameHours);
        TickNetworkEffects(gameHours);
        TickDiffusion(gameHours);

        if (!claimed)
            return;

        TickIncome(gameHours);
        TickEvents(gameHours);
        TickDilemma(gameHours);
    }

    private void PruneStatuses()
    {
        foreach (var sectorId in _status.Keys.ToList())
        {
            if (!_senate.IsClaimed(sectorId))
                _status.Remove(sectorId);
        }
    }

    private void DriftMeters(float gameHours)
    {
        var statuses = ClaimedStatuses();
        var weight = 0f;
        var open = 0;
        foreach (var (_, status) in statuses)
        {
            weight += status switch { StatusOpen => 1f, StatusVisa => 0.5f, _ => 0f };
            if (status == StatusOpen)
                open++;
        }

        var avgWeight = statuses.Count > 0 ? weight / statuses.Count : 0.5f;
        var mobilityTarget = 20f + 75f * avgWeight;
        _mobility += (mobilityTarget - _mobility) * Math.Clamp(0.2f * gameHours, 0f, 1f);
        _mobility = Math.Clamp(_mobility, 0f, 100f);

        var congestionTarget = _mobility * (0.35f + open * 0.06f);
        _congestion += (Math.Clamp(congestionTarget, 0f, 100f) - _congestion) * Math.Clamp(0.12f * gameHours, 0f, 1f);
        _congestion = Math.Clamp(_congestion, 0f, 100f);

        var decay = SafetyDecay + (_congestion >= 70f ? 0.5f : 0f);
        AddSafety(-decay * gameHours);
    }

    private void TickNetworkEffects(float gameHours)
    {
        var push = (_mobility - 50f) / 50f;
        if (push != 0f)
        {
            _senate.AddPolicy("SWStatTrade", 0.2f * push * gameHours);
            _senate.AddPolicy("SWStatMorale", 0.1f * push * gameHours);
        }

        var unrest = _security.Unrest;
        foreach (var (sectorId, status) in ClaimedStatuses())
        {
            switch (status)
            {
                case StatusCordon:
                    _senate.AddSectorBias(sectorId, "SWGlobalEconomy", -0.35f * gameHours);
                    _senate.AddPolicy("SWStatRights", -0.08f * gameHours);
                    if (unrest >= 55)
                        _security.AddUnrest(-0.25f * gameHours);
                    break;

                case StatusVisa:
                    _senate.AddPolicy("SWStatRights", -0.03f * gameHours);
                    break;

                default:
                    if (_congestion >= 70f)
                        _senate.AddSectorBias(sectorId, "SWGlobalSociety", -0.15f * gameHours);
                    if (unrest >= 55 && _mobility >= 60f)
                        _senate.AddSectorBias(sectorId, "SWGlobalSociety", -0.1f * gameHours);
                    break;
            }
        }

        if (_safety < 40f && _random.Prob(Math.Clamp(0.10f * gameHours, 0f, 1f)))
        {
            AddMobility(-3f);
            _senate.AddPolicy("SWStatMorale", -1.5f);
            AddLog("Авария на пассажирской линии: рейсы задержаны, публика в ярости.");
        }
    }

    private void TickDiffusion(float gameHours)
    {
        var openSectors = ClaimedStatuses().Where(kv => kv.Value == StatusOpen).Select(kv => kv.Key).ToList();
        if (openSectors.Count < 2)
            return;

        var rate = 0.03f * (_mobility / 50f) * gameHours;
        foreach (var globalId in new[] { "SWGlobalEconomy", "SWGlobalSociety" })
        {
            var sum = 0f;
            var count = 0;
            foreach (var sectorId in openSectors)
            {
                if (_senate.SectorGlobals.TryGetValue(sectorId, out var globals) && globals.TryGetValue(globalId, out var value))
                {
                    sum += value;
                    count++;
                }
            }

            if (count < 2)
                continue;

            var avg = sum / count;
            foreach (var sectorId in openSectors)
            {
                if (_senate.SectorGlobals.TryGetValue(sectorId, out var globals) && globals.TryGetValue(globalId, out var value))
                    _senate.AddSectorBias(sectorId, globalId, (avg - value) * rate);
            }
        }
    }

    /// <summary>Passenger restrictions affect the health model's real graph contacts.</summary>
    public float HealthContactFactor(string sectorId)
    {
        var status = GetStatus(sectorId);
        return status == StatusCordon ? 0 : (status == StatusVisa ? .5f : 1) * Math.Clamp(_mobility / 50, 0, 2);
    }

    private void TickIncome(float gameHours)
    {
        _income += TicketIncome() * gameHours;
        if (_income < 1f)
            return;

        var due = (int) _income;
        _income -= due;
        AddBudget(due);
    }

    private void TickEvents(float gameHours)
    {
        for (var i = _events.Count - 1; i >= 0; i--)
        {
            var active = _events[i];
            active.LifeHours -= gameHours;
            if (active.LifeHours > 0f)
                continue;

            _events.RemoveAt(i);
            FailEvent(active);
        }

        if (!_arrivalQueue.Tick(_random, gameHours, _events.Count + (_dilIndex >= 0 ? 2 : 0) + _dilemma.PendingWork(BureauId), EventCap))
            return;
        var pool = Enumerable.Range(0, Events.Length).Where(i => _events.All(e => e.DefIndex != i)).ToList();
        if (pool.Count == 0)
            return;

        var claimed = _senate.SectorHolders.Keys.ToList();
        _events.Add(new ActiveEvent
        {
            Id = _nextEventId++,
            DefIndex = _random.Pick(pool),
            LifeHours = EventLife,
            SectorId = claimed.Count > 0 ? _random.Pick(claimed) : null,
        });
    }

    private void TickDilemma(float gameHours)
    {
        if (_dilIndex >= 0)
        {
            _dilLife -= gameHours;
            if (_dilLife <= 0f)
            {
                FailDilemma();
                _dilIndex = -1;
                _dilemmaQueue.Reset(false);
            }

            return;
        }

        if (!_dilemmaQueue.Tick(_random, gameHours,
            _events.Count + _dilemma.PendingWork(BureauId), 4))
            return;
        _dilIndex = _random.Next(Dilemmas.Length);
        _dilLife = DilInterval;
    }

    private sealed class ActiveEvent
    {
        public int Id;
        public int DefIndex;
        public float LifeHours;
        public string? SectorId;
    }
}
