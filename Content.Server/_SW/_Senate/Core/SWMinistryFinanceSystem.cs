using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Core;

public sealed class SWMinistryFinanceSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWMinistryEconomySystem _economy = default!;

    public const string FinanceMinistryId = "SWMinistryFinance";

    private const float TickInterval = 1f;

    private const float InflationNeutral = 20f;
    private const float NeutralRate = 5f;

    private static readonly int[] RatePresets = { 0, 3, 6, 9, 12, 16 };

    private const int BondDebt = 15;        // debt added per bond issue
    private const int BondTreasuryBase = 14; // treasury raised per issue at top rating
    private const float BondInterest = 0.1f;
    private sealed class BondIssue(int proceeds, int repayment)
    {
        public readonly int Proceeds = proceeds;
        public readonly int RepaymentAtIssue = repayment;
        public float UnitsLeft = BondDebt;
        public int CostLeft = repayment;
    }

    private readonly Queue<BondIssue> _bondRepayments = new();
    public int NextRepaymentCost => _bondRepayments.TryPeek(out var issue) ? issue.CostLeft : 0;

    private float _inflation = InflationNeutral;
    private int _keyRate = 5;
    private float DebtUnits => _bondRepayments.Sum(b => b.UnitsLeft);
    private int _taxLevel = 1; // 0 low / 1 mid / 2 high
    private bool _emissionPending;

    private float _ratingScore = 35f;
    private int _ratingBand = 2; // A

    private string? _program;
    private float _progress;

    private readonly List<string> _log = new();
    private int _day = -1;
    private float _accum;

    public int Inflation => (int) MathF.Round(_inflation);
    public int KeyRate => _keyRate;
    public int Debt => (int) MathF.Round(DebtUnits);
    public int TaxLevel => _taxLevel;
    public bool EmissionPending => _emissionPending;
    public string? SelectedProgram => _program;
    public int ProgramProgress => (int) MathF.Round(_progress);
    public IReadOnlyList<int> RateOptions => RatePresets;

    /// <summary>Rating band index: 0 = AAA (best) .. 7 = D (worst). See <see cref="RatingLabels"/>.</summary>
    public int RatingBand => _ratingBand;

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _accum = 0f;
        _inflation = InflationNeutral;
        _keyRate = 5;
        _bondRepayments.Clear();
        _taxLevel = 1;
        _emissionPending = false;
        _ratingScore = 35f;
        _ratingBand = 2;
        _program = null;
        _progress = 0f;
        _log.Clear();
        _day = -1;
    }

    private void AddLog(string text)
    {
        _log.Insert(0, text);
        if (_log.Count > 6)
            _log.RemoveRange(6, _log.Count - 6);
    }

    public List<string> GetLog() => new(_log);

    // The price level: high inflation makes every credit buy less. Consumed by ministry budgets
    // (SWSenateDilemmaSystem.EnsureMinistry) and the treasury distributable (SWSenateBudgetSystem.Total).
    public float PriceMultiplier() => Math.Clamp(1f + (_inflation - InflationNeutral) / 100f, 0.8f, 1.8f);

    public int PricePercent() => (int) MathF.Round(PriceMultiplier() * 100f);

    // Growth drag handed to the Economy ministry: a high key rate cools the boom, a cheap rate fans it.
    public float RateDrag() => (_keyRate - NeutralRate) * 0.1f;

    public string InflationStatus() => _inflation switch
    {
        >= 70f => "Гиперинфляция",
        >= 45f => "Высокая",
        >= 28f => "Умеренная",
        >= 10f => "Под контролем",
        _ => "Дефляция",
    };

    public string DebtStatus() => DebtUnits switch
    {
        >= 80f => "Критический",
        >= 55f => "Высокий",
        >= 30f => "Умеренный",
        _ => "Низкий",
    };

    public string TaxLevelLabel() => _taxLevel switch
    {
        0 => "Низкие",
        2 => "Высокие",
        _ => "Средние",
    };

    private int Treasury() => _senate.SubStats.TryGetValue("SWStatTreasury", out var t) ? (int) MathF.Round(t) : 50;

    // Lower score edge of each rating band (index 0 = AAA … 6 = CCC; band 7 = D, below the last edge).
    private static readonly float[] RatingEdges = { 60f, 45f, 32f, 20f, 8f, -6f, -20f };
    private static readonly string[] RatingLabels = { "AAA", "AA", "A", "BBB", "BB", "B", "CCC", "D" };
    private const float RatingHysteresis = 3f;

    // Instant credit score from treasury, debt and inflation. Smoothed into _ratingScore so the rating
    // does not flicker as the underlying live stats jitter each tick.
    private float TargetScore() => Treasury() - DebtUnits * 0.6f - (_inflation - InflationNeutral) * 0.4f;

    private void UpdateRating(float gameHours)
    {
        _ratingScore += (TargetScore() - _ratingScore) * Math.Clamp(2f * gameHours, 0f, 1f);

        // Sticky bands: only upgrade/downgrade once the smoothed score clears the threshold by a margin.
        while (_ratingBand > 0 && _ratingScore >= RatingEdges[_ratingBand - 1] + RatingHysteresis)
            _ratingBand--;
        while (_ratingBand < RatingLabels.Length - 1 && _ratingScore < RatingEdges[_ratingBand] - RatingHysteresis)
            _ratingBand++;
    }

    public string CreditRating() => RatingLabels[_ratingBand];

    public Color RatingColor() => _ratingBand switch
    {
        <= 1 => Color.FromHex("#43D16A"),
        <= 3 => Color.FromHex("#5DADE2"),
        <= 5 => Color.FromHex("#E0A23B"),
        _ => Color.FromHex("#E0556B"),
    };

    // Bond proceeds scale with the (smoothed) rating: a trusted Republic borrows cheaply.
    public int BondProceeds() => Math.Clamp((int) MathF.Round(BondTreasuryBase * (_ratingScore / 50f + 0.5f)), 4, BondTreasuryBase + 6);

    public bool CanIssueBonds() => _time.ModeActive && _time.DayActive
        && DebtUnits + BondDebt <= 100f && CreditRating() != "D"
        && _senate.SubStats.TryGetValue("SWStatTreasury", out var treasury)
        && treasury + BondProceeds() <= 100f;

    public bool CanRepayDebt() => _time.ModeActive && _time.DayActive && NextRepaymentCost > 0
        && _senate.SubStats.TryGetValue("SWStatTreasury", out var treasury) && treasury >= NextRepaymentCost;

    public void SetRate(int rate)
    {
        if (RatePresets.Contains(rate))
            _keyRate = rate;
    }

    public void SetTax(int level) => _taxLevel = Math.Clamp(level, 0, 2);

    public void SelectProgram(string programId)
    {
        if (_proto.HasIndex<SWFinanceProgramPrototype>(programId))
            _program = programId;
    }

    public bool IssueBonds()
    {
        if (!CanIssueBonds())
            return false;

        var proceeds = BondProceeds();
        _senate.AddPolicy("SWStatTreasury", proceeds);
        var repayment = proceeds + Math.Max(1, (int) MathF.Ceiling(proceeds * BondInterest));
        _bondRepayments.Enqueue(new BondIssue(proceeds, repayment));
        AddLog($"Выпущены облигации: +{proceeds} в казну, долг вырос.");
        return true;
    }

    public bool RepayDebt()
    {
        if (!CanRepayDebt())
            return false;

        RepayUnits(_bondRepayments.Peek().UnitsLeft);
        AddLog("Часть госдолга погашена досрочно.");
        return true;
    }

    private void RepayUnits(float units)
    {
        while (units > 0f && _bondRepayments.TryPeek(out var issue))
        {
            var portion = Math.Min(units, issue.UnitsLeft);
            var cost = Math.Min(issue.CostLeft, (int) MathF.Ceiling(issue.CostLeft * portion / issue.UnitsLeft));
            if (!_senate.SubStats.TryGetValue("SWStatTreasury", out var treasury) || treasury < cost)
                break;
            _senate.AddPolicy("SWStatTreasury", -cost);
            issue.UnitsLeft -= portion;
            issue.CostLeft -= cost;
            units -= portion;
            if (issue.UnitsLeft <= 0f || issue.CostLeft == 0)
                _bondRepayments.Dequeue();
        }
    }

    public void DeclareEmission()
    {
        if (_emissionPending)
            return;

        if (_senate.OfficesVacant)
        {
            BeginEmission();
            return;
        }

        _emissionPending = true;
    }

    public bool ApproveEmission()
    {
        if (!_emissionPending)
            return false;

        _emissionPending = false;
        BeginEmission();
        return true;
    }

    public bool RejectEmission()
    {
        if (!_emissionPending)
            return false;

        _emissionPending = false;
        return true;
    }

    private void BeginEmission()
    {
        _senate.AddPolicy("SWStatTreasury", 25f);
        _inflation = Math.Clamp(_inflation + 25f, 0f, 100f);
        AddLog("Денежная эмиссия: казна полна, инфляция подскочила.");
    }

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

        if (_emissionPending && _senate.OfficesVacant)
        {
            _emissionPending = false;
            BeginEmission();
        }

        if (_time.Day != _day)
        {
            if (_day != -1)
                ApplyProgram();
            _day = _time.Day;
            _program = null;
            _progress = 0f;
        }

        if (!_time.DayActive)
            return;

        var gameHours = dt * (1f / SWSenateTimeSystem.RealSecondsPerGameHour);

        TickInflation(gameHours);
        TickTaxes(gameHours);
        TickDebtService(gameHours);
        UpdateRating(gameHours);

        if (_program != null && _progress < 100f)
        {
            var perHour = 100f / (SWSenateTimeSystem.EndHour - SWSenateTimeSystem.StartHour);
            _progress = Math.Clamp(_progress + perHour * gameHours, 0f, 100f);
        }
    }

    private void TickInflation(float gameHours)
    {
        // Low rate stokes inflation; high rate tames it. Economic overheat pours fuel on (cross-link).
        var rateGap = (NeutralRate - _keyRate) * 0.15f;
        var overheat = _economy.Overheat;
        var overheatPush = overheat > 50 ? (overheat - 50) / 50f * 1.5f : 0f;
        _inflation = Math.Clamp(_inflation + (rateGap + overheatPush) * gameHours, 0f, 100f);

        // Painful prices erode living standards and morale across the Republic.
        if (_inflation >= 45f)
        {
            var bite = (_inflation - 45f) / 55f;
            _senate.AddPolicy("SWStatLiving", -0.4f * bite * gameHours);
            _senate.AddPolicy("SWStatMorale", -0.3f * bite * gameHours);
        }
    }

    private void TickTaxes(float gameHours)
    {
        switch (_taxLevel)
        {
            case 0:
                _senate.AddPolicy("SWStatTaxes", -0.4f * gameHours);
                _senate.AddPolicy("SWStatTreasury", -0.2f * gameHours);
                NudgeSectors("SWGlobalEconomy", 0.15f * gameHours);
                _senate.AddPolicy("SWStatEmployment", 0.1f * gameHours);
                break;
            case 2:
                _senate.AddPolicy("SWStatTaxes", 0.4f * gameHours);
                _senate.AddPolicy("SWStatTreasury", 0.3f * gameHours);
                NudgeSectors("SWGlobalEconomy", -0.15f * gameHours);
                _senate.AddPolicy("SWStatMorale", -0.1f * gameHours);
                break;
        }
    }

    private void TickDebtService(float gameHours)
    {
        if (DebtUnits <= 0f)
            return;

        var service = DebtUnits * _keyRate * 0.0006f;
        _senate.AddPolicy("SWStatTreasury", -service * gameHours);
    }

    private void NudgeSectors(string globalId, float delta)
    {
        if (delta == 0f)
            return;

        foreach (var (sectorId, _) in _senate.SectorHolders)
            _senate.AddSectorBias(sectorId, globalId, delta);
    }

    private void ApplyProgram()
    {
        if (_program == null || !_proto.TryIndex<SWFinanceProgramPrototype>(_program, out var proto))
            return;

        var scale = _progress / 100f;
        if (scale <= 0f)
            return;

        foreach (var (sub, delta) in proto.PolicyEffects)
            _senate.AddPolicy(sub, delta * scale);

        if (proto.InflationDelta != 0f)
            _inflation = Math.Clamp(_inflation + proto.InflationDelta * scale, 0f, 100f);
        if (proto.DebtDelta < 0f)
            RepayUnits(-proto.DebtDelta * scale);
    }

    // Finance requests are eased when the treasury is healthy and bite under high inflation.
    public (float Cost, float Effect) GetRequestModifier(string? sectorId)
    {
        var cost = 1f;
        var effect = 1f;

        if (Treasury() >= 60)
            effect *= 1.15f;
        else if (Treasury() <= 25)
            cost *= 1.2f;

        if (_inflation >= 55f)
            cost *= 1.2f;

        return (cost, effect);
    }

    public void OnSectorEngagement(string? sectorId)
    {
        if (sectorId == null)
            return;

        _senate.AddSectorBias(sectorId, "SWGlobalEconomy", 1.5f);
    }

    public int TreasuryValue => Treasury();

    public List<SWDefenseProgramEntry> BuildPrograms()
    {
        var list = new List<SWDefenseProgramEntry>();
        foreach (var proto in _proto.EnumeratePrototypes<SWFinanceProgramPrototype>().OrderBy(p => p.Order))
        {
            var entry = new SWDefenseProgramEntry
            {
                Id = proto.ID,
                Name = proto.Name,
                Description = proto.Description,
                Selected = proto.ID == _program,
                Effects = PolicyEffectEntries(proto.PolicyEffects),
            };

            if (proto.InflationDelta != 0f)
                entry.Effects.Add(new SWBillEffectEntry { StatName = "Инфляция", Color = Color.FromHex("#E0556B"), Delta = proto.InflationDelta });
            if (proto.DebtDelta != 0f)
                entry.Effects.Add(new SWBillEffectEntry { StatName = "Долг", Color = Color.FromHex("#E0A23B"), Delta = proto.DebtDelta });

            list.Add(entry);
        }

        return list;
    }

    private List<SWBillEffectEntry> PolicyEffectEntries(Dictionary<ProtoId<SWStatPrototype>, float> effects)
    {
        var list = new List<SWBillEffectEntry>();
        foreach (var sub in _proto.EnumeratePrototypes<SWStatPrototype>().OrderBy(s => s.Order))
        {
            if (!effects.TryGetValue(sub.ID, out var delta))
                continue;

            var color = _proto.TryIndex(sub.Global, out var global) ? global.Color : Color.White;
            list.Add(new SWBillEffectEntry { StatName = sub.ShortName, Color = color, Delta = delta });
        }

        return list;
    }
}
