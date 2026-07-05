using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared.GameTicking;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Core;

public sealed class SWDeptArbitrageSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;
    [Dependency] private readonly SWMinistryInternalSecuritySystem _security = default!;
    [Dependency] private readonly SWBureauWeaponsSystem _weapons = default!;

    public const string DeptId = "SWDepartmentArbitrage";

    private const float TickInterval = 1f;
    private const float CaseInterval = 1.5f;
    private const int CaseCap = 3;
    private static readonly int[] StageCosts = { 500, 800, 1200 };
    private static readonly string[] StageNames = { "Зацепка", "Улики", "Арест" };

    private enum CaseBonus : byte
    {
        Threat,
        Manufacturing,
        Arms,
        Trade,
        RevealPerson,
        Morale,
        Taxes,
        Treasury,
    }

    private readonly record struct CaseDef(string Name, string Desc, int Fine, CaseBonus Bonus, string BonusLabel);

    private static readonly CaseDef[] CaseDefs =
    {
        new("Казино-лайнер «Фортуна хатта»", "Отмывание кредитов через игорные палубы прогулочного лайнера.", 3000,
            CaseBonus.Threat, "угроза ВБ снизится"),
        new("Приписки на верфях", "Подрядчик выставлял счета за корабли, существующие только на бумаге.", 3500,
            CaseBonus.Manufacturing, "производство оздоровится"),
        new("Оружейный трест", "Легальные оружейные фирмы сливали партии на чёрный рынок.", 4000,
            CaseBonus.Arms, "чёрный рынок оружия просядет"),
        new("Контрабандная бухгалтерия", "Торговый дом вёл двойные книги для ухода от пошлин.", 2500,
            CaseBonus.Trade, "торговля выйдет из тени"),
        new("Взятки при госзаказе", "Чиновник распределял контракты за долю малую.", 3000,
            CaseBonus.RevealPerson, "коррупционер будет раскрыт Минюсту"),
        new("Пирамида «Звёздный дивиденд»", "Финансовая пирамида обобрала вкладчиков трёх секторов.", 2500,
            CaseBonus.Morale, "вкладчикам вернут веру"),
        new("Налоговый заговор гильдий", "Картель гильдий синхронно занижал налоговую базу.", 3500,
            CaseBonus.Taxes, "налоги придут в казну"),
        new("Фиктивные подряды", "Сеть фирм-однодневок доила республиканскую казну.", 3000,
            CaseBonus.Treasury, "казна вздохнёт свободнее"),
    };

    private float _independence = 60f;
    private int _solved;

    private readonly List<ArbCase> _cases = new();
    private int _nextCaseId = 1;
    private float _caseTimer;

    private readonly Dictionary<int, int> _opCooldowns = new();
    private readonly List<string> _log = new();

    private int _day = -1;
    private float _accum;

    public int Independence => (int) MathF.Round(_independence);
    public int Solved => _solved;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _independence = 60f;
        _solved = 0;
        _cases.Clear();
        _nextCaseId = 1;
        _caseTimer = 0f;
        _opCooldowns.Clear();
        _log.Clear();
        _day = -1;
    }

    private void AddLog(string text)
    {
        _log.Insert(0, $"[{_time.CurrentHour:00}:00] {text}");
        if (_log.Count > 8)
            _log.RemoveRange(8, _log.Count - 8);
    }

    public List<string> GetLog() => new(_log);

    private void AddIndependence(float d) => _independence = Math.Clamp(_independence + d, 0f, 100f);

    private void AddBudget(int amount) =>
        _dilemma.SetMinistryBudget(DeptId, Math.Max(0, _dilemma.GetMinistryBudget(DeptId) + amount));

    public string IndependenceStatus() => _independence switch
    {
        >= 75f => "Неподкупный трибунал",
        >= 50f => "Держим удар",
        >= 30f => "Звонки сверху",
        _ => "Марионетки",
    };

    private int CollapseChance() => Math.Clamp((int) (40f - _independence / 2f), 5, 50);

    // --- Cases ---

    public List<SWArbCaseEntry> BuildCases()
    {
        var budget = _dilemma.GetMinistryBudget(DeptId);
        var list = new List<SWArbCaseEntry>();

        foreach (var arb in _cases)
        {
            var def = CaseDefs[arb.DefIndex];
            var cost = StageCosts[arb.Stage];
            list.Add(new SWArbCaseEntry
            {
                Id = arb.Id,
                Name = def.Name,
                Description = $"{def.Desc} Штраф при аресте: {def.Fine} кр, {def.BonusLabel}.",
                Stage = arb.Stage,
                StageLabel = $"стадия {arb.Stage + 1}/3: {StageNames[arb.Stage]}",
                AdvanceLabel = arb.Stage == 2 ? "Провести арест" : $"Собрать: {StageNames[arb.Stage + 1].ToLower()}",
                AdvanceCost = cost,
                CanAdvance = budget >= cost,
                RiskLabel = $"риск давления сверху: {CollapseChance()}%",
            });
        }

        return list;
    }

    public bool AdvanceCase(int caseId)
    {
        var arb = _cases.FirstOrDefault(c => c.Id == caseId);
        if (arb == null)
            return false;
        if (!_dilemma.SpendMinistry(DeptId, StageCosts[arb.Stage]))
            return false;

        var def = CaseDefs[arb.DefIndex];

        if (_random.Prob(CollapseChance() / 100f))
        {
            _cases.Remove(arb);
            AddIndependence(-3f);
            AddLog($"«{def.Name}»: дело развалено давлением сверху.");
            return true;
        }

        arb.Stage++;
        if (arb.Stage < 3)
        {
            AddIndependence(1f);
            AddLog($"«{def.Name}»: {StageNames[arb.Stage - 1].ToLower()} в деле, стадия {arb.Stage + 1}/3.");
            return true;
        }

        _cases.Remove(arb);
        CompleteCase(def);
        return true;
    }

    private void CompleteCase(CaseDef def)
    {
        _solved++;
        AddBudget(def.Fine);
        AddIndependence(4f);
        _senate.AddPolicy("SWStatCrime", -1.5f);

        switch (def.Bonus)
        {
            case CaseBonus.Threat:
                _security.AddThreat(-3f);
                break;
            case CaseBonus.Manufacturing:
                _senate.AddPolicy("SWStatManufacturing", 2f);
                break;
            case CaseBonus.Arms:
                _weapons.ReduceArmsRandom(12f);
                break;
            case CaseBonus.Trade:
                _senate.AddPolicy("SWStatTrade", 1.5f);
                break;
            case CaseBonus.RevealPerson:
                RevealRandomOfficial();
                break;
            case CaseBonus.Morale:
                _senate.AddPolicy("SWStatMorale", 2f);
                break;
            case CaseBonus.Taxes:
                _senate.AddPolicy("SWStatTaxes", 2f);
                break;
            case CaseBonus.Treasury:
                _senate.AddPolicy("SWStatTreasury", 1.5f);
                break;
        }

        AddLog($"«{def.Name}»: арест! Штраф {def.Fine} кр взыскан.");
    }

    private void RevealRandomOfficial()
    {
        var names = new List<string>();
        foreach (var (_, holder) in _senate.SectorHolders)
            names.Add(holder);
        foreach (var (_, holder) in _ministry.Holders)
            names.Add(holder);
        if (_senate.Chancellor != null)
            names.Add(_senate.Chancellor);

        if (names.Count == 0)
            return;

        var name = _random.Pick(names);
        _corruption.Screen(false, name);
        AddLog($"Материалы дела раскрыли счета: {name} - досье передано Минюсту.");
    }

    // --- Express operations ---

    private static readonly (string Name, string Desc, int Cost, int Cd)[] Ops =
    {
        ("Защита свидетелей", "Укрыть ключевых свидетелей: независимость +10.", 800, 4),
        ("Пресс-конференция", "Публичный отчёт о делах: независимость +5, Мораль +1.", 500, 3),
        ("Спецаудит казино", "Внеплановая проверка игорных палуб: штрафы +1500 кр.", 0, 6),
        ("Реформа регламента", "Закрыть лазейки в процедурах: независимость +8, Права +1.", 1000, 6),
        ("Агент под прикрытием", "Внедрение в картель: новое дело сразу со стадии улик.", 1200, 5),
    };

    public List<SWShipsOpEntry> BuildOps()
    {
        var budget = _dilemma.GetMinistryBudget(DeptId);
        var list = new List<SWShipsOpEntry>();
        for (var i = 0; i < Ops.Length; i++)
        {
            var (name, desc, cost, _) = Ops[i];
            var entry = new SWShipsOpEntry { Id = i, Name = name, Description = desc, Cost = cost };

            if (_opCooldowns.TryGetValue(i, out var ready) && _time.CurrentHour < ready)
                entry.Status = $"откат: {ready - _time.CurrentHour} ч";
            else if (i == 4 && _cases.Count >= CaseCap)
                entry.Status = "все следователи заняты";
            else if (cost > budget)
                entry.Status = "не хватает бюджета";
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
        if (_opCooldowns.TryGetValue(idx, out var ready) && _time.CurrentHour < ready)
            return false;
        if (idx == 4 && _cases.Count >= CaseCap)
            return false;

        var (_, _, cost, cd) = Ops[idx];
        if (cost > 0 && !_dilemma.SpendMinistry(DeptId, cost))
            return false;

        _opCooldowns[idx] = _time.CurrentHour + cd;

        switch (idx)
        {
            case 0:
                AddIndependence(10f);
                AddLog("Свидетели укрыты на конспиративных квартирах.");
                break;
            case 1:
                AddIndependence(5f);
                _senate.AddPolicy("SWStatMorale", 1f);
                AddLog("Пресс-конференция: граждане видят работу арбитража.");
                break;
            case 2:
                AddBudget(1500);
                _security.AddThreat(1f);
                AddLog("Спецаудит казино: штрафы взысканы, хатты недовольны.");
                break;
            case 3:
                AddIndependence(8f);
                _senate.AddPolicy("SWStatRights", 1f);
                AddLog("Регламент реформирован: лазейки закрыты.");
                break;
            case 4:
                SpawnCase(1);
                AddLog("Агент под прикрытием принёс папку улик: новое дело открыто.");
                break;
        }

        return true;
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
            _opCooldowns.Clear();
            _caseTimer = 0f;
        }

        if (!_time.DayActive || !_ministry.IsClaimed(DeptId))
            return;

        var gameHours = dt * (1f / SWSenateTimeSystem.RealSecondsPerGameHour);

        // Political pressure gnaws at independence while corruption runs high.
        if (_corruption.GlobalIndex() >= 50)
            AddIndependence(-0.4f * gameHours);

        _caseTimer += gameHours;
        if (_caseTimer >= CaseInterval && _cases.Count < CaseCap)
        {
            _caseTimer = 0f;
            SpawnCase(0);
        }
    }

    private void SpawnCase(int startStage)
    {
        var pool = Enumerable.Range(0, CaseDefs.Length).Where(i => _cases.All(c => c.DefIndex != i)).ToList();
        if (pool.Count == 0)
            return;

        var def = _random.Pick(pool);
        _cases.Add(new ArbCase { Id = _nextCaseId++, DefIndex = def, Stage = startStage });
        AddLog($"Новое дело: «{CaseDefs[def].Name}».");
    }

    private sealed class ArbCase
    {
        public int Id;
        public int DefIndex;
        public int Stage;
    }
}
