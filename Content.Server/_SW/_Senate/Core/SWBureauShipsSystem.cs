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

    public const string BureauId = "SWBureauShips";

    private const float TickInterval = 1f;
    private const int AppCap = 6;
    private const float AppInterval = 0.7f;   // game-hours between new applications
    private const float DilInterval = 2f;     // game-hours between bureau dilemmas
    private const int InspectCost = 400;

    private static readonly string[] Flags =
    {
        "«Звёздный вихрь»", "«Кореллианская линия»", "«Глубинный фрахт»", "«Внешнее кольцо-Транзит»",
        "«Небула Экспресс»", "«Хатт-Карго»", "«Первопроходец»", "«Тионский рейс»", "«Сен-Тон Лайнс»",
    };

    private float _tradeFlow = 50f;
    private int _policy = 1;                   // 0 lax, 1 balanced, 2 strict
    private readonly Dictionary<string, float> _forged = new();

    private readonly List<ShipApp> _apps = new();
    private int _nextAppId = 1;
    private float _appTimer;

    private int _dilIndex = -1;
    private float _dilTimer;
    private float _dilLife;

    private int _day = -1;
    private float _accum;

    public int TradeFlow => (int) MathF.Round(_tradeFlow);
    public int Policy => _policy;
    public int ForgedTotal => (int) MathF.Round(_forged.Values.Sum());

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _tradeFlow = 50f;
        _policy = 1;
        _forged.Clear();
        _apps.Clear();
        _nextAppId = 1;
        _appTimer = 0f;
        _dilIndex = -1;
        _dilTimer = 0f;
        _dilLife = 0f;
        _day = -1;
    }

    public string TradeFlowStatus() => _tradeFlow switch
    {
        >= 75f => "Оживлённая торговля",
        >= 55f => "Устойчивый поток",
        >= 35f => "Вялый поток",
        _ => "Застой",
    };

    public static string PolicyLabel(int policy) => policy switch
    {
        0 => "Свободная регистрация",
        2 => "Строгий досмотр",
        _ => "Сбалансированный режим",
    };

    public void SetPolicy(int level) => _policy = Math.Clamp(level, 0, 2);

    // --- Applications ---

    public bool Process(int appId, int action)
    {
        var app = _apps.FirstOrDefault(a => a.Id == appId);
        if (app == null)
            return false;

        switch (action)
        {
            case 1: // inspect
                if (app.Inspected || !_dilemma.SpendMinistry(BureauId, InspectCost))
                    return false;
                app.Inspected = true;
                _tradeFlow = Math.Clamp(_tradeFlow - 1f, 0f, 100f);
                return true;

            case 0: // approve
                _apps.Remove(app);
                if (app.Risk == ShipRisk.Clean)
                {
                    _tradeFlow = Math.Clamp(_tradeFlow + 3f, 0f, 100f);
                    _senate.AddPolicy("SWStatTrade", 0.6f);
                }
                else
                {
                    _tradeFlow = Math.Clamp(_tradeFlow + 4f, 0f, 100f);
                    SeedForged();
                }
                return true;

            case 2: // deny
                _apps.Remove(app);
                if (app.Risk == ShipRisk.Clean)
                {
                    _tradeFlow = Math.Clamp(_tradeFlow - 4f, 0f, 100f);
                    _senate.AddPolicy("SWStatTrade", -0.6f);
                }
                else
                {
                    _tradeFlow = Math.Clamp(_tradeFlow - 1f, 0f, 100f);
                    _security.AddThreat(-1.5f);
                }
                return true;

            default:
                return false;
        }
    }

    // Approving a dirty transponder plants forged papers in a claimed sector: contraband cover that feeds
    // smuggling (МВБ threat) and slowly bleeds that sector's economy until it decays.
    private void SeedForged()
    {
        var claimed = _senate.SectorHolders.Keys.ToList();
        if (claimed.Count == 0)
            return;

        var sectorId = _random.Pick(claimed);
        _forged[sectorId] = (_forged.TryGetValue(sectorId, out var cur) ? cur : 0f) + 1f;
        _security.AddThreat(2.5f);
    }

    private void SpawnApp()
    {
        // Lax registration lets more forged hulls slip in; strict screening turns most away as clean.
        var forgedChance = _policy switch { 0 => 0.5f, 2 => 0.15f, _ => 0.3f };
        var suspiciousChance = 0.25f;
        var roll = _random.NextFloat();
        var risk = roll < forgedChance ? ShipRisk.Forged
            : roll < forgedChance + suspiciousChance ? ShipRisk.Suspicious
            : ShipRisk.Clean;

        var claimed = _senate.SectorHolders.Keys.ToList();
        string? origin = claimed.Count > 0 && _random.Prob(0.7f) ? _random.Pick(claimed) : null;

        _apps.Add(new ShipApp
        {
            Id = _nextAppId++,
            Flag = _random.Pick(Flags),
            OriginSector = origin,
            Risk = risk,
        });
    }

    public List<SWShipsAppEntry> BuildApps()
    {
        var budget = _dilemma.GetMinistryBudget(BureauId);
        var list = new List<SWShipsAppEntry>();
        foreach (var app in _apps)
        {
            list.Add(new SWShipsAppEntry
            {
                Id = app.Id,
                Name = app.Flag,
                Origin = app.OriginSector != null && _proto.TryIndex<SWSectorPrototype>(app.OriginSector, out var sp)
                    ? sp.Name
                    : "происхождение неизвестно",
                RiskLabel = app.Inspected ? RiskLabel(app.Risk) : "не проверено",
                RiskColor = app.Inspected ? RiskColor(app.Risk) : Color.FromHex("#888888"),
                Inspected = app.Inspected,
                InspectCost = InspectCost,
                CanInspect = !app.Inspected && budget >= InspectCost,
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

    // --- Bureau dilemmas ---

    private readonly record struct DilOpt(string Label, string Desc, (string Name, float Delta)[] Show);
    private readonly record struct DilDef(string Name, string Desc, DilOpt[] Options);

    private static readonly DilDef[] Dilemmas =
    {
        new("Флаг удобства",
            "Крупный перевозчик просит перерегистрировать весь флот под флаг Республики без индивидуальных проверок.",
            new[]
            {
                new DilOpt("Одобрить пакетом", "Торговля взлетит, но в реестр просочатся фальшивки.",
                    new[] { ("Торговый поток", 12f), ("Фальшивки", 2f) }),
                new DilOpt("Только с проверкой", "Проверить каждый корпус — часть развернём, поток просядет.",
                    new[] { ("Торговый поток", -8f), ("Угроза ВБ", -3f) }),
            }),
        new("Устаревшие гиперкарты",
            "Навигационные карты сектора устарели, растёт риск аварий на гиперпутях.",
            new[]
            {
                new DilOpt("Обновить карты (2000 кр)", "Оплатить обновление реестра — безопасность путей.",
                    new[] { ("Промышленность", 3f), ("Бюджет", -2000f) }),
                new DilOpt("Отложить", "Сэкономить, но аварии ударят по экономике.",
                    new[] { ("Экономика", -4f), ("Мораль", -2f) }),
            }),
        new("Контрабандный караван",
            "Разведка ВБ сообщает: под флагом Республики идёт караван с нелегальным грузом.",
            new[]
            {
                new DilOpt("Аннулировать транспондеры", "Заблокировать караван — удар по контрабанде.",
                    new[] { ("Угроза ВБ", -6f), ("Торговый поток", -5f) }),
                new DilOpt("Закрыть глаза", "Не вмешиваться — поток цел, но контрабанда крепнет.",
                    new[] { ("Торговый поток", 4f), ("Фальшивки", 2f) }),
            }),
        new("Забастовка регистраторов",
            "Клерки реестра требуют надбавок и грозят остановить регистрацию.",
            new[]
            {
                new DilOpt("Выплатить надбавки (1500 кр)", "Реестр работает штатно.",
                    new[] { ("Торговый поток", 6f), ("Бюджет", -1500f) }),
                new DilOpt("Отказать", "Регистрация встанет — поток обрушится.",
                    new[] { ("Торговый поток", -10f) }),
            }),
    };

    public SWBureauDilemmaEntry? BuildDilemma()
    {
        if (_dilIndex < 0 || _dilIndex >= Dilemmas.Length)
            return null;

        var def = Dilemmas[_dilIndex];
        var entry = new SWBureauDilemmaEntry
        {
            Name = def.Name,
            Description = def.Desc,
            TimeLabel = $"Решить в течение ~{MathF.Max(0f, _dilLife):0.0} ч",
        };

        foreach (var opt in def.Options)
        {
            var oe = new SWBureauDilemmaOptionEntry { Label = opt.Label, Description = opt.Desc };
            foreach (var (name, delta) in opt.Show)
                oe.Effects.Add(new SWBillEffectEntry { StatName = name, Color = ShowColor(name), Delta = delta });
            oe.Affordable = true;
            entry.Options.Add(oe);
        }

        return entry;
    }

    private static Color ShowColor(string name) => name switch
    {
        "Фальшивки" or "Угроза ВБ" => Color.FromHex("#E0556B"),
        "Бюджет" => Color.FromHex("#B7950B"),
        _ => Color.FromHex("#5DADE2"),
    };

    public bool ResolveDilemma(int optionIndex)
    {
        if (_dilIndex < 0 || _dilIndex >= Dilemmas.Length)
            return false;
        if (optionIndex < 0 || optionIndex >= Dilemmas[_dilIndex].Options.Length)
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
            case (0, 0): // flag of convenience: approve
                _tradeFlow = Math.Clamp(_tradeFlow + 12f, 0f, 100f);
                SeedForged();
                SeedForged();
                break;
            case (0, 1): // inspect
                _tradeFlow = Math.Clamp(_tradeFlow - 8f, 0f, 100f);
                _security.AddThreat(-3f);
                break;

            case (1, 0): // update charts
                if (_dilemma.SpendMinistry(BureauId, 2000))
                    _senate.AddPolicy("SWStatManufacturing", 3f);
                break;
            case (1, 1): // defer
                _senate.AddPolicy("SWStatTrade", -4f);
                _senate.AddPolicy("SWStatMorale", -2f);
                break;

            case (2, 0): // cancel smuggler transponders
                _security.AddThreat(-6f);
                _tradeFlow = Math.Clamp(_tradeFlow - 5f, 0f, 100f);
                break;
            case (2, 1): // look away
                _tradeFlow = Math.Clamp(_tradeFlow + 4f, 0f, 100f);
                SeedForged();
                SeedForged();
                break;

            case (3, 0): // pay clerks
                if (_dilemma.SpendMinistry(BureauId, 1500))
                    _tradeFlow = Math.Clamp(_tradeFlow + 6f, 0f, 100f);
                break;
            case (3, 1): // refuse
                _tradeFlow = Math.Clamp(_tradeFlow - 10f, 0f, 100f);
                break;
        }
    }

    private void FailDilemma()
    {
        // An ignored bureau problem festers: registry drift plus a small hit.
        _tradeFlow = Math.Clamp(_tradeFlow - 4f, 0f, 100f);
        _senate.AddPolicy("SWStatTrade", -1.5f);
    }

    // --- Tick ---

    public override void Update(float frameTime)
    {
        _accum += frameTime;
        if (_accum < TickInterval)
            return;

        var dt = _accum;
        _accum = 0f;

        if (_time.Day != _day)
        {
            _day = _time.Day;
            _apps.Clear();
            _dilIndex = -1;
            _dilTimer = 0f;
            _appTimer = 0f;
        }

        if (!_time.DayActive)
            return;

        var gameHours = dt * (1f / SWSenateTimeSystem.RealSecondsPerGameHour);
        var claimed = _ministry.IsClaimed(BureauId);

        DriftTradeFlow(gameHours);
        TickForged(gameHours);

        if (claimed)
        {
            TickApps(gameHours);
            TickDilemma(gameHours);
        }
    }

    private void DriftTradeFlow(float gameHours)
    {
        var target = _policy switch { 0 => 70f, 2 => 40f, _ => 55f };
        _tradeFlow += (target - _tradeFlow) * Math.Clamp(0.15f * gameHours, 0f, 1f);
        _tradeFlow = Math.Clamp(_tradeFlow, 0f, 100f);

        // Trade flow spills over into every sector's economy, up or down from a neutral 50.
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

    private void TickApps(float gameHours)
    {
        _appTimer += gameHours;
        if (_appTimer >= AppInterval && _apps.Count < AppCap)
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

    private sealed class ShipApp
    {
        public int Id;
        public string Flag = string.Empty;
        public string? OriginSector;
        public ShipRisk Risk;
        public bool Inspected;
    }

    private enum ShipRisk : byte
    {
        Clean,
        Suspicious,
        Forged,
    }
}
