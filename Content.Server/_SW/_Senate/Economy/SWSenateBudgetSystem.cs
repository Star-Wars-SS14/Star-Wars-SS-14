using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Sectors;
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

    private int _day = -1;

    private SWBudgetPhase _phase = SWBudgetPhase.Draft;
    private float _sectorRatio = 0.5f;
    private int _reservePct = 10;
    private int _treasury;
    private int _revision;
    private int _reserveBalance;
    private string _draftHolders = string.Empty;
    private bool _draftManuallyModified;
    private readonly HashSet<string> _transferIds = new();
    private readonly List<SWBudgetTransferEntry> _transferHistory = new();
    private readonly Dictionary<string, int> _ministryWeights = new();
    private readonly Dictionary<string, int> _sectorAlloc = new();
    private int _rejects;
    private string _rejectReason = string.Empty;

    private bool _hasSubmitted;
    private float _submittedRatio = 0.5f;
    private int _submittedReservePct = 10;
    private readonly Dictionary<string, int> _submittedWeights = new();

    private bool _autoSubmitted;
    private bool _appliedFallback;
    public bool AutoApplied { get; private set; }
    public string AutoReason { get; private set; } = string.Empty;
    public string TransferResultRequestId { get; private set; } = string.Empty;
    public string TransferResult { get; private set; } = string.Empty;
    public void ReportTransfer(string requestId, bool success)
    {
        TransferResultRequestId = requestId ?? string.Empty;
        TransferResult = success ? "Перевод выполнен" : "Перевод не выполнен: проверьте остаток и обновите данные";
        if (!success) _revision++;
    }

    private int _frozenTotal;
    private readonly Dictionary<string, int> _remaining = new();
    private string[] _governedSectors = Array.Empty<string>();
    public IReadOnlyList<string> GovernedSectors => _governedSectors;

    public SWBudgetPhase Phase => _phase;
    public bool Approved => _phase == SWBudgetPhase.Approved;
    public bool Pending => _phase == SWBudgetPhase.Submitted;
    public int Rejects => _rejects;
    public bool CanReject => _phase == SWBudgetPhase.Submitted;
    public string RejectReason => _rejectReason;
    public int SectorPct { get { RefreshDraftHolders(); return (int) MathF.Round(_sectorRatio * 100f); } }
    public int ReservePct => _reservePct;
    public int Treasury => _treasury;
    public int Revision => _revision;
    public int ReserveBalance => _reserveBalance;
    public bool HasCurrentDay => _day == _time.Day;
    /// <summary>All unspent public accounts, each counted once; issued allocations are not spent a second time.</summary>
    public long ConsolidatedPublicBalance => (long) _treasury + _reserveBalance
        + _remaining.Values.Sum(value => (long) value) + _dilemma.TotalUnspentMinistryBudgets;
    public int BaselinePublicBudget => _governedSectors.Length * PerSector + _ministry.Holders.Count * PerMinistry;
    public bool SpendReserveForProject(int amount)
    {
        if (_phase != SWBudgetPhase.Approved || _day != _time.Day || amount <= 0 || amount > _reserveBalance) return false;
        _reserveBalance -= amount;
        _revision++;
        return true;
    }
    public void RefundReserveForProject(int amount)
    {
        if (_phase == SWBudgetPhase.Approved && amount > 0) { _reserveBalance += amount; _revision++; }
    }
    public void RecordReserveProject(string target, int amount, string purpose)
    {
        _transferHistory.Add(new SWBudgetTransferEntry { TargetName = target, Amount = amount,
            Purpose = purpose, Time = _time.AbsoluteHour.ToString() });
    }
    public void CreditTreasury(int amount) { EnsureDay(); if (amount > 0) _treasury = (int) Math.Min(int.MaxValue, (long) _treasury + amount); }
    public bool DebitTreasury(int amount)
    {
        EnsureDay();
        if (amount < 0 || amount > _treasury) return false;
        _treasury -= amount;
        return true;
    }
    public IReadOnlyList<SWBudgetTransferEntry> TransferHistory => _transferHistory;

    public List<SWBudgetTransferTarget> BuildTransferTargets()
    {
        var result = new List<SWBudgetTransferTarget>();
        foreach (var id in _ministry.Holders.Keys)
            if (_proto.TryIndex<SWMinistryPrototype>(id, out var ministry))
                result.Add(new SWBudgetTransferTarget { Id = id, Name = ministry.Name, Ministry = true });
        foreach (var id in _governedSectors)
            result.Add(new SWBudgetTransferTarget { Id = id, Name = _proto.TryIndex<SWSectorPrototype>(id, out var sector) ? sector.Name : id });
        result.Add(new SWBudgetTransferTarget { Id = SWSenateDilemmaSystem.RepublicOwner, Name = "Общереспубликанские нужды" });
        return result;
    }

    public override void Initialize()
    {
        // Civilian administrations exist independently of occupied Senate seats.
        var scenario = _proto.Index<SWRepublicEconomyScenarioPrototype>(SWRepublicEconomyFactory.ScenarioId);
        _governedSectors = _proto.Index(scenario.Map).Worlds
            .Where(w => w.Sector != null && _proto.Index<SWPlanetEconomyPrototype>(w.Id).ActiveEconomy)
            .Select(w => w.Sector!.Value.Id).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray();
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<SWSenateDayStartedEvent>(_ => EnsureDay());
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _day = -1;
        _treasury = 0;
        _reserveBalance = 0;
        _remaining.Clear();
        ResetDay();
    }

    private void ResetDay()
    {
        if (_day > 0)
            _treasury += _reserveBalance + _remaining.Values.Sum() + _dilemma.ReturnAllMinistryBudgets();
        _reserveBalance = 0;
        _transferIds.Clear();
        _transferHistory.Clear();
        TransferResultRequestId = string.Empty;
        TransferResult = string.Empty;
        _revision++;
        _phase = SWBudgetPhase.Draft;
        _sectorRatio = DefaultSectorRatio();
        _draftHolders = HolderSignature();
        _draftManuallyModified = false;
        _reservePct = 10;
        _sectorRatio = Math.Min(_sectorRatio, 0.9f);
        _ministryWeights.Clear();
        foreach (var id in _ministry.Holders.Keys)
            _ministryWeights[id] = 1;
        _sectorAlloc.Clear();
        _rejects = 0;
        _rejectReason = string.Empty;
        _hasSubmitted = false;
        _submittedWeights.Clear();
        _submittedRatio = _sectorRatio;
        _submittedReservePct = _reservePct;
        _autoSubmitted = false;
        _appliedFallback = false;
        AutoApplied = false;
        AutoReason = string.Empty;
        _frozenTotal = 0;
        _remaining.Clear();
    }

    private float DefaultRatio()
    {
        var sectorMoney = _governedSectors.Length * PerSector;
        var ministryMoney = _ministry.Holders.Count * PerMinistry;
        var denom = sectorMoney + ministryMoney;
        return denom > 0 ? Math.Clamp((float) sectorMoney / denom, 0.1f, 0.9f) : 0.5f;
    }

    private float DefaultSectorRatio() => _ministry.Holders.Count == 0 ? 0.9f : Math.Max(0f, DefaultRatio() - 0.1f);

    private string HolderSignature() => string.Join("|", _ministry.Holders.Keys.OrderBy(id => id, StringComparer.Ordinal));

    private void RefreshDraftHolders()
    {
        if (_phase != SWBudgetPhase.Draft) return;
        var current = HolderSignature();
        if (current == _draftHolders) return;
        _draftHolders = current;
        if (!_draftManuallyModified)
            _sectorRatio = DefaultSectorRatio();
        _revision++;
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
        RefreshDraftHolders();
        if (_phase == SWBudgetPhase.Approved)
            return _frozenTotal;

        var baseMoney = _governedSectors.Length * PerSector + _ministry.Holders.Count * PerMinistry;
        var transparency = _expenditure.TransparencyBonusActive ? 1.1f : 1f;
        return Math.Min(_treasury, (int) MathF.Round(baseMoney * TreasuryMultiplier() * transparency / _finance.PriceMultiplier()));
    }

    public int SectorPool() => (int) MathF.Round(Total() * _sectorRatio);
    public int ReservePool() => Math.Min(Total() - SectorPool(), (int) MathF.Round(Total() * _reservePct / 100f));
    public int MinistryPool() => Math.Max(0, Total() - SectorPool() - ReservePool());

    public int Allocated() => _sectorAlloc.Values.Sum();
    public int Reserve() => Math.Max(0, SectorPool() - Allocated());

    public int Allocation(string sectorId) => _sectorAlloc.TryGetValue(sectorId, out var a) ? a : 0;

    // --- Finance: shape the project ---

    public void AdjustRatio(int deltaPct)
    {
        if (_phase != SWBudgetPhase.Draft)
            return;
        _draftManuallyModified = true;
        _sectorRatio = Math.Clamp(_sectorRatio + deltaPct / 100f, 0.1f, 0.9f);
    }

    public void SetWeight(string ministryId, int delta)
    {
        if (_phase != SWBudgetPhase.Draft || !_ministry.IsClaimed(ministryId))
            return;
        _draftManuallyModified = true;
        var w = _ministryWeights.TryGetValue(ministryId, out var cur) ? cur : 1;
        _ministryWeights[ministryId] = Math.Clamp(w + delta, 0, 6);
    }

    public void ResetWeights()
    {
        if (_phase != SWBudgetPhase.Draft)
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
        _revision++;
        return true;
    }

    public bool Submit(int revision, int sectorPct, int reservePct, Dictionary<string, int> weights)
    {
        if (_day != _time.Day || revision != _revision || _phase != SWBudgetPhase.Draft || sectorPct < 0 || reservePct < 0
            || sectorPct + reservePct > 100 || weights == null || weights.Count != _ministry.Holders.Count
            || weights.Any(w => !_ministry.IsClaimed(w.Key) || w.Value < 0 || w.Value > 6)
            || (weights.Count > 0 && sectorPct + reservePct < 100 && weights.Values.Sum() == 0))
            return false;
        _sectorRatio = sectorPct / 100f;
        _reservePct = reservePct;
        _draftManuallyModified = true;
        _ministryWeights.Clear();
        foreach (var (id, weight) in weights) _ministryWeights[id] = weight;
        return Submit();
    }

    private void SnapshotProject()
    {
        _hasSubmitted = true;
        _submittedRatio = _sectorRatio;
        _submittedReservePct = _reservePct;
        _submittedWeights.Clear();
        foreach (var (id, w) in _ministryWeights)
            _submittedWeights[id] = w;
    }

    // --- Chancellor: allocate sectors & ratify ---

    public bool SetAllocation(string sectorId, int amount)
    {
        if (_phase != SWBudgetPhase.Submitted || !_governedSectors.Contains(sectorId))
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

    public bool Approve(int revision, Dictionary<string, int> allocations)
    {
        if (_day != _time.Day || revision != _revision || _phase != SWBudgetPhase.Submitted || allocations == null
            || allocations.Any(x => !_governedSectors.Contains(x.Key) || x.Value < 0)
            || allocations.Values.Sum(x => (long) x) > SectorPool()) return false;
        _sectorAlloc.Clear();
        foreach (var (id, amount) in allocations) _sectorAlloc[id] = amount;
        ApplyProject(false);
        return true;
    }

    public bool Reject(string reason)
    {
        if (_phase != SWBudgetPhase.Submitted)
            return false;

        _rejects++;
        _rejectReason = reason.Trim();
        _phase = SWBudgetPhase.Draft;
        _revision++;
        return true;
    }

    public bool Reject(int revision, string reason) => _day == _time.Day && revision == _revision && !string.IsNullOrWhiteSpace(reason)
        && reason.Length <= 512 && Reject(reason);

    // --- Release ---

    private void ApplyProject(bool equalSectors)
    {
        _frozenTotal = Total();
        var sectorPool = (int) MathF.Round(_frozenTotal * _sectorRatio);
        _reserveBalance = ReservePool();
        var ministryPool = _frozenTotal - sectorPool - _reserveBalance;
        _treasury -= _frozenTotal;

        _remaining.Clear();

        var sectors = _governedSectors.ToList();
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

        var shares = CalculateMinistryShares(ministryPool);
        foreach (var (id, amount) in shares)
        {
            _dilemma.SetMinistryBudget(id, _dilemma.GetMinistryBudget(id) + amount);
        }
        _treasury += ministryPool - shares.Values.Sum();

        _phase = SWBudgetPhase.Approved;
        _revision++;
    }

    public bool Transfer(int revision, string requestId, bool ministry, string targetId, int amount, string purpose)
    {
        if (_day != _time.Day || _phase != SWBudgetPhase.Approved || revision != _revision || string.IsNullOrWhiteSpace(requestId)
            || requestId.Length > 100 || _transferIds.Contains(requestId) || amount <= 0 || amount > _reserveBalance
            || string.IsNullOrWhiteSpace(purpose) || purpose.Length > 256) return false;
        string name;
        if (ministry)
        {
            if (!_ministry.IsClaimed(targetId) || !_proto.TryIndex<SWMinistryPrototype>(targetId, out var proto)) return false;
            name = proto.Name;
            _dilemma.SetMinistryBudget(targetId, _dilemma.GetMinistryBudget(targetId) + amount);
        }
        else
        {
            if (!_governedSectors.Contains(targetId) && targetId != SWSenateDilemmaSystem.RepublicOwner) return false;
            name = targetId == SWSenateDilemmaSystem.RepublicOwner ? "Общереспубликанские нужды" :
                _proto.TryIndex<SWSectorPrototype>(targetId, out var sector) ? sector.Name : targetId;
            _remaining[targetId] = Available(targetId) + amount;
        }
        _reserveBalance -= amount;
        _transferIds.Add(requestId);
        _transferHistory.Add(new SWBudgetTransferEntry { TargetName = name, Amount = amount, Purpose = purpose.Trim(), Time = _time.AbsoluteHour.ToString() });
        _revision++;
        return true;
    }

    public List<SWBudgetShareEntry> BuildShares()
    {
        RefreshDraftHolders();
        var pool = MinistryPool();
        var active = _ministry.Holders.Keys.ToList();
        var list = new List<SWBudgetShareEntry>();
        var amounts = CalculateMinistryShares(pool);
        foreach (var id in active)
        {
            if (!_proto.TryIndex<SWMinistryPrototype>(id, out var proto))
                continue;

            var weight = _ministryWeights.TryGetValue(id, out var w) ? w : 1;
            var amount = amounts.GetValueOrDefault(id);
            list.Add(new SWBudgetShareEntry { MinistryId = id, Name = proto.Name, Color = proto.Color, Weight = weight, Amount = amount });
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        return list;
    }

    private Dictionary<string, int> CalculateMinistryShares(int pool)
    {
        var ids = _ministry.Holders.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var total = ids.Sum(id => _ministryWeights.GetValueOrDefault(id, 1));
        var result = new Dictionary<string, int>();
        if (total == 0) return result;
        var remaining = pool;
        foreach (var id in ids)
        {
            var amount = (int) ((long) pool * _ministryWeights.GetValueOrDefault(id, 1) / total);
            result[id] = amount; remaining -= amount;
        }
        foreach (var id in ids.Where(id => _ministryWeights.GetValueOrDefault(id, 1) > 0))
        {
            if (remaining-- <= 0) break;
            result[id]++;
        }
        return result;
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
        frameTime = _time.ActiveSeconds(frameTime);
        if (frameTime <= 0f)
            return;
        if (!_time.ModeActive)
            return;

        EnsureDay();
        RefreshDraftHolders();

        if (!_time.DayActive || _phase == SWBudgetPhase.Approved)
            return;

        var financeActive = _ministry.IsClaimed(FinanceMinistryId);

        // Finance idle: auto-submit a default project by the end of the first game-hour.
        if (!financeActive && !_autoSubmitted && _phase == SWBudgetPhase.Draft && _time.CurrentHour >= SubmitDeadlineHour)
        {
            _autoSubmitted = true;
            _sectorRatio = DefaultSectorRatio();
            ResetWeights();
            Submit();
        }

        // No Chancellor to ratify: autopilot approves a submitted project with an equal sector split.
        if (_phase == SWBudgetPhase.Submitted && _senate.OfficesVacant)
        {
            AutoApplied = true;
            AutoReason = "Нет канцлера и спикера";
            ApplyProject(true);
            return;
        }

        // Deadline reached without approval: fall back to the last submitted project (or a default).
        if (!_appliedFallback && _time.CurrentHour >= ApplyDeadlineHour)
        {
            _appliedFallback = true;
            AutoApplied = true;
            AutoReason = _phase == SWBudgetPhase.Draft && _rejects > 0
                ? "Возвращённый проект: применено стандартное распределение по сроку"
                : "Срок утверждения бюджета истёк";
            if (_hasSubmitted && !(_phase == SWBudgetPhase.Draft && _rejects > 0))
            {
                _sectorRatio = _submittedRatio;
                _reservePct = _submittedReservePct;
                _ministryWeights.Clear();
                foreach (var (id, w) in _submittedWeights)
                    _ministryWeights[id] = w;
            }
            else
            {
                _sectorRatio = DefaultSectorRatio();
                _reservePct = 10;
                ResetWeights();
                _sectorAlloc.Clear();
            }

            ApplyProject(_sectorAlloc.Count == 0);
        }
    }

    private void EnsureDay()
    {
        if (_time.Day == _day) return;
        if (_day < 0)
            _treasury = _governedSectors.Length * PerSector + _ministry.Holders.Count * PerMinistry;
        _day = _time.Day;
        ResetDay();
    }
}
