using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Economy;

public enum SWBudgetPhase : byte
{
    Draft,
    Submitted,
    Approved,
}

// Daily budget pipeline. The Finance ministry shapes a budget project (total split between a sector pool
// and a ministry pool, plus per-ministry shares), the Chancellor allocates the sector pool and ratifies it,
// and only on approval is money released to ministries and to the sector/republic budget-dilemma ledger.
public sealed class SWSenateBudgetSystem : EntitySystem
{
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWMinistryFinanceSystem _finance = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWDeptExpenditureSystem _expenditure = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public const string FinanceMinistryId = "SWMinistryFinance";

    public const int PerSector = 5000;
    public const int PerMinistry = 3000;

    private const int SubmitDeadlineHour = 8; // Finance auto-submits a default if still idle by here.
    private const int ApplyDeadlineHour = 9;  // unapproved project falls back to last submitted by here.
    private const int MaxRejects = 2;

    private int _day = -1;

    private SWBudgetPhase _phase = SWBudgetPhase.Draft;
    private float _sectorRatio = 0.5f;
    private readonly Dictionary<string, int> _ministryWeights = new();
    private readonly Dictionary<string, int> _sectorAlloc = new();
    private int _rejects;
    private string _rejectReason = string.Empty;

    private bool _hasSubmitted;
    private float _submittedRatio = 0.5f;
    private readonly Dictionary<string, int> _submittedWeights = new();

    private bool _autoSubmitted;
    private bool _appliedFallback;

    private int _frozenTotal;
    private readonly Dictionary<string, int> _remaining = new();

