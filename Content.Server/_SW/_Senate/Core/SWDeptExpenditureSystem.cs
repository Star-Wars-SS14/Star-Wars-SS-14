using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Core;

public sealed class SWDeptExpenditureSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;

    public const string DeptId = "SWDepartmentExpenditure";

    private const float TickInterval = 1f;
    private const int TargetAuditCost = 400;
    private const float TipInterval = 0.7f;
    private const int TipCap = 3;
    private const float TipLife = 2f;
    private const float TransparencyDecay = 0.6f;
    public const int TransparencyBonusThreshold = 70;

    private enum Anomaly : byte
    {
        Clean,
        Padding,
        Embezzlement,
    }

    private readonly record struct TipDef(string Name, string Desc, int Bribe);

    private static readonly TipDef[] Tips =
    {
        new("Закупки по завышенным ценам", "Аноним сообщает: расходники берут втридорога у «своих» фирм.", 0),
        new("Мёртвые души в ведомостях", "На довольствии числятся давно уволенные сотрудники.", 800),
        new("Банкет за казённый счёт", "Приём «для служебных нужд» на роскошной палубе.", 0),
        new("Двойные командировочные", "Одни и те же перелёты оплачены дважды.", 700),
        new("Исчезнувший груз снабжения", "Партия оборудования растворилась между складами.", 900),
        new("Аренда у родственников", "Ведомство арендует офисы у семьи чиновника по тройной ставке.", 0),
    };

    private float _transparency = 50f;
    private int _recovered;

    private readonly Dictionary<string, Anomaly> _anomalies = new();
    private readonly HashSet<string> _audited = new();

    private readonly List<ActiveTip> _tips = new();
    private int _nextTipId = 1;
    private float _tipTimer;

    private readonly Dictionary<int, int> _opCooldowns = new();
    private readonly List<string> _log = new();

    private int _day = -1;
    private float _accum;

    public int Transparency => (int) MathF.Round(_transparency);
    public int Recovered => _recovered;
    public bool TransparencyBonusActive => _transparency >= TransparencyBonusThreshold;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _transparency = 50f;
        _recovered = 0;
        _anomalies.Clear();
        _audited.Clear();
        _tips.Clear();
        _nextTipId = 1;
        _tipTimer = 0f;
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

    private void AddTransparency(float d) => _transparency = Math.Clamp(_transparency + d, 0f, 100f);

    private void AddBudget(int amount) =>
        _dilemma.SetMinistryBudget(DeptId, Math.Max(0, _dilemma.GetMinistryBudget(DeptId) + amount));

    public string TransparencyStatus() => _transparency switch
    {
        >= 70f => "Стеклянная казна (+10% к бюджету дня)",
        >= 45f => "Терпимо",
        >= 25f => "Мутные ведомости",
        _ => "Чёрная дыра",
    };

    // --- Ledger (audit board) ---

    private Anomaly AnomalyFor(string targetId)
    {
        if (!_anomalies.TryGetValue(targetId, out var anomaly))
        {
            var roll = _random.NextFloat();
            anomaly = roll < 0.5f ? Anomaly.Clean : roll < 0.82f ? Anomaly.Padding : Anomaly.Embezzlement;
            _anomalies[targetId] = anomaly;
        }

        return anomaly;
    }

    public List<SWAuditRowEntry> BuildLedger()
    {
        var budget = _dilemma.GetMinistryBudget(DeptId);
        var list = new List<SWAuditRowEntry>();

        foreach (var (targetId, _) in _ministry.Holders)
        {
            if (targetId == DeptId || !_proto.TryIndex<SWMinistryPrototype>(targetId, out var proto))
                continue;

            var audited = _audited.Contains(targetId);
            list.Add(new SWAuditRowEntry
            {
                TargetId = targetId,
                Name = proto.Name,
                Color = proto.Color,
                Budget = _dilemma.GetMinistryBudget(targetId),
                StatusLabel = audited ? "проверено сегодня" : "не проверено",
                StatusGood = audited,
                AuditCost = TargetAuditCost,
                CanAudit = !audited && budget >= TargetAuditCost,
            });
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        return list;
    }

    public bool AuditTarget(string targetId)
    {
        if (targetId == DeptId || !_ministry.IsClaimed(targetId) || _audited.Contains(targetId))
            return false;
        if (!_dilemma.SpendMinistry(DeptId, TargetAuditCost))
            return false;

        _audited.Add(targetId);
        var name = _proto.TryIndex<SWMinistryPrototype>(targetId, out var proto) ? proto.Name : targetId;

        switch (AnomalyFor(targetId))
        {
            case Anomaly.Clean:
                AddTransparency(2f);
                AddLog($"{name}: отчётность чиста.");
                break;

            case Anomaly.Padding:
                var target = _dilemma.GetMinistryBudget(targetId);
                var recover = Math.Max(200, (int) (target * (_auditBoost ? 0.25f : 0.15f)));
                recover = Math.Min(recover, target);
                _dilemma.SetMinistryBudget(targetId, target - recover);
                AddBudget(recover);
                _recovered += recover;
                AddTransparency(3f);
                AddLog($"{name}: вскрыты приписки, возвращено {recover} кр.");
                break;

            default:
                AddTransparency(4f);
                if (_ministry.GetHolder(targetId) is { } holder)
                {
                    _corruption.Screen(false, holder);
                    AddLog($"{name}: следы хищений. Досье на {holder} передано Минюсту.");
                    RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Департамент учёта расходов",
                        $"Аудит {name}: выявлены признаки хищений, материалы переданы Министерству юстиции."));
                }
                break;
        }

        return true;
    }

    // --- Whistleblower tips ---

    public List<SWTransitEventEntry> BuildTips()
    {
        var budget = _dilemma.GetMinistryBudget(DeptId);
        var list = new List<SWTransitEventEntry>();

        foreach (var tip in _tips)
        {
            var def = Tips[tip.DefIndex];
            var entry = new SWTransitEventEntry
            {
                Id = tip.Id,
                Name = $"Сигнал: {def.Name}",
                Description = def.Desc,
                TimeLabel = $"След остынет через ~{MathF.Max(0f, tip.LifeHours):0.0} ч",
            };

            entry.Options.Add(new SWBureauDilemmaOptionEntry
            {
                Label = "Проверить сигнал",
                Description = "Ревизоры выедут на место. Подтверждение вернёт деньги в казну.",
                Cost = 300,
                Affordable = budget >= 300,
            });

            if (def.Bribe > 0)
            {
                entry.Options.Add(new SWBureauDilemmaOptionEntry
                {
                    Label = $"Закрыть глаза (+{def.Bribe} кр лично)",
                    Description = "Нарушитель предлагает решить вопрос тихо.",
                    Affordable = true,
                    Corrupt = true,
                });
            }

            list.Add(entry);
        }

        return list;
    }

    public bool ResolveTip(int tipId, int optionIndex)
    {
        var tip = _tips.FirstOrDefault(t => t.Id == tipId);
        if (tip == null)
            return false;

        var def = Tips[tip.DefIndex];

        if (optionIndex == 1)
        {
            if (def.Bribe <= 0 || _ministry.GetHolder(DeptId) is not { } holder)
                return false;
            _tips.Remove(tip);
            _corruption.TakeBribe(holder, null, def.Bribe);
            AddTransparency(-5f);
            AddLog($"Сигнал «{def.Name}» закрыт без проверки.");
            return true;
        }

        if (!_dilemma.SpendMinistry(DeptId, 300))
            return false;

        _tips.Remove(tip);
        if (_random.Prob(0.6f))
        {
            var recover = 600 + _random.Next(5) * 200;
            AddBudget(recover);
            _recovered += recover;
            AddTransparency(3f);
            AddLog($"Сигнал «{def.Name}» подтвердился: возвращено {recover} кр.");
        }
        else
        {
            AddTransparency(1f);
            AddLog($"Сигнал «{def.Name}» не подтвердился.");
        }

        return true;
    }

    private void FailTip(ActiveTip tip)
    {
        AddTransparency(-2f);
        AddLog($"Сигнал «{Tips[tip.DefIndex].Name}» остыл: следы заметены.");
    }

    // --- Express operations ---

    private static readonly (string Name, string Desc, int Cost, int Cd)[] Ops =
    {
        ("Публикация отчёта", "Открытый отчёт о расходах: прозрачность +10, Мораль +1.", 600, 4),
        ("Цифровизация учёта", "Единая система проводок: прозрачность +15.", 1500, 6),
        ("Внеплановая ревизия", "Снять по 5% с бюджетов всех ведомств в счёт проверки — вернётся, что найдём.", 0, 6),
        ("Горячая линия", "Поощрение заявителей: сигналы будут поступать чаще.", 500, 5),
        ("Премии ревизорам", "Мотивация штата: прозрачность +5, следующие аудиты острее.", 800, 4),
    };

    private float _tipBoostHours;
    private bool _auditBoost;

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

        var (_, _, cost, cd) = Ops[idx];
        if (cost > 0 && !_dilemma.SpendMinistry(DeptId, cost))
            return false;

        _opCooldowns[idx] = _time.CurrentHour + cd;

        switch (idx)
        {
            case 0:
                AddTransparency(10f);
                _senate.AddPolicy("SWStatMorale", 1f);
                AddLog("Отчёт о расходах опубликован в голонете.");
                break;
            case 1:
                AddTransparency(15f);
                AddLog("Учёт переведён на единую цифровую систему.");
                break;
            case 2:
                var total = 0;
                foreach (var (targetId, _) in _ministry.Holders)
                {
                    if (targetId == DeptId)
                        continue;
                    var b = _dilemma.GetMinistryBudget(targetId);
                    var cut = (int) (b * 0.05f);
                    if (cut <= 0)
                        continue;
                    _dilemma.SetMinistryBudget(targetId, b - cut);
                    total += cut;
                }
                AddBudget(total);
                _recovered += total;
                AddTransparency(5f);
                AddLog($"Внеплановая ревизия: изъято на проверку {total} кр.");
                break;
            case 3:
                _tipBoostHours = 4f;
                AddLog("Горячая линия открыта: заявители осмелели.");
                break;
            case 4:
                AddTransparency(5f);
                _auditBoost = true;
                AddLog("Ревизоры премированы и рвутся в бой.");
                break;
        }

        return true;
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
            _anomalies.Clear();
            _audited.Clear();
            _tips.Clear();
            _opCooldowns.Clear();
            _tipTimer = 0f;
            _auditBoost = false;
        }

        if (!_time.DayActive || !_ministry.IsClaimed(DeptId))
            return;

        var gameHours = dt * (1f / SWSenateTimeSystem.RealSecondsPerGameHour);

        AddTransparency(-TransparencyDecay * gameHours);

        if (_tipBoostHours > 0f)
            _tipBoostHours = MathF.Max(0f, _tipBoostHours - gameHours);

        for (var i = _tips.Count - 1; i >= 0; i--)
        {
            _tips[i].LifeHours -= gameHours;
            if (_tips[i].LifeHours > 0f)
                continue;

            var tip = _tips[i];
            _tips.RemoveAt(i);
            FailTip(tip);
        }

        _tipTimer += gameHours;
        var interval = _tipBoostHours > 0f ? TipInterval * 0.5f : TipInterval;
        if (_tipTimer >= interval && _tips.Count < TipCap)
        {
            _tipTimer = 0f;
            var pool = Enumerable.Range(0, Tips.Length).Where(i => _tips.All(t => t.DefIndex != i)).ToList();
            if (pool.Count > 0)
                _tips.Add(new ActiveTip { Id = _nextTipId++, DefIndex = _random.Pick(pool), LifeHours = TipLife });
        }
    }

    private sealed class ActiveTip
    {
        public int Id;
        public int DefIndex;
        public float LifeHours;
    }
}
