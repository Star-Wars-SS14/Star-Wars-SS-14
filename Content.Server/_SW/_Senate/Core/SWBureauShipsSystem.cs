using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Core;

public sealed class SWBureauShipsSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWMinistryInternalSecuritySystem _security = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;

    public const string BureauId = "SWBureauShips";

    private const float TickInterval = 1f;
    private const int AppCap = 8;
    private const float AppInterval = 0.35f;
    private const float DilInterval = 2f;
    private const int InspectCost = 400;
    private const float NetworkDecay = 1.1f;
    private const float DuesPerShipHour = 20f;
    private const int FleetCap = 20;

    private static readonly string[] Flags =
    {
        "«Звёздный вихрь»", "«Кореллианская линия»", "«Глубинный фрахт»", "«Внешнее кольцо-Транзит»",
        "«Небула Экспресс»", "«Хатт-Карго»", "«Первопроходец»", "«Тионский рейс»", "«Сен-Тон Лайнс»",
    };

    private static readonly string[] ShipNames =
    {
        "Странник", "Гордость Кореллии", "Астра", "Пылевой бегун", "Селонианка", "Дюрасталь",
        "Край галактики", "Тионский ветер", "Вентафор", "Ронто", "Зов Тайтона", "Алый парус",
        "Мародёр Мау", "Данталус", "Кессельский гонец", "Белая нерфа",
    };

    private static readonly (string Name, int Fee)[] Classes =
    {
        ("Грузовик", 300), ("Лайнер", 500), ("Курьер", 250), ("Танкер", 450),
        ("Буксир", 200), ("Яхта", 600), ("Разведчик", 350),
    };

    private static readonly string[] LegalCargo =
    {
        "зерно", "тибанна-газ", "медикаменты", "дроиды-помощники", "запчасти",
        "кристаллы иллума", "руда", "ткани", "топливо", "стадо нерфов",
    };

    private static readonly string[] DirtyCargo =
    {
        "контрабанда бластеров", "нелегальные специи", "краденые дроиды",
        "шпионское оборудование Империи", "неучтённое военное сырьё",
    };

    private static readonly string[] SuspiciousCargo =
    {
        "двойное дно, следы специй", "неопознанные контейнеры", "перебитые серийники дроидов",
        "груз не совпадает с декларацией",
    };

    private static readonly string[] WantedReasons =
    {
        "контрабанда оружия", "шпионаж в пользу Империи", "пиратство", "работорговля", "хищение казённых грузов",
    };

    private static readonly string[] GoodShipEvents =
    {
        "открыл новый маршрут - торговля оживилась",
        "доставил гуманитарный груз точно в срок",
        "отбился от пиратского перехвата, репутация флага растёт",
        "установил рекорд кессельского плеча",
    };

    private float _tradeFlow = 50f;
    private float _integrity = 60f;
    private float _network = 75f;
    private int _policy = 1;
    private readonly Dictionary<string, float> _forged = new();

    private readonly List<ShipApp> _apps = new();
    private int _nextAppId = 1;
    private float _appTimer;

    private readonly List<RegisteredShip> _fleet = new();
    private int _nextShipId = 1;
    private float _dues;

    private readonly List<WantedEntry> _wanted = new();
    private int _nextWantedId = 1;

    private readonly Dictionary<int, int> _opCooldowns = new();
    private readonly List<string> _log = new();

    private int _dilIndex = -1;
    private float _dilTimer;
    private float _dilLife;

    private int _day = -1;
    private float _accum;

    public int TradeFlow => (int) MathF.Round(_tradeFlow);
    public int Integrity => (int) MathF.Round(_integrity);
    public int Network => (int) MathF.Round(_network);
    public int Policy => _policy;
    public int ForgedTotal => (int) MathF.Round(_forged.Values.Sum());
    public int FleetRegistered => _fleet.Count;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _tradeFlow = 50f;
        _integrity = 60f;
        _network = 75f;
        _policy = 1;
        _forged.Clear();
        _apps.Clear();
        _nextAppId = 1;
        _appTimer = 0f;
        _fleet.Clear();
        _nextShipId = 1;
        _dues = 0f;
        _wanted.Clear();
        _nextWantedId = 1;
        _opCooldowns.Clear();
        _log.Clear();
        _dilIndex = -1;
        _dilTimer = 0f;
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

    private void AddFlow(float d) => _tradeFlow = Math.Clamp(_tradeFlow + d, 0f, 100f);
    private void AddIntegrity(float d) => _integrity = Math.Clamp(_integrity + d, 0f, 100f);
    private void AddNetwork(float d) => _network = Math.Clamp(_network + d, 0f, 100f);

    private void AddBudget(int amount) =>
        _dilemma.SetMinistryBudget(BureauId, Math.Max(0, _dilemma.GetMinistryBudget(BureauId) + amount));

    public string TradeFlowStatus() => _tradeFlow switch
    {
        >= 75f => "Оживлённая торговля",
        >= 55f => "Устойчивый поток",
        >= 35f => "Вялый поток",
        _ => "Застой",
    };

    public string IntegrityStatus() => _integrity switch
    {
        >= 75f => "Образцовый реестр",
        >= 50f => "Надёжный",
        >= 30f => "Дырявый",
        _ => "Проходной двор",
    };

    public string NetworkStatus() => _network switch
    {
        >= 70f => "Маяки в порядке",
        >= 40f => "Сеть изношена",
        _ => "Аварийное состояние",
    };

    public static string PolicyLabel(int policy) => policy switch
    {
        0 => "Свободная регистрация",
        2 => "Строгий досмотр",
        _ => "Сбалансированный режим",
    };

    public void SetPolicy(int level) => _policy = Math.Clamp(level, 0, 2);

    // --- Applications ---

    private void SpawnApp()
    {
        var baseForged = _policy switch { 0 => 0.45f, 2 => 0.15f, _ => 0.28f };
        var forgedChance = Math.Clamp(baseForged * (1.6f - _integrity / 100f), 0.05f, 0.6f);
        var roll = _random.NextFloat();
        var risk = roll < forgedChance ? ShipRisk.Forged
            : roll < forgedChance + 0.22f ? ShipRisk.Suspicious
            : ShipRisk.Clean;

        var claimed = _senate.SectorHolders.Keys.ToList();
        string? origin = claimed.Count > 0 && _random.Prob(0.7f) ? _random.Pick(claimed) : null;

        var (className, baseFee) = _random.Pick(Classes);
        var policyMult = _policy switch { 0 => 0.8f, 2 => 1.3f, _ => 1f };
        var fee = (int) (baseFee * policyMult * (0.5f + _integrity / 200f + _tradeFlow / 200f));

        var app = new ShipApp
        {
            Id = _nextAppId++,
            Flag = _random.Pick(Flags),
            ShipName = _random.Pick(ShipNames),
            Class = className,
            DeclaredCargo = _random.Pick(LegalCargo),
            OriginSector = origin,
            Risk = risk,
            Fee = fee,
            Urgent = _random.Prob(0.25f),
            LifeHours = 1.2f,
            Vip = origin != null && _random.Prob(0.15f),
        };

        if (risk != ShipRisk.Clean && _random.Prob(0.4f))
            app.Bribe = 600 + _random.Next(11) * 100;

        if (_wanted.Count > 0 && _random.Prob(0.25f))
        {
            app.Risk = ShipRisk.Forged;
            app.WantedId = _random.Pick(_wanted).Id;
        }

        app.TrueCargo = app.Risk switch
        {
            ShipRisk.Forged => _random.Pick(DirtyCargo),
            ShipRisk.Suspicious => _random.Pick(SuspiciousCargo),
            _ => "груз подтверждён",
        };

        _apps.Add(app);
    }

    public bool Process(int appId, int action)
    {
        var app = _apps.FirstOrDefault(a => a.Id == appId);
        if (app == null)
            return false;

        switch (action)
        {
            case 1:
                if (app.Inspected || !_dilemma.SpendMinistry(BureauId, InspectCost))
                    return false;
                app.Inspected = true;
                AddIntegrity(1f);
                return true;

            case 0:
                Approve(app, false);
                return true;

            case 2:
                Deny(app);
                return true;

            case 3:
                if (app.Bribe <= 0 || _ministry.GetHolder(BureauId) is not { } holder)
                    return false;
                _corruption.TakeBribe(holder, app.OriginSector, app.Bribe);
                Approve(app, true);
                AddIntegrity(-2f);
                AddLog($"«{app.ShipName}» зарегистрирован без лишних вопросов.");
                return true;

            default:
                return false;
        }
    }

    private void Approve(ShipApp app, bool bribed)
    {
        _apps.Remove(app);
        AddBudget(app.Fee);

        if (app.Risk == ShipRisk.Clean)
        {
            AddFlow(3f);
            _senate.AddPolicy("SWStatTrade", 0.6f);
            if (app.Vip && app.OriginSector != null && _senate.IsClaimed(app.OriginSector))
                _senate.AddSectorBias(app.OriginSector, "SWGlobalSociety", 2f);
        }
        else
        {
            AddFlow(4f);
            AddIntegrity(-4f);
            SeedForged();

            if (app.WantedId > 0)
            {
                _wanted.RemoveAll(w => w.Id == app.WantedId);
                _security.AddThreat(6f);
                AddIntegrity(-6f);
                AddLog($"Разыскиваемое судно «{app.ShipName}» ушло с чистыми документами.");
            }
        }

        _fleet.Insert(0, new RegisteredShip
        {
            Id = _nextShipId++,
            Name = app.ShipName,
            Class = app.Class,
            Flag = app.Flag,
            Risk = app.Risk,
            Known = app.Inspected || bribed,
        });

        if (_fleet.Count > FleetCap)
            _fleet.RemoveAt(_fleet.Count - 1);
    }

    private void Deny(ShipApp app)
    {
        _apps.Remove(app);

        if (app.Inspected && app.WantedId > 0)
        {
            var wanted = _wanted.FirstOrDefault(w => w.Id == app.WantedId);
            _wanted.RemoveAll(w => w.Id == app.WantedId);
            var bounty = wanted?.Bounty ?? 1500;
            AddBudget(bounty);
            _security.AddThreat(-4f);
            AddIntegrity(6f);
            AddLog($"«{app.ShipName}» задержан по розыску ({wanted?.Reason ?? "ориентировка МВБ"}): премия {bounty} кр.");
            return;
        }

        if (app.Risk == ShipRisk.Clean)
        {
            AddFlow(-4f);
            _senate.AddPolicy("SWStatTrade", -0.6f);
            if (app.Vip && app.OriginSector != null && _senate.IsClaimed(app.OriginSector))
            {
                _senate.AddSectorBias(app.OriginSector, "SWGlobalSociety", -3f);
                AddLog($"Сектор оскорблён отказом VIP-судну «{app.ShipName}».");
            }
        }
        else
        {
            AddFlow(-1f);
            _security.AddThreat(-1.5f);
            AddIntegrity(3f);
        }
    }

    private void SeedForged()
    {
        var claimed = _senate.SectorHolders.Keys.ToList();
        if (claimed.Count == 0)
            return;

        var sectorId = _random.Pick(claimed);
        _forged[sectorId] = (_forged.TryGetValue(sectorId, out var cur) ? cur : 0f) + 1f;
        _security.AddThreat(2.5f);
    }

    public List<SWShipsAppEntry> BuildApps()
    {
        var budget = _dilemma.GetMinistryBudget(BureauId);
        var list = new List<SWShipsAppEntry>();
        foreach (var app in _apps)
        {
            var origin = app.OriginSector != null && _proto.TryIndex<SWSectorPrototype>(app.OriginSector, out var sp)
                ? sp.Name
                : "происхождение неизвестно";

            var flags = new List<string>();
            if (app.Urgent)
                flags.Add($"СРОЧНО · осталось {MathF.Max(0f, app.LifeHours):0.0} ч");
            if (app.Vip)
                flags.Add("VIP-протекция сектора");

            var dossier = string.Empty;
            if (app.Inspected)
            {
                dossier = app.TrueCargo;
                if (app.WantedId > 0 && _wanted.FirstOrDefault(w => w.Id == app.WantedId) is { } wanted)
                    dossier += $" · СОВПАДЕНИЕ С РОЗЫСКОМ {wanted.Signature}";
            }

            list.Add(new SWShipsAppEntry
            {
                Id = app.Id,
                Name = $"«{app.ShipName}» · {app.Class} · {app.Flag}",
                Origin = $"{origin} · груз: {app.DeclaredCargo} · пошлина: {app.Fee} кр",
                FlagsLabel = string.Join("  ·  ", flags),
                RiskLabel = app.Inspected ? RiskLabel(app.Risk) : "не проверено",
                RiskColor = app.Inspected ? RiskColor(app.Risk) : Color.FromHex("#888888"),
                DossierLabel = dossier,
                Inspected = app.Inspected,
                InspectCost = InspectCost,
                CanInspect = !app.Inspected && budget >= InspectCost,
                Fee = app.Fee,
                Bribe = app.Bribe,
                WantedMatch = app.Inspected && app.WantedId > 0,
            });
        }

        return list;
    }

    private static string RiskLabel(ShipRisk risk) => risk switch
    {
        ShipRisk.Clean => "Чисто",
        ShipRisk.Suspicious => "Подозрительно",
        _ => "Поддельный транспондер",
    };

    private static Color RiskColor(ShipRisk risk) => risk switch
    {
        ShipRisk.Clean => Color.FromHex("#43D16A"),
        ShipRisk.Suspicious => Color.FromHex("#E0A23B"),
        _ => Color.FromHex("#E0556B"),
    };

    // --- Fleet & wanted ---

    public List<SWShipsFleetEntry> BuildFleet()
    {
        var list = new List<SWShipsFleetEntry>();
        foreach (var ship in _fleet.Take(12))
        {
            var (label, color) = ship.Known switch
            {
                true when ship.Risk == ShipRisk.Forged => ("фальшивый борт", Color.FromHex("#E0556B")),
                true when ship.Risk == ShipRisk.Suspicious => ("сомнительный", Color.FromHex("#E0A23B")),
                _ => ("на линии", Color.FromHex("#43D16A")),
            };

            list.Add(new SWShipsFleetEntry
            {
                Name = $"«{ship.Name}»",
                Detail = $"{ship.Class} · {ship.Flag}",
                StatusLabel = label,
                StatusColor = color,
            });
        }

        return list;
    }

    public List<SWShipsWantedEntry> BuildWanted() =>
        _wanted.Select(w => new SWShipsWantedEntry { Signature = w.Signature, Reason = w.Reason, Bounty = w.Bounty }).ToList();

    // --- Express operations ---

    private static readonly (string Name, string Desc, int Cost, int Cd)[] Ops =
    {
        ("Обслуживание маяков", "Ремонтные бригады на гиперпутях: состояние сети +25.", 800, 2),
        ("Конвойное сопровождение", "Эскорт торговых линий: поток +10, угроза ВБ снижается.", 1200, 4),
        ("Амнистия транспондеров", "Легализовать поддельные транспондеры: часть контрабанды выйдет из тени, но доверие к реестру снизится.", 1500, 6),
        ("Тотальный аудит очереди", "Сверхурочная проверка: мгновенно вскрывает риск всех заявок в очереди.", 600, 3),
        ("Продажа данных реестра", "Передать маршруты перевозчиков теневым брокерам: +2000 кр в казну бюро.", 0, 6),
    };

    public List<SWShipsOpEntry> BuildOps()
    {
        var budget = _dilemma.GetMinistryBudget(BureauId);
        var list = new List<SWShipsOpEntry>();
        for (var i = 0; i < Ops.Length; i++)
        {
            var (name, desc, cost, _) = Ops[i];
            var entry = new SWShipsOpEntry { Id = i, Name = name, Description = desc, Cost = cost };

            if (_opCooldowns.TryGetValue(i, out var ready) && _time.AbsoluteHour < ready)
                entry.Status = $"готовность через {ready - _time.AbsoluteHour} ч";
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
        if (_opCooldowns.TryGetValue(idx, out var ready) && _time.AbsoluteHour < ready)
            return false;

        var (_, _, cost, cd) = Ops[idx];
        if (cost > 0 && !_dilemma.SpendMinistry(BureauId, cost))
            return false;

        _opCooldowns[idx] = _time.AbsoluteHour + cd;

        switch (idx)
        {
            case 0:
                AddNetwork(25f);
                AddLog("Маяки гиперпутей обслужены.");
                break;
            case 1:
                AddFlow(10f);
                _security.AddThreat(-2f);
                AddLog("Конвои сопровождают торговые линии.");
                break;
            case 2:
                var count = _forged.Count;
                _forged.Clear();
                AddIntegrity(-6f);
                _security.AddThreat(-3f);
                AddFlow(5f);
                AddLog($"Амнистия: фальшивые транспондеры легализованы ({count} сект.).");
                break;
            case 3:
                foreach (var app in _apps)
                    app.Inspected = true;
                AddIntegrity(2f);
                AddLog("Тотальный аудит очереди завершён.");
                break;
            case 4:
                AddBudget(2000);
                AddIntegrity(-8f);
                _security.AddThreat(2f);
                AddLog("Данные реестра проданы неизвестным брокерам.");
                break;
        }

        return true;
    }

    // --- Bureau dilemmas ---

    private readonly record struct DilOpt(string Label, string Desc, int Cost, (string Name, float Delta)[] Show);
    private readonly record struct DilDef(string Name, string Desc, DilOpt[] Options);

    private static readonly DilDef[] Dilemmas =
    {
        new("Флаг удобства",
            "Крупный перевозчик просит перерегистрировать весь флот под флаг Республики без индивидуальных проверок.",
            new[]
            {
                new DilOpt("Одобрить пакетом", "Торговля резко вырастет, но в реестр попадут поддельные документы.", 0,
                    new[] { ("Торговый поток", 12f), ("Фальшивки", 2f) }),
                new DilOpt("Только с проверкой", "Проверить каждый корпус - часть развернём, поток просядет.", 0,
                    new[] { ("Торговый поток", -8f), ("Угроза ВБ", -3f) }),
            }),
        new("Устаревшие гиперкарты",
            "Навигационные карты устарели, растёт риск аварий на гиперпутях.",
            new[]
            {
                new DilOpt("Обновить карты", "Оплатить срочное обновление реестра маршрутов.", 2000,
                    new[] { ("Нав. сеть", 12f) }),
                new DilOpt("Отложить", "Сэкономить сейчас - платить авариями потом.", 0,
                    new[] { ("Нав. сеть", -10f), ("Мораль", -2f) }),
            }),
        new("Контрабандный караван",
            "Разведка ВБ сообщает: под флагом Республики идёт караван с нелегальным грузом.",
            new[]
            {
                new DilOpt("Аннулировать транспондеры", "Заблокировать караван - удар по контрабанде.", 0,
                    new[] { ("Угроза ВБ", -6f), ("Торговый поток", -5f) }),
                new DilOpt("Закрыть глаза", "Не вмешиваться - поток цел, но контрабанда крепнет.", 0,
                    new[] { ("Торговый поток", 4f), ("Фальшивки", 2f) }),
            }),
        new("Забастовка регистраторов",
            "Клерки реестра требуют надбавок и грозят остановить регистрацию.",
            new[]
            {
                new DilOpt("Выплатить надбавки", "Реестр работает штатно.", 1500,
                    new[] { ("Торговый поток", 6f) }),
                new DilOpt("Отказать", "Остановка регистрации резко снизит торговый поток.", 0,
                    new[] { ("Торговый поток", -10f) }),
            }),
        new("Хаттский картель",
            "Посредники картеля предлагают крупный контракт на эксклюзивную регистрацию их флота без проверки грузов.",
            new[]
            {
                new DilOpt("Принять контракт", "Казна бюро пополнится, но реестр станет инструментом легализации грузов картеля.", 0,
                    new[] { ("Казна бюро", 3000f), ("Целостность", -10f), ("Фальшивки", 2f) }),
                new DilOpt("Отказать", "Картель обратится к конкурентам, но реестр сохранит прозрачность.", 0,
                    new[] { ("Угроза ВБ", -2f), ("Торговый поток", -3f) }),
            }),
        new("Крушение лайнера",
            "Пассажирский лайнер под вашим флагом потерпел крушение. Семьи требуют компенсаций, пресса - официальных объяснений.",
            new[]
            {
                new DilOpt("Выплатить компенсации", "Высокие расходы подтвердят ответственность Республики перед пострадавшими.", 1200,
                    new[] { ("Мораль", 2f) }),
                new DilOpt("Закрыть внутренним отчётом", "Списать происшествие на ошибку пилота без публичного расследования.", 0,
                    new[] { ("Целостность", -6f), ("Мораль", -3f) }),
            }),
        new("Конвой Ордена джедаев",
            "Орден просит негласную регистрацию гуманитарного конвоя в спорные сектора - без следа в открытом реестре.",
            new[]
            {
                new DilOpt("Оформить негласно", "Помощь дойдёт, но имперская разведка может заметить след конвоя.", 0,
                    new[] { ("Мораль", 3f), ("Угроза ВБ", 1f) }),
                new DilOpt("Отказать Ордену", "Реестр останется вне политических исключений.", 0,
                    new[] { ("Мораль", -2f), ("Договоры", -2f) }),
            }),
        new("Пропавший курьер",
            "Курьер реестра с грузом транспондерных кодов исчез в Глубоком Космосе.",
            new[]
            {
                new DilOpt("Снарядить поиски", "Найти экипаж и коды, пока их не нашли другие.", 800,
                    new[] { ("Мораль", 2f), ("Целостность", 2f) }),
                new DilOpt("Списать судно", "Аннулировать коды и внести экипаж в список пропавших.", 0,
                    new[] { ("Мораль", -3f), ("Целостность", -2f) }),
            }),
        new("Челнок беженцев",
            "На границе сектора дрейфует перегруженный челнок с беженцами без документов.",
            new[]
            {
                new DilOpt("Зарегистрировать бесплатно", "Люди получат флаг и защиту Республики.", 0,
                    new[] { ("Мораль", 2f), ("Торговый поток", -2f), ("Целостность", -3f) }),
                new DilOpt("Развернуть", "Реестр не предоставляет временное убежище без документов.", 0,
                    new[] { ("Мораль", -4f), ("Права", -2f) }),
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

        var inverted = name is "Фальшивки" or "Угроза ВБ";
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
        _dilTimer = 0f;
        return true;
    }

    private void ApplyDilemma(int dil, int opt)
    {
        switch (dil, opt)
        {
            case (0, 0):
                AddFlow(12f);
                SeedForged();
                SeedForged();
                break;
            case (0, 1):
                AddFlow(-8f);
                _security.AddThreat(-3f);
                break;

            case (1, 0):
                if (_dilemma.SpendMinistry(BureauId, 2000))
                    AddNetwork(12f);
                break;
            case (1, 1):
                AddNetwork(-10f);
                _senate.AddPolicy("SWStatMorale", -2f);
                break;

            case (2, 0):
                _security.AddThreat(-6f);
                AddFlow(-5f);
                break;
            case (2, 1):
                AddFlow(4f);
                SeedForged();
                SeedForged();
                break;

            case (3, 0):
                if (_dilemma.SpendMinistry(BureauId, 1500))
                    AddFlow(6f);
                break;
            case (3, 1):
                AddFlow(-10f);
                break;

            case (4, 0):
                AddBudget(3000);
                AddIntegrity(-10f);
                SeedForged();
                SeedForged();
                AddLog("Флот картеля внесён в реестр. Хатты довольны.");
                break;
            case (4, 1):
                _security.AddThreat(-2f);
                AddFlow(-3f);
                break;

            case (5, 0):
                if (_dilemma.SpendMinistry(BureauId, 1200))
                    _senate.AddPolicy("SWStatMorale", 2f);
                break;
            case (5, 1):
                AddIntegrity(-6f);
                _senate.AddPolicy("SWStatMorale", -3f);
                break;

            case (6, 0):
                _senate.AddPolicy("SWStatMorale", 3f);
                _security.AddThreat(1f);
                AddLog("Конвой Ордена ушёл в спорные сектора под нейтральным кодом.");
                break;
            case (6, 1):
                _senate.AddPolicy("SWStatMorale", -2f);
                _senate.AddPolicy("SWStatTreaties", -2f);
                break;

            case (7, 0):
                if (_dilemma.SpendMinistry(BureauId, 800))
                {
                    _senate.AddPolicy("SWStatMorale", 2f);
                    AddIntegrity(2f);
                    AddLog("Пропавший курьер найден, коды реестра в безопасности.");
                }
                break;
            case (7, 1):
                _senate.AddPolicy("SWStatMorale", -3f);
                AddIntegrity(-2f);
                break;

            case (8, 0):
                _senate.AddPolicy("SWStatMorale", 2f);
                AddFlow(-2f);
                AddIntegrity(-3f);
                AddLog("Челнок беженцев получил флаг Республики.");
                break;
            case (8, 1):
                _senate.AddPolicy("SWStatMorale", -4f);
                _senate.AddPolicy("SWStatRights", -2f);
                break;
        }
    }

    private void FailDilemma()
    {
        AddFlow(-4f);
        AddIntegrity(-2f);
        _senate.AddPolicy("SWStatTrade", -1.5f);
        AddLog("Нерешённый вопрос бюро истёк с потерями.");
    }

    // --- Tick ---

    public override void Update(float frameTime)
    {
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
            _apps.Clear();
            _opCooldowns.Clear();
            _dilIndex = -1;
            _dilTimer = DilInterval - 0.75f;
            _appTimer = 0f;
        }

        if (!_time.DayActive)
            return;

        var gameHours = dt * (1f / SWSenateTimeSystem.RealSecondsPerGameHour);
        var claimed = _ministry.IsClaimed(BureauId);

        DriftTradeFlow(gameHours);
        TickForged(gameHours);
        TickNetwork(gameHours);
        TickIntegrityBonus(gameHours);

        if (!claimed)
            return;

        TickApps(gameHours);
        TickDilemma(gameHours);
        TickWanted(gameHours);
        TickFleet(gameHours);
    }

    private void DriftTradeFlow(float gameHours)
    {
        var target = _policy switch { 0 => 70f, 2 => 40f, _ => 55f };
        if (_network < 40f)
            target = MathF.Min(target, 50f);

        _tradeFlow += (target - _tradeFlow) * Math.Clamp(0.15f * gameHours, 0f, 1f);
        _tradeFlow = Math.Clamp(_tradeFlow, 0f, 100f);

        var push = (_tradeFlow - 50f) / 50f;
        foreach (var (sectorId, _) in _senate.SectorHolders)
        {
            _senate.AddSectorBias(sectorId, "SWGlobalEconomy", 0.25f * push * gameHours);
            if (_policy == 2)
                _senate.AddSectorBias(sectorId, "SWGlobalSecurity", 0.1f * gameHours);
        }
    }

    private void TickForged(float gameHours)
    {
        if (_forged.Count == 0)
            return;

        foreach (var sectorId in _forged.Keys.ToList())
        {
            var level = _forged[sectorId];
            if (_senate.IsClaimed(sectorId))
                _senate.AddSectorBias(sectorId, "SWGlobalEconomy", -0.2f * level * gameHours);

            level -= 0.06f * gameHours;
            if (level <= 0.05f)
                _forged.Remove(sectorId);
            else
                _forged[sectorId] = level;
        }
    }

    private void TickNetwork(float gameHours)
    {
        AddNetwork(-NetworkDecay * gameHours);

        if (_network >= 40f)
            return;

        if (_random.Prob(Math.Clamp(0.10f * gameHours, 0f, 1f)))
        {
            AddFlow(-3f);
            var claimed = _senate.SectorHolders.Keys.ToList();
            if (claimed.Count > 0)
            {
                var sector = _random.Pick(claimed);
                _senate.AddSectorBias(sector, "SWGlobalEconomy", -2f);
                var name = _proto.TryIndex<SWSectorPrototype>(sector, out var sp) ? sp.Name : sector;
                AddLog($"Авария на гиперпути у {name}: маяки не отвечают.");
            }
            else
            {
                AddLog("Авария на гиперпути: маяки не отвечают.");
            }
        }
    }

    private void TickIntegrityBonus(float gameHours)
    {
        if (_integrity < 70f)
            return;

        foreach (var (sectorId, _) in _senate.SectorHolders)
            _senate.AddSectorBias(sectorId, "SWGlobalSecurity", 0.08f * gameHours);
    }

    private void TickApps(float gameHours)
    {
        for (var i = _apps.Count - 1; i >= 0; i--)
        {
            var app = _apps[i];
            if (!app.Urgent)
                continue;

            app.LifeHours -= gameHours;
            if (app.LifeHours > 0f)
                continue;

            _apps.RemoveAt(i);
            AddFlow(-2f);
            AddLog($"«{app.ShipName}» не дождался регистрации и ушёл под чужой флаг.");
        }

        _appTimer += gameHours;
        var interval = _apps.Count < 3 ? 0.15f : AppInterval;
        if (_appTimer >= interval && _apps.Count < AppCap)
        {
            _appTimer = 0f;
            SpawnApp();
        }
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
                _dilTimer = 0f;
            }

            return;
        }

        _dilTimer += gameHours;
        if (_dilTimer < DilInterval)
            return;

        _dilTimer = 0f;
        _dilIndex = _random.Next(Dilemmas.Length);
        _dilLife = DilInterval;
    }

    private void TickWanted(float gameHours)
    {
        if (_wanted.Count >= 2)
            return;

        var chance = (0.10f + _security.Threat / 100f * 0.25f) * gameHours;
        if (!_random.Prob(Math.Clamp(chance, 0f, 1f)))
            return;

        var entry = new WantedEntry
        {
            Id = _nextWantedId++,
            Signature = $"TR-{_random.Next(1000, 9999)}",
            Reason = _random.Pick(WantedReasons),
            Bounty = 1500 + _random.Next(4) * 500,
        };
        _wanted.Add(entry);
        AddLog($"МВБ объявило в розыск сигнатуру {entry.Signature}: {entry.Reason}.");
    }

    private void TickFleet(float gameHours)
    {
        _dues += _fleet.Count * DuesPerShipHour * gameHours;
        if (_dues >= 1f)
        {
            var due = (int) _dues;
            _dues -= due;
            AddBudget(due);
        }

        for (var i = _fleet.Count - 1; i >= 0; i--)
        {
            var ship = _fleet[i];
            ship.AgeHours += gameHours;

            switch (ship.Risk)
            {
                case ShipRisk.Forged when _random.Prob(Math.Clamp(0.22f * gameHours, 0f, 1f)):
                    _fleet.RemoveAt(i);
                    _security.AddThreat(3f);
                    AddIntegrity(-5f);
                    AddFlow(-4f);
                    var claimed = _senate.SectorHolders.Keys.ToList();
                    if (claimed.Count > 0)
                        _senate.AddSectorBias(_random.Pick(claimed), "SWGlobalEconomy", -2f);
                    AddLog($"«{ship.Name}» перехвачен с контрабандой - пятно на реестре.");
                    break;

                case ShipRisk.Suspicious when _random.Prob(Math.Clamp(0.06f * gameHours, 0f, 1f)):
                    AddIntegrity(-2f);
                    AddBudget(150);
                    AddLog($"«{ship.Name}» оштрафован за неучтённый груз.");
                    break;

                case ShipRisk.Clean when _random.Prob(Math.Clamp(0.04f * gameHours, 0f, 1f)):
                    AddFlow(1f);
                    AddLog($"«{ship.Name}» {_random.Pick(GoodShipEvents)}.");
                    break;
            }
        }
    }

    private sealed class ShipApp
    {
        public int Id;
        public string Flag = string.Empty;
        public string ShipName = string.Empty;
        public string Class = string.Empty;
        public string DeclaredCargo = string.Empty;
        public string TrueCargo = string.Empty;
        public string? OriginSector;
        public ShipRisk Risk;
        public bool Inspected;
        public bool Urgent;
        public float LifeHours;
        public bool Vip;
        public int Fee;
        public int Bribe;
        public int WantedId;
    }

    private sealed class RegisteredShip
    {
        public int Id;
        public string Name = string.Empty;
        public string Class = string.Empty;
        public string Flag = string.Empty;
        public ShipRisk Risk;
        public bool Known;
        public float AgeHours;
    }

    private sealed class WantedEntry
    {
        public int Id;
        public string Signature = string.Empty;
        public string Reason = string.Empty;
        public int Bounty;
    }

    private enum ShipRisk : byte
    {
        Clean,
        Suspicious,
        Forged,
    }
}