    public SWBudgetPhase Phase => _phase;
    public bool Approved => _phase == SWBudgetPhase.Approved;
    public bool Pending => _phase == SWBudgetPhase.Submitted;
    public int Rejects => _rejects;
    public bool CanReject => _phase == SWBudgetPhase.Submitted && _rejects < MaxRejects;
    public string RejectReason => _rejectReason;
    public int SectorPct => (int) MathF.Round(_sectorRatio * 100f);

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _day = -1;
        ResetDay();
    }

    private void ResetDay()
    {
        _phase = SWBudgetPhase.Draft;
        _sectorRatio = DefaultRatio();
        _ministryWeights.Clear();
        foreach (var id in _ministry.Holders.Keys)
            _ministryWeights[id] = 1;
        _sectorAlloc.Clear();
        _rejects = 0;
        _rejectReason = string.Empty;
        _hasSubmitted = false;
        _submittedWeights.Clear();
        _submittedRatio = _sectorRatio;
        _autoSubmitted = false;
        _appliedFallback = false;
        _frozenTotal = 0;
        _remaining.Clear();
    }

    private float DefaultRatio()
    {
        var sectorMoney = _senate.SectorHolders.Count * PerSector;
        var ministryMoney = _ministry.Holders.Count * PerMinistry;
        var denom = sectorMoney + ministryMoney;
        return denom > 0 ? Math.Clamp((float) sectorMoney / denom, 0.1f, 0.9f) : 0.5f;
    }

    // --- Totals & pools ---

    private float TreasuryMultiplier()
    {
        var treasury = _senate.SubStats.TryGetValue("SWStatTreasury", out var t) ? t : 50f;
        var taxes = _senate.SubStats.TryGetValue("SWStatTaxes", out var x) ? x : 50f;
        return Math.Clamp((treasury + taxes) / 100f, 0.4f, 1.8f);
    }

    public int Total()
    {
        if (_phase == SWBudgetPhase.Approved)
            return _frozenTotal;

        var baseMoney = _senate.SectorHolders.Count * PerSector + _ministry.Holders.Count * PerMinistry;
        var transparency = _expenditure.TransparencyBonusActive ? 1.1f : 1f;
        return (int) MathF.Round(baseMoney * TreasuryMultiplier() * transparency / _finance.PriceMultiplier());
    }

    public int SectorPool() => (int) MathF.Round(Total() * _sectorRatio);
    public int MinistryPool() => Total() - SectorPool();

    public int Allocated() => _sectorAlloc.Values.Sum();
    public int Reserve() => Math.Max(0, SectorPool() - Allocated());

    public int Allocation(string sectorId) => _sectorAlloc.TryGetValue(sectorId, out var a) ? a : 0;

    // --- Finance: shape the project ---

    public void AdjustRatio(int deltaPct)
    {
        if (_phase == SWBudgetPhase.Approved)
            return;
        _sectorRatio = Math.Clamp(_sectorRatio + deltaPct / 100f, 0.1f, 0.9f);
    }

    public void SetWeight(string ministryId, int delta)
    {
        if (_phase == SWBudgetPhase.Approved || !_ministry.IsClaimed(ministryId))
            return;
        var w = _ministryWeights.TryGetValue(ministryId, out var cur) ? cur : 1;
        _ministryWeights[ministryId] = Math.Clamp(w + delta, 0, 6);
    }

    public void ResetWeights()
    {
        if (_phase == SWBudgetPhase.Approved)
            return;
        _ministryWeights.Clear();
        foreach (var id in _ministry.Holders.Keys)
            _ministryWeights[id] = 1;
    }

    public bool Submit()
    {
        if (_phase == SWBudgetPhase.Approved)
            return false;

        _phase = SWBudgetPhase.Submitted;
        _rejectReason = string.Empty;
        SnapshotProject();
        return true;
    }

    private void SnapshotProject()
    {
        _hasSubmitted = true;
        _submittedRatio = _sectorRatio;
        _submittedWeights.Clear();
        foreach (var (id, w) in _ministryWeights)
            _submittedWeights[id] = w;
    }

    // --- Chancellor: allocate sectors & ratify ---

    public bool SetAllocation(string sectorId, int amount)
    {
        if (_phase != SWBudgetPhase.Submitted)
            return false;

        amount = Math.Max(0, amount);
        var others = _sectorAlloc.Where(kv => kv.Key != sectorId).Sum(kv => kv.Value);
        var cap = Math.Max(0, SectorPool() - others);
        _sectorAlloc[sectorId] = Math.Min(amount, cap);
        return true;
    }

    public bool Approve()
    {
        if (_phase != SWBudgetPhase.Submitted)
            return false;

        ApplyProject(false);
        return true;
    }

    public bool Reject(string reason)
    {
        if (_phase != SWBudgetPhase.Submitted || _rejects >= MaxRejects)
            return false;

        _rejects++;
        _rejectReason = reason.Trim();
        _phase = SWBudgetPhase.Draft;
        return true;
    }

    // --- Release ---

    private void ApplyProject(bool equalSectors)
    {
        _frozenTotal = Total();
        var sectorPool = (int) MathF.Round(_frozenTotal * _sectorRatio);
        var ministryPool = _frozenTotal - sectorPool;

        _remaining.Clear();

        var sectors = _senate.SectorHolders.Keys.ToList();
        if (equalSectors || _sectorAlloc.Count == 0)
        {
            var share = sectors.Count > 0 ? sectorPool / sectors.Count : 0;
            foreach (var sector in sectors)
                _remaining[sector] = share;
            _remaining[SWSenateDilemmaSystem.RepublicOwner] = sectorPool - share * sectors.Count;
        }
        else
        {
            var used = 0;
            foreach (var sector in sectors)
            {
                var amount = Math.Min(Allocation(sector), Math.Max(0, sectorPool - used));
                _remaining[sector] = amount;
                used += amount;
            }
            _remaining[SWSenateDilemmaSystem.RepublicOwner] = Math.Max(0, sectorPool - used);
        }

        var active = _ministry.Holders.Keys.ToList();
        var totalWeight = active.Sum(id => _ministryWeights.TryGetValue(id, out var w) ? w : 1);
        foreach (var id in active)
        {
            var weight = _ministryWeights.TryGetValue(id, out var w) ? w : 1;
            var amount = totalWeight > 0 ? (int) MathF.Round((float) ministryPool * weight / totalWeight) : 0;
            _dilemma.SetMinistryBudget(id, _dilemma.GetMinistryBudget(id) + amount);
        }

        _phase = SWBudgetPhase.Approved;
    }

    public List<SWBudgetShareEntry> BuildShares()
    {
        var pool = MinistryPool();
        var active = _ministry.Holders.Keys.ToList();
        var totalWeight = active.Sum(id => _ministryWeights.TryGetValue(id, out var w) ? w : 1);

        var list = new List<SWBudgetShareEntry>();
        foreach (var id in active)
        {
            if (!_proto.TryIndex<SWMinistryPrototype>(id, out var proto))
                continue;

            var weight = _ministryWeights.TryGetValue(id, out var w) ? w : 1;
            var amount = totalWeight > 0 ? (int) MathF.Round((float) pool * weight / totalWeight) : 0;
            list.Add(new SWBudgetShareEntry { MinistryId = id, Name = proto.Name, Color = proto.Color, Weight = weight, Amount = amount });
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        return list;
    }

    public int Available(string owner) =>
        _phase == SWBudgetPhase.Approved && _remaining.TryGetValue(owner, out var r) ? r : 0;

    public bool Spend(string owner, int cost)
    {
        if (_phase != SWBudgetPhase.Approved || cost < 0)
            return false;

        if (!_remaining.TryGetValue(owner, out var remaining) || remaining < cost)
            return false;

        _remaining[owner] = remaining - cost;
        return true;
    }

    public void Grant(string owner, int amount)
    {
        if (_phase != SWBudgetPhase.Approved || amount == 0)
            return;

        _remaining[owner] = Math.Max(0, (_remaining.TryGetValue(owner, out var r) ? r : 0) + amount);
    }

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive)
            return;

        if (_time.Day != _day)
        {
            _day = _time.Day;
            ResetDay();
        }

        if (!_time.DayActive || _phase == SWBudgetPhase.Approved)
            return;

        var financeActive = _ministry.IsClaimed(FinanceMinistryId);

        // Finance idle: auto-submit a default project by the end of the first game-hour.
        if (!financeActive && !_autoSubmitted && _phase == SWBudgetPhase.Draft && _time.CurrentHour >= SubmitDeadlineHour)
        {
            _autoSubmitted = true;
            _sectorRatio = DefaultRatio();
            ResetWeights();
            Submit();
        }

        // No Chancellor to ratify: autopilot approves a submitted project with an equal sector split.
        if (_phase == SWBudgetPhase.Submitted && _senate.OfficesVacant)
        {
            ApplyProject(true);
            return;
        }

        // Deadline reached without approval: fall back to the last submitted project (or a default).
        if (!_appliedFallback && _time.CurrentHour >= ApplyDeadlineHour)
        {
            _appliedFallback = true;
            if (_hasSubmitted)
            {
                _sectorRatio = _submittedRatio;
                _ministryWeights.Clear();
                foreach (var (id, w) in _submittedWeights)
                    _ministryWeights[id] = w;
            }
            else
            {
                _sectorRatio = DefaultRatio();
                ResetWeights();
            }

            ApplyProject(_sectorAlloc.Count == 0);
        }
    }
}
