using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Core;

public sealed class SWBureauWeaponsSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWMinistryInternalSecuritySystem _security = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;

    public const string BureauId = "SWBureauWeapons";

    public const int PolicyBan = 0;
    public const int PolicyLicense = 1;
    public const int PolicyFree = 2;

    private const float TickInterval = 1f;
    private const float DilInterval = 2f;
    private const int AppCap = 6;
    private const int InspectCost = 300;
    private const int SeizeCost = 600;
    private const int SeizeCooldownHours = 2;

    private static readonly string[] Applicants =
    {
        "«БласТех Индастриз»", "«Мерр-Сонн Мунишнс»", "«Чедак Комбайн»", "«Голан Армз»",
        "охранная фирма «Щит Тиона»", "ЧВК «Красные вымпелы»", "шахтёрская гильдия Кессела",
        "Дом Органа", "коллекционер с Кореллии", "торговый конвой «Данталус»",
    };

    private static readonly string[] Purposes =
    {
        "охрана конвоев", "тир и спортивная стрельба", "музейная коллекция",
        "самооборона колонии", "охота на пиратов", "охрана шахт", "сопровождение дипмиссий",
    };

    private static readonly string[] CleanDossiers =
    {
        "чистая история поставок", "лицензии продлевались без нареканий", "поручительство сектора",
    };

    private static readonly string[] ShadyDossiers =
    {
        "связи с картелями не доказаны, но и не опровергнуты", "часть прошлых партий не найдена",
        "учредители скрываются за подставными счетами",
    };

    private static readonly string[] FrontDossiers =
    {
        "подставная фирма хаттского картеля", "закупщик сепаратистских ополчений",
        "перекупщик с чёрного рынка Нар-Шаддаа", "агентура имперских снабженцев",
    };

    private float _control = 55f;
    private readonly int[] _policy = { PolicyLicense, PolicyLicense, PolicyLicense };
    private readonly Dictionary<string, float> _arms = new();
    private readonly Dictionary<string, double> _seizeCooldown = new();

    private readonly List<ArmsApp> _apps = new();
    private int _nextAppId = 1;
    private readonly SWSenateWorkQueue _arrivalQueue = new(.625f, .875f);

    private int _registryDroids;
    private int _securityDroids;

    private readonly Dictionary<int, double> _opCooldowns = new();
    private readonly List<string> _log = new();
    private float _income;

    private int _dilIndex = -1;
    private readonly SWSenateWorkQueue _dilemmaQueue = new(3f, 4f, 1.5f, 2.5f);
    private float _dilLife;

    private int _day = -1;
    private float _accum;

    public int Control => (int) MathF.Round(_control);
    public int RegistryDroids => _registryDroids;
    public int SecurityDroids => _securityDroids;
    public int GetPolicy(int category) => category is >= 0 and <= 2 ? _policy[category] : PolicyLicense;

    public float ArmsLevel(string sectorId) => _arms.TryGetValue(sectorId, out var v) ? v : 0f;

    public void ReduceArms(string sectorId, float amount) =>
        _arms[sectorId] = Math.Clamp(ArmsLevel(sectorId) - MathF.Abs(amount), 0f, 100f);

    public void ReduceArmsRandom(float amount)
    {
        var claimed = _senate.SectorHolders.Keys.ToList();
        if (claimed.Count > 0)
            ReduceArms(_random.Pick(claimed), amount);
    }

    public int BlackMarket()
    {
        var claimed = _senate.SectorHolders.Keys.ToList();
        if (claimed.Count == 0)
            return 0;
        return (int) MathF.Round(claimed.Sum(s => ArmsLevel(s)) / claimed.Count);
    }

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _accum = 0f;
        _control = 55f;
        _policy[0] = _policy[1] = _policy[2] = PolicyLicense;
        _arms.Clear();
        _seizeCooldown.Clear();
        _apps.Clear();
        _nextAppId = 1;
        _arrivalQueue.Reset();
        _registryDroids = 0;
        _securityDroids = 0;
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

    private void AddControl(float d) => _control = Math.Clamp(_control + d, 0f, 100f);

    private void AddArms(string sectorId, float d) =>
        _arms[sectorId] = Math.Clamp(ArmsLevel(sectorId) + d, 0f, 100f);

    private void AddArmsAll(float d)
    {
        foreach (var sectorId in _senate.SectorHolders.Keys.ToList())
            AddArms(sectorId, d);
    }

    private void AddArmsRandom(float d)
    {
        var claimed = _senate.SectorHolders.Keys.ToList();
        if (claimed.Count > 0)
            AddArms(_random.Pick(claimed), d);
    }

    private void AddBudget(int amount) =>
        _dilemma.SetMinistryBudget(BureauId, Math.Max(0, _dilemma.GetMinistryBudget(BureauId) + amount));

    public string ControlStatus() => _control switch
    {
        >= 75f => "Железный учёт",
        >= 50f => "Рабочий контроль",
        >= 30f => "Недостатки учёта",
        _ => "Контроль утрачен",
    };

    public string BlackMarketStatus() => BlackMarket() switch
    {
        >= 70 => "Высокое насыщение вооружением",
        >= 50 => "Активный чёрный рынок",
        >= 30 => "Ограниченная теневая торговля",
        _ => "Рынок подавлен",
    };

    public static string PolicyName(int level) => level switch
    {
        PolicyBan => "Запрет",
        PolicyFree => "Свободно",
        _ => "Лицензии",
    };

    public static string CategoryName(int category) => category switch
    {
        0 => "Гражданское оружие",
        1 => "Тяжёлое вооружение",
        _ => "Боевые дроиды",
    };

    public void SetPolicy(int category, int level)
    {
        if (category is < 0 or > 2 || level is < PolicyBan or > PolicyFree)
            return;

        if (_policy[category] == level)
            return;

        _policy[category] = level;
        AddLog($"{CategoryName(category)}: режим «{PolicyName(level)}».");
    }

    public int LicenseIncome()
    {
        var licensed = _policy.Count(p => p == PolicyLicense);
        return (int) MathF.Round(licensed * 12f + _control / 10f);
    }

    // --- License applications ---

    private void SpawnApp()
    {
        var open = Enumerable.Range(0, 3).Where(c => _policy[c] == PolicyLicense).ToList();
        if (open.Count == 0)
            return;

        var category = _random.Pick(open);
        var roll = _random.NextFloat();
        var risk = roll < 0.2f ? AppRisk.Front : roll < 0.45f ? AppRisk.Shady : AppRisk.Clean;

        var qty = 10 + _random.Next(6) * 10;
        var baseFee = category switch { 0 => 250, 1 => 500, _ => 700 };

        var app = new ArmsApp
        {
            Id = _nextAppId++,
            Applicant = _random.Pick(Applicants),
            Category = category,
            Purpose = _random.Pick(Purposes),
            Quantity = qty,
            Fee = baseFee + qty * 8,
            Risk = risk,
            Dossier = risk switch
            {
                AppRisk.Front => _random.Pick(FrontDossiers),
                AppRisk.Shady => _random.Pick(ShadyDossiers),
                _ => _random.Pick(CleanDossiers),
            },
        };

        if (risk != AppRisk.Clean && _random.Prob(0.4f))
            app.Bribe = 700 + _random.Next(9) * 100;

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
                AddControl(1f);
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
                _corruption.TakeBribe(holder, null, app.Bribe);
                Approve(app, true);
                AddControl(-3f);
                AddLog($"Партия {app.Applicant} оформлена без лишних вопросов.");
                return true;

            default:
                return false;
        }
    }

    private void Approve(ArmsApp app, bool bribed)
    {
        _apps.Remove(app);
        AddBudget(app.Fee);

        switch (app.Risk)
        {
            case AppRisk.Clean:
                AddControl(1f);
                if (app.Category == 1)
                    _senate.AddPolicy("SWStatReserve", 0.5f);
                break;

            case AppRisk.Shady:
                if (_random.Prob(0.5f))
                {
                    AddArmsRandom(4f);
                    AddControl(-2f);
                }
                break;

            default:
                AddArmsRandom(8f);
                _security.AddThreat(2f);
                AddControl(-4f);
                if (!bribed)
                    AddLog($"Партия {app.Applicant} растворилась на чёрном рынке.");
                break;
        }
    }

    private void Deny(ArmsApp app)
    {
        _apps.Remove(app);

        if (app.Risk == AppRisk.Clean)
        {
            _senate.AddPolicy("SWStatManufacturing", -0.5f);
        }
        else
        {
            AddControl(app.Risk == AppRisk.Front ? 3f : 2f);
            if (app.Risk == AppRisk.Front)
            {
                _security.AddThreat(-1f);
                AddLog($"Подставная фирма {app.Applicant} внесена в чёрный список.");
            }
        }
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
                Name = $"{app.Applicant}, {CategoryName(app.Category)}",
                Origin = $"назначение: {app.Purpose}, партия: {app.Quantity} ед., пошлина: {app.Fee} кр",
                RiskLabel = app.Inspected ? RiskLabel(app.Risk) : "не проверено",
                RiskColor = app.Inspected ? RiskColor(app.Risk) : Color.FromHex("#888888"),
                DossierLabel = app.Inspected ? app.Dossier : string.Empty,
                Inspected = app.Inspected,
                InspectCost = InspectCost,
                CanInspect = !app.Inspected && budget >= InspectCost,
                Fee = app.Fee,
                Bribe = app.Bribe,
            });
        }

        return list;
    }

    private static string RiskLabel(AppRisk risk) => risk switch
    {
        AppRisk.Clean => "Надёжный",
        AppRisk.Shady => "Сомнительный",
        _ => "Подставной",
    };

    private static Color RiskColor(AppRisk risk) => risk switch
    {
        AppRisk.Clean => Color.FromHex("#43D16A"),
        AppRisk.Shady => Color.FromHex("#E0A23B"),
        _ => Color.FromHex("#E0556B"),
    };

    // --- Sector arms board & seizure ---

    public List<SWArmsSectorEntry> BuildSectors()
    {
        var budget = _dilemma.GetMinistryBudget(BureauId);
        var joint = _ministry.IsClaimed(SWMinistryInternalSecuritySystem.SecurityMinistryId);
        var list = new List<SWArmsSectorEntry>();

        foreach (var (sectorId, _) in _senate.SectorHolders)
        {
            if (!_proto.TryIndex<SWSectorPrototype>(sectorId, out var proto))
                continue;

            var arms = (int) MathF.Round(ArmsLevel(sectorId));
            var onCooldown = _seizeCooldown.TryGetValue(sectorId, out var ready) && _time.ActiveGameHours < ready;

            list.Add(new SWArmsSectorEntry
            {
                Id = sectorId,
                Name = proto.Name,
                Color = proto.Color,
                Arms = arms,
                ArmsLabel = arms switch
                {
                    >= 70 => "арсенал у каждого",
                    >= 40 => "стволы в подворотнях",
                    >= 15 => "редкие стычки",
                    _ => "чисто",
                },
                SeizeCost = SeizeCost,
                CanSeize = !onCooldown && budget >= SeizeCost,
                SeizeLabel = onCooldown ? $"готовность через {SWSenateDuration.FormatHours(ready - _time.ActiveGameHours)}"
                    : joint ? "Изъятие (совместно с ВБ)" : "Изъятие",
            });
        }

        list.Sort((a, b) => b.Arms.CompareTo(a.Arms));
        return list;
    }

    public bool Seize(string sectorId)
    {
        if (!_senate.IsClaimed(sectorId))
            return false;
        if (_seizeCooldown.TryGetValue(sectorId, out var ready) && _time.ActiveGameHours < ready)
            return false;
        if (!_dilemma.SpendMinistry(BureauId, SeizeCost))
            return false;

        _seizeCooldown[sectorId] = _time.ActiveGameHours + SeizeCooldownHours;
        AddArms(sectorId, -15f);
        _senate.AddPolicy("SWStatRights", -0.5f);

        var name = SectorName(sectorId);
        if (_ministry.IsClaimed(SWMinistryInternalSecuritySystem.SecurityMinistryId))
        {
            _security.DampPlots(sectorId, -12f);
            _security.AddThreat(-1.5f);
            AddLog($"Совместное с ВБ изъятие в {name}: склады вскрыты, заговорщики без оружия.");
        }
        else
        {
            AddLog($"Изъятие оружия в {name}.");
        }

        return true;
    }

    private string SectorName(string sectorId) =>
        _proto.TryIndex<SWSectorPrototype>(sectorId, out var proto) ? proto.Name : sectorId;

    // --- Droid registry ---

    public bool DroidAction(int action)
    {
        switch (action)
        {
            case 0:
                if (_registryDroids <= 0)
                    return false;
                _registryDroids--;
                _senate.AddPolicy("SWStatReserve", 2f);
                _senate.AddPolicy("SWStatArmy", 1f);
                AddLog("Партия боевых дроидов передана Обороне.");
                return true;

            case 1:
                if (_registryDroids <= 0 || _securityDroids >= 5)
                    return false;
                _registryDroids--;
                _securityDroids++;
                AddLog("Партия дроидов придана МВБ: облавы станут точнее.");
                return true;

            case 2:
                if (_securityDroids <= 0)
                    return false;
                _securityDroids--;
                _registryDroids++;
                AddLog("Партия дроидов отозвана из МВБ в резерв бюро.");
                return true;

            default:
                return false;
        }
    }

    // --- Express operations ---

    private static readonly (string Name, string Desc, int Cost, int Cd)[] Ops =
    {
        ("Оружейная амнистия", "Выкуп стволов у населения: чёрный рынок по всем секторам -10.", 1500, 6),
        ("Маркировка партий", "Трассировка каждого ствола: контроль оборота +15.", 800, 4),
        ("Совместные рейды с ВБ", "Синхронный удар по складам во всех секторах. Требует активного МВБ.", 1200, 5),
        ("Аукцион конфиската", "Распродать изъятое: +1800 кр, но часть уйдёт не в те руки.", 0, 6),
        ("Гос-программа дроидов", "Заказ у верфей: +2 партии боевых дроидов в реестр бюро.", 2000, 8),
        ("Сертификация ЧВК", "Ускоренные лицензии частникам: резерв растёт, риски тоже.", 600, 4),
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
            else if (i == 2 && !_ministry.IsClaimed(SWMinistryInternalSecuritySystem.SecurityMinistryId))
                entry.Status = "требуется активное МВБ";
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
        if (idx == 2 && !_ministry.IsClaimed(SWMinistryInternalSecuritySystem.SecurityMinistryId))
            return false;

        var (_, _, cost, cd) = Ops[idx];
        if (cost > 0 && !_dilemma.SpendMinistry(BureauId, cost))
            return false;

        _opCooldowns[idx] = _time.ActiveGameHours + cd;

        switch (idx)
        {
            case 0:
                AddArmsAll(-10f);
                _senate.AddPolicy("SWStatMorale", 1f);
                _senate.AddPolicy("SWStatRights", 1f);
                AddLog("Оружейная амнистия: пункты выкупа завалены стволами.");
                break;
            case 1:
                AddControl(15f);
                AddLog("Все новые партии получили трассировочные метки.");
                break;
            case 2:
                AddArmsAll(-6f);
                _security.AddThreat(-3f);
                _senate.AddPolicy("SWStatRights", -2f);
                foreach (var sectorId in _senate.SectorHolders.Keys.ToList())
                    _security.DampPlots(sectorId, -5f);
                AddLog("Совместные рейды с ВБ: склады контрабандистов пусты.");
                break;
            case 3:
                AddBudget(1800);
                AddControl(-5f);
                AddArmsRandom(5f);
                AddLog("Аукцион конфиската: касса полна, часть лотов ушла в тень.");
                break;
            case 4:
                _registryDroids += 2;
                AddLog("Верфи поставили две партии боевых дроидов в реестр.");
                break;
            case 5:
                _senate.AddPolicy("SWStatReserve", 3f);
                _security.AddThreat(1f);
                AddLog("ЧВК сертифицированы по ускоренной программе.");
                break;
        }

        return true;
    }

    // --- Bureau dilemmas ---

    private readonly record struct DilOpt(string Label, string Desc, int Cost, int Bribe, (string Name, float Delta)[] Show);
    private readonly record struct DilDef(string Name, string Desc, DilOpt[] Options);

    private static readonly DilDef[] Dilemmas =
    {
        new("Оружейный барон",
            "Посредник оружейного барона просит оптовую лицензию на «сельскохозяйственную технику». Ящики подозрительно лязгают.",
            new[]
            {
                new DilOpt("Оформить без досмотра", "Барон умеет благодарить.", 0, 1200,
                    new[] { ("Контроль", -6f), ("Чёрный рынок", 6f) }),
                new DilOpt("Отказать и внести в чёрный список", "Заявитель будет исключён из будущих процедур лицензирования.", 0, 0,
                    new[] { ("Контроль", 3f), ("Угроза ВБ", -2f) }),
            }),
        new("Кризис «БласТех»",
            "Крупнейший оружейный завод Республики на грани закрытия: госзаказов нет, рабочие бастуют.",
            new[]
            {
                new DilOpt("Субсидировать", "Сохранить производство и рабочие места.", 1200,  0,
                    new[] { ("Производство", 3f), ("Резерв", 2f) }),
                new DilOpt("Не вмешиваться", "Без поддержки мастерская уйдёт в теневой сектор.", 0, 0,
                    new[] { ("Производство", -3f), ("Чёрный рынок", 5f) }),
            }),
        new("Реестр световых мечей",
            "Фракция сенаторов требует внести джедайское оружие в общий реестр вооружений.",
            new[]
            {
                new DilOpt("Поддержать инициативу", "Закон один для всех. Орден будет в ярости.", 0, 0,
                    new[] { ("Контроль", 4f), ("Договоры", -2f) }),
                new DilOpt("Отклонить", "Оружие Ордена остаётся вне общего реестра.", 0, 0,
                    new[] { ("Мораль", 1f), ("Договоры", 1f) }),
            }),
        new("Выставка вооружений",
            "Оружейные дома просят провести галактическую выставку под эгидой бюро.",
            new[]
            {
                new DilOpt("Провести", "Пошлины, контракты и полный зал покупателей.", 800, 0,
                    new[] { ("Казна бюро", 2000f), ("Производство", 2f), ("Угроза ВБ", 2f) }),
                new DilOpt("Отменить", "Не время для оружейных салонов.", 0, 0,
                    new[] { ("Производство", -1f) }),
            }),
        new("Дроид-ветеран",
            "Боевой дроид прошлой войны, обретший разум, просит легальный статус вместо переплавки.",
            new[]
            {
                new DilOpt("Легализовать", "Решение создаст правовой прецедент и даст реестру нового сотрудника.", 0, 0,
                    new[] { ("Права", 2f), ("Мораль", 1f), ("Дроиды", 1f) }),
                new DilOpt("Утилизировать", "Закон о боевых машинах требует утилизации.", 0, 0,
                    new[] { ("Права", -2f), ("Мораль", -1f) }),
            }),
        new("Ополчение Внешнего Кольца",
            "Приграничный сектор просит партию оружия для ополчения - пираты сожгли три поселения.",
            new[]
            {
                new DilOpt("Выдать партию", "Вооружение повысит выживаемость колоний, но усилит неконтролируемый оборот.", 0, 0,
                    new[] { ("Сектор", 4f), ("Чёрный рынок", 8f) }),
                new DilOpt("Отказать", "Отказ снизит риск утечки оружия в нелегальный оборот.", 0, 0,
                    new[] { ("Сектор", -2f) }),
            }),
        new("Утечка чертежей",
            "Чертежи армейского бластера утекли в голонет. Реплики уже печатают в подпольных цехах.",
            new[]
            {
                new DilOpt("Отозвать серию и перевыпустить", "Дорого, но перекроет реплики.", 1000, 0,
                    new[] { ("Контроль", 8f) }),
                new DilOpt("Игнорировать", "Реплики разойдутся по рукам.", 0, 0,
                    new[] { ("Чёрный рынок", 5f), ("Угроза ВБ", 2f) }),
            }),
        new("Имперский заказ",
            "Сеть подставных фирм скупает тяжёлое вооружение. След ведёт к имперским снабженцам.",
            new[]
            {
                new DilOpt("Заблокировать сделки", "Империя останется без республиканских стволов.", 0, 0,
                    new[] { ("Угроза ВБ", -2f), ("Договоры", -1f) }),
                new DilOpt("Продать по завышенной цене", "Продажа пополнит казну бюро, но усилит серый оборот.", 0, 0,
                    new[] { ("Казна бюро", 2500f), ("Угроза ВБ", 3f), ("Контроль", -5f) }),
            }),
        new("Коллекция сенатора",
            "Влиятельный сенатор просит лицензию на частный арсенал «исторического значения».",
            new[]
            {
                new DilOpt("Выдать лицензию", "Дружба с сенатором дороже формальностей.", 0, 0,
                    new[] { ("Сектор", 2f), ("Контроль", -3f) }),
                new DilOpt("Отказать", "Перед реестром все равны.", 0, 0,
                    new[] { ("Сектор", -2f), ("Контроль", 2f) }),
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

        return entry;
    }

    private static Color ShowColor(string name, float delta)
    {
        if (name == "Казна бюро")
            return Color.FromHex("#B7950B");

        var inverted = name is "Угроза ВБ" or "Чёрный рынок";
        var good = inverted ? delta < 0f : delta > 0f;
        return good ? Color.FromHex("#43D16A") : Color.FromHex("#E0556B");
    }

    public bool ResolveDilemma(int optionIndex)
    {
        if (_dilIndex < 0 || _dilIndex >= Dilemmas.Length)
            return false;
        if (optionIndex < 0 || optionIndex >= Dilemmas[_dilIndex].Options.Length)
            return false;

        var opt = Dilemmas[_dilIndex].Options[optionIndex];
        if (opt.Cost > _dilemma.GetMinistryBudget(BureauId))
            return false;

        if (opt.Bribe > 0)
        {
            if (_ministry.GetHolder(BureauId) is not { } holder)
                return false;
            _corruption.TakeBribe(holder, null, opt.Bribe);
        }

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
                AddControl(-6f);
                AddArmsRandom(6f);
                break;
            case (0, 1):
                AddControl(3f);
                _security.AddThreat(-2f);
                break;

            case (1, 0):
                if (_dilemma.SpendMinistry(BureauId, 1200))
                {
                    _senate.AddPolicy("SWStatManufacturing", 3f);
                    _senate.AddPolicy("SWStatReserve", 2f);
                    AddLog("«БласТех» спасён субсидией бюро.");
                }
                break;
            case (1, 1):
                _senate.AddPolicy("SWStatManufacturing", -3f);
                AddArmsAll(3f);
                break;

            case (2, 0):
                AddControl(4f);
                _senate.AddPolicy("SWStatTreaties", -2f);
                AddLog("Реестр световых мечей утверждён. Орден подал протест.");
                break;
            case (2, 1):
                _senate.AddPolicy("SWStatMorale", 1f);
                _senate.AddPolicy("SWStatTreaties", 1f);
                break;

            case (3, 0):
                if (_dilemma.SpendMinistry(BureauId, 800))
                {
                    AddBudget(2000);
                    _senate.AddPolicy("SWStatManufacturing", 2f);
                    _security.AddThreat(2f);
                    AddLog("Выставка вооружений прошла с аншлагом.");
                }
                break;
            case (3, 1):
                _senate.AddPolicy("SWStatManufacturing", -1f);
                break;

            case (4, 0):
                _senate.AddPolicy("SWStatRights", 2f);
                _senate.AddPolicy("SWStatMorale", 1f);
                _registryDroids++;
                AddLog("Дроид-ветеран легализован и зачислен в реестр бюро.");
                break;
            case (4, 1):
                _senate.AddPolicy("SWStatRights", -2f);
                _senate.AddPolicy("SWStatMorale", -1f);
                break;

            case (5, 0):
                var claimed = _senate.SectorHolders.Keys.ToList();
                if (claimed.Count > 0)
                {
                    var sector = _random.Pick(claimed);
                    _senate.AddSectorBias(sector, "SWGlobalSecurity", 4f);
                    AddArms(sector, 8f);
                    AddLog($"Ополчение {SectorName(sector)} вооружено со складов бюро.");
                }
                break;
            case (5, 1):
                var claimed2 = _senate.SectorHolders.Keys.ToList();
                if (claimed2.Count > 0)
                    _senate.AddSectorBias(_random.Pick(claimed2), "SWGlobalSociety", -2f);
                break;

            case (6, 0):
                if (_dilemma.SpendMinistry(BureauId, 1000))
                {
                    AddControl(8f);
                    AddLog("Серия отозвана, реплики перекрыты новым стандартом.");
                }
                break;
            case (6, 1):
                AddArmsAll(5f);
                _security.AddThreat(2f);
                break;

            case (7, 0):
                _security.AddThreat(-2f);
                _senate.AddPolicy("SWStatTreaties", -1f);
                AddLog("Сделки имперских снабженцев заблокированы.");
                break;
            case (7, 1):
                AddBudget(2500);
                _security.AddThreat(3f);
                AddControl(-5f);
                break;

            case (8, 0):
                var claimed3 = _senate.SectorHolders.Keys.ToList();
                if (claimed3.Count > 0)
                    _senate.AddSectorBias(_random.Pick(claimed3), "SWGlobalSociety", 2f);
                AddControl(-3f);
                break;
            case (8, 1):
                var claimed4 = _senate.SectorHolders.Keys.ToList();
                if (claimed4.Count > 0)
                    _senate.AddSectorBias(_random.Pick(claimed4), "SWGlobalSociety", -2f);
                AddControl(2f);
                break;
        }
    }

    private void FailDilemma()
    {
        AddControl(-3f);
        AddArmsRandom(4f);
        AddLog("Нерешённый вопрос бюро утёк в тень вместе с оружием.");
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

        TickArms(gameHours);
        TickArmsEffects(gameHours);

        if (!claimed)
            return;

        TickIncome(gameHours);
        TickApps(gameHours);
        TickDilemma(gameHours);
        TickDroidTheft(gameHours);
    }

    private void TickArms(float gameHours)
    {
        var growth = 0.15f
            + _policy[0] switch { PolicyBan => 0.4f, PolicyFree => 0.5f, _ => 0f }
            + (_policy[1] == PolicyFree ? 0.8f : 0f)
            + (_policy[2] == PolicyFree ? 0.5f : 0f)
            + _security.Threat / 100f * 0.4f
            - _control / 100f * 0.7f;

        foreach (var sectorId in _senate.SectorHolders.Keys.ToList())
            AddArms(sectorId, growth * gameHours);

        foreach (var sectorId in _arms.Keys.ToList())
        {
            if (!_senate.IsClaimed(sectorId))
                _arms.Remove(sectorId);
        }

        if (_policy[0] == PolicyBan)
            _senate.AddPolicy("SWStatRights", -0.04f * gameHours);
        else if (_policy[0] == PolicyFree)
            _senate.AddPolicy("SWStatRights", 0.03f * gameHours);

        if (_policy[1] == PolicyFree)
            _senate.AddPolicy("SWStatReserve", 0.04f * gameHours);
    }

    private void TickArmsEffects(float gameHours)
    {
        foreach (var (sectorId, level) in _arms.ToList())
        {
            if (!_senate.IsClaimed(sectorId) || level < 60f)
                continue;

            _senate.AddSectorBias(sectorId, "SWGlobalSecurity", -0.2f * gameHours);
            _senate.AddSectorBias(sectorId, "SWGlobalSociety", -0.1f * gameHours);

            if (level >= 80f && _random.Prob(Math.Clamp(0.08f * gameHours, 0f, 1f)))
            {
                _senate.AddPolicy("SWStatMorale", -1.5f);
                AddLog($"Перестрелка в {SectorName(sectorId)}: чёрный рынок диктует правила.");
            }
        }
    }

    private void TickIncome(float gameHours)
    {
        _income += LicenseIncome() * gameHours;
        if (_income < 1f)
            return;

        var due = (int) _income;
        _income -= due;
        AddBudget(due);
    }

    private void TickApps(float gameHours)
    {
        if (!_arrivalQueue.Tick(_random, gameHours, _apps.Count + (_dilIndex >= 0 ? 2 : 0) + _dilemma.PendingWork(BureauId), AppCap))
            return;
        SpawnApp();
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
            _apps.Count + _dilemma.PendingWork(BureauId), 4))
            return;
        _dilIndex = _random.Next(Dilemmas.Length);
        _dilLife = DilInterval;
    }

    private void TickDroidTheft(float gameHours)
    {
        if (_registryDroids <= 0 || BlackMarket() < 60)
            return;

        if (!_random.Prob(Math.Clamp(0.05f * gameHours, 0f, 1f)))
            return;

        _registryDroids--;
        AddArmsRandom(6f);
        _security.AddThreat(3f);
        AddLog("Партия дроидов похищена со складов реестра.");
    }

    private sealed class ArmsApp
    {
        public int Id;
        public string Applicant = string.Empty;
        public int Category;
        public string Purpose = string.Empty;
        public int Quantity;
        public int Fee;
        public AppRisk Risk;
        public string Dossier = string.Empty;
        public bool Inspected;
        public int Bribe;
    }

    private enum AppRisk : byte
    {
        Clean,
        Shady,
        Front,
    }
}
