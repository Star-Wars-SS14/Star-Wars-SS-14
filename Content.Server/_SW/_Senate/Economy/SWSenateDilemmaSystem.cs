using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Dilemmas;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Core;

namespace Content.Server._SW._Senate.Economy;

public sealed class SWSenateDilemmaSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateBudgetSystem _budget = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWMinistryDefenseSystem _defense = default!;
    [Dependency] private readonly SWMinistryInternalSecuritySystem _security = default!;
    [Dependency] private readonly SWMinistryHealthSystem _health = default!;
    [Dependency] private readonly SWMinistryEconomySystem _economy = default!;
    [Dependency] private readonly SWMinistryFinanceSystem _finance = default!;
    [Dependency] private readonly SWMinistryJusticeSystem _justice = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;

    public const string RepublicOwner = "republic";

    // Mini-debuff to a sector whose citizens are left waiting on a delegated ministry request.
    private const float WaitingDebuff = -2f;

    // Stock ministry budget/admin (later tied to МинФин). Recomputed each day from active sectors.
    private const int MinistryBudgetBase = 4000;
    private const int MinistryBudgetPerSector = 1000;
    private const int MinistryAdminPerDay = 3;
    private const int AuditAdminCost = 1;

    private const float ReliableBonus = 1.25f;
    private const float InflatedFactor = 0.7f;
    private const float UnderestimatedFailFactor = 1.5f;

    // Sector self-solve options are toned down globally (the strong path is delegating to a ministry).
    private const float SenatorScale = 0.6f;

    private const int MorningDeadline = 14;
    private const int MorningApply = 15;
    private const int AfternoonDeadline = 20;
    private const int AfternoonApply = 20;
    private const float UpdateInterval = 1f;

    // Chance an eligible (condition-met) conditional dilemma actually fires in a window — a bit higher
    // than any single unconditional one, but well short of guaranteed.
    private const float ConditionalChance = 0.5f;

    private readonly List<SWActiveDilemma> _dilemmas = new();
    private readonly List<SWMinistryRequest> _requests = new();
    private readonly Dictionary<string, int> _ministryBudget = new();
    private readonly Dictionary<string, int> _ministryAdmin = new();
    private readonly Dictionary<string, List<string>> _notices = new();
    private readonly HashSet<string> _generated = new();
    private int _nextId = 1;
    private int _nextRequestId = 1;
    private int _day = -1;
    private float _accumulator;

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        if (_time.Day != _day)
        {
            _day = _time.Day;
            _dilemmas.Clear();
            _requests.Clear();
            _ministryBudget.Clear();
            _ministryAdmin.Clear();
            _notices.Clear();
            _generated.Clear();
            _nextId = 1;
            _nextRequestId = 1;
        }

        ApplyDue();
        CompleteAudits();
        ExpireRequests();
        EnsureGeneration();
    }

    private void CompleteAudits()
    {
        foreach (var request in _requests)
        {
            if (!request.AuditPending)
                continue;

            if (_time.DayActive && _time.CurrentHour < request.AuditCompleteHour)
                continue;

            request.AuditPending = false;
            request.AuditDone = true;
        }
    }

    private void EnsureMinistry(string ministryId)
    {
        if (_ministryBudget.ContainsKey(ministryId))
            return;

        // No stock budget — ministries are funded only once the day's budget project is approved
        // (SWSenateBudgetSystem.SetMinistryBudget). Admin resource is separate and still granted daily.
        _ministryBudget[ministryId] = 0;
        _ministryAdmin[ministryId] = MinistryAdminPerDay;
    }

    public int GetMinistryBudget(string ministryId)
    {
        EnsureMinistry(ministryId);
        return _ministryBudget[ministryId];
    }

    public int GetMinistryAdmin(string ministryId)
    {
        EnsureMinistry(ministryId);
        return _ministryAdmin[ministryId];
    }

    public int AuditCost => AuditAdminCost;

    public bool SpendMinistry(string ministryId, int amount)
    {
        EnsureMinistry(ministryId);
        if (_ministryBudget[ministryId] < amount)
            return false;

        _ministryBudget[ministryId] -= amount;
        return true;
    }

    // Called by the budget pipeline when the day's project is approved.
    public void SetMinistryBudget(string ministryId, int amount)
    {
        EnsureMinistry(ministryId);
        _ministryBudget[ministryId] = Math.Max(0, amount);
    }

    public bool SpendMinistryAdmin(string ministryId, int amount)
    {
        EnsureMinistry(ministryId);
        if (_ministryAdmin[ministryId] < amount)
            return false;

        _ministryAdmin[ministryId] -= amount;
        return true;
    }

    private SWDilemmaWindow? CurrentGenWindow()
    {
        if (!_time.DayActive)
            return null;

        var hour = _time.CurrentHour;
        if (hour >= SWSenateTimeSystem.StartHour && hour <= 13)
            return SWDilemmaWindow.Morning;
        if (hour is >= 15 and <= 19)
            return SWDilemmaWindow.Afternoon;

        return null;
    }

    private void EnsureGeneration()
    {
        var window = CurrentGenWindow();
        if (window == null)
            return;

        // Budget dilemmas only arrive in the morning (the Chancellor distributes the budget at day start).
        var allowBudget = window.Value == SWDilemmaWindow.Morning;

        foreach (var (sectorId, _) in _senate.SectorHolders)
        {
            GenerateFor(sectorId, SWDilemmaScope.Sectoral, window.Value, sectorId, false);
            if (allowBudget)
                GenerateFor(sectorId, SWDilemmaScope.Sectoral, window.Value, sectorId, true);
        }

        if (_senate.Chancellor != null)
        {
            GenerateFor(RepublicOwner, SWDilemmaScope.Republic, window.Value, null, false);
            if (allowBudget)
                GenerateFor(RepublicOwner, SWDilemmaScope.Republic, window.Value, null, true);
        }
    }

    private void GenerateFor(string owner, SWDilemmaScope scope, SWDilemmaWindow window, string? sectorId, bool budget)
    {
        var key = $"{owner}|{window}|{budget}";
        if (!_generated.Add(key))
            return;

        var candidates = _proto.EnumeratePrototypes<SWDilemmaPrototype>()
            .Where(d => d.Scope == scope && d.Budget == budget)
            .ToList();

        var deadline = window == SWDilemmaWindow.Morning ? MorningDeadline : AfternoonDeadline;
        var apply = window == SWDilemmaWindow.Morning ? MorningApply : AfternoonApply;

        // Unconditional pool: 1–2 random picks (budget pools roll 0–1).
        var pool = candidates.Where(d => d.Conditions.Count == 0).ToList();
        var count = Math.Min(budget ? _random.Next(0, 2) : _random.Next(1, 3), pool.Count);
        _random.Shuffle(pool);
        for (var i = 0; i < count; i++)
            Spawn(pool[i], scope, owner, sectorId, window, deadline, apply);

        // Conditional dilemmas (always non-budget): each one whose thresholds currently hold gets an
        // independent chance to fire. Meeting the condition only unlocks the possibility.
        if (budget)
            return;

        var republicGlobals = scope == SWDilemmaScope.Republic ? _senate.GetRepublicGlobals() : null;
        foreach (var proto in candidates)
        {
            if (proto.Conditions.Count == 0 || HasActive(proto.ID, owner))
                continue;
            if (!ConditionsMet(proto, scope, sectorId, republicGlobals))
                continue;
            if (_random.Prob(ConditionalChance))
                Spawn(proto, scope, owner, sectorId, window, deadline, apply);
        }
    }

    private void Spawn(SWDilemmaPrototype proto, SWDilemmaScope scope, string owner, string? sectorId, SWDilemmaWindow window, int deadline, int apply)
    {
        _dilemmas.Add(new SWActiveDilemma
        {
            Id = _nextId++,
            DilemmaId = proto.ID,
            Scope = scope,
            IsBudget = proto.Budget,
            Owner = owner,
            SectorId = sectorId,
            Window = window,
            DeadlineHour = deadline,
            ApplyHour = apply,
            Status = SWDilemmaStatus.Pending,
        });
    }

    private bool HasActive(string dilemmaId, string owner) =>
        _dilemmas.Any(d => d.DilemmaId == dilemmaId && d.Owner == owner
            && d.Status is SWDilemmaStatus.Pending or SWDilemmaStatus.Chosen);

    private bool ConditionsMet(SWDilemmaPrototype proto, SWDilemmaScope scope, string? sectorId, Dictionary<string, float>? republicGlobals)
    {
        foreach (var cond in proto.Conditions)
        {
            float value;
            if (scope == SWDilemmaScope.Republic)
            {
                if (republicGlobals == null || !republicGlobals.TryGetValue(cond.Stat, out value))
                    return false;
            }
            else if (sectorId == null
                     || !_senate.SectorGlobals.TryGetValue(sectorId, out var globals)
                     || !globals.TryGetValue(cond.Stat, out value))
            {
                return false;
            }

            var ok = cond.Comparison == SWDilemmaComparison.Below
                ? value < cond.Threshold
                : value > cond.Threshold;
            if (!ok)
                return false;
        }

        return true;
    }

    private void ApplyDue()
    {
        foreach (var dilemma in _dilemmas)
        {
            if (dilemma.Status is not (SWDilemmaStatus.Pending or SWDilemmaStatus.Chosen))
                continue;

            var due = !_time.DayActive || _time.CurrentHour >= dilemma.ApplyHour;
            if (due)
                ApplyDilemma(dilemma);
        }
    }

    private void ApplyDilemma(SWActiveDilemma dilemma)
    {
        if (!_proto.TryIndex<SWDilemmaPrototype>(dilemma.DilemmaId, out var proto))
        {
            dilemma.Status = SWDilemmaStatus.Applied;
            return;
        }

        if (dilemma.Status == SWDilemmaStatus.Chosen
            && dilemma.ChosenOption >= 0
            && dilemma.ChosenOption < proto.Options.Count)
        {
            var option = proto.Options[dilemma.ChosenOption];
            ApplyScaled(dilemma.Scope, dilemma.SectorId, option.PolicyEffects, option.SectorEffects, SenatorScale);
            dilemma.Status = SWDilemmaStatus.Applied;
        }
        else
        {
            ApplyScaled(dilemma.Scope, dilemma.SectorId, proto.FailPolicyEffects, proto.FailSectorEffects, 1f);
            dilemma.Status = SWDilemmaStatus.Failed;
        }
    }

    public void AdminAddDilemma(SWDilemmaScope scope, string owner, string? sectorId, string dilemmaId)
    {
        var isBudget = _proto.TryIndex<SWDilemmaPrototype>(dilemmaId, out var proto) && proto.Budget;

        _dilemmas.Add(new SWActiveDilemma
        {
            Id = _nextId++,
            DilemmaId = dilemmaId,
            Scope = scope,
            IsBudget = isBudget,
            Owner = owner,
            SectorId = sectorId,
            Window = SWDilemmaWindow.Afternoon,
            DeadlineHour = SWSenateTimeSystem.EndHour,
            ApplyHour = SWSenateTimeSystem.EndHour,
            Status = SWDilemmaStatus.Pending,
        });
    }

    public bool Resolve(int id, int option, string owner)
    {
        var dilemma = _dilemmas.FirstOrDefault(d => d.Id == id);
        if (dilemma == null || dilemma.Owner != owner || dilemma.Status != SWDilemmaStatus.Pending)
            return false;

        if (!_time.DayActive || _time.CurrentHour >= dilemma.DeadlineHour)
            return false;

        if (!_proto.TryIndex<SWDilemmaPrototype>(dilemma.DilemmaId, out var proto))
            return false;

        // Corrupt option lives just past the real options.
        if (option == proto.Options.Count)
            return ResolveCorrupt(dilemma, proto);

        if (option < 0 || option >= proto.Options.Count)
            return false;

        var chosen = proto.Options[option];

        // Appeal option: delegate the dilemma to the named ministry instead of resolving it directly.
        if (chosen.Ministry is { } ministryId)
            return Delegate(dilemma, option, ministryId);

        var cost = dilemma.IsBudget ? (int) (chosen.Cost * _corruption.CostMultiplier(dilemma.SectorId)) : chosen.Cost;
        if (dilemma.IsBudget && !_budget.Spend(dilemma.Owner, cost))
            return false;

        dilemma.ChosenOption = option;
        dilemma.Status = SWDilemmaStatus.Chosen;
        return true;
    }

    private bool ResolveCorrupt(SWActiveDilemma dilemma, SWDilemmaPrototype proto)
    {
        var beneficiary = dilemma.Scope == SWDilemmaScope.Republic
            ? _senate.Chancellor
            : dilemma.SectorId != null && _senate.SectorHolders.TryGetValue(dilemma.SectorId, out var h) ? h : null;
        if (beneficiary == null)
            return false;

        _corruption.TakeBribe(beneficiary, dilemma.SectorId, KickbackFor(dilemma));

        // The problem festers while the money is skimmed — half the failure lands.
        ApplyScaled(dilemma.Scope, dilemma.SectorId, proto.FailPolicyEffects, proto.FailSectorEffects, 0.5f);
        // Mask the choice in the public summary as an ordinary (non-ministry) option so the bribe — and even
        // the existence of the corrupt option — stays hidden. Only the owner gets the private notice.
        dilemma.ChosenOption = FirstNormalOption(proto);
        dilemma.Status = SWDilemmaStatus.Applied;
        AddNotice(dilemma.Owner, "Проблема замята за взятку. Деньги на личном счету.");
        return true;
    }

    private static int FirstNormalOption(SWDilemmaPrototype proto)
    {
        for (var i = 0; i < proto.Options.Count; i++)
            if (proto.Options[i].Ministry == null)
                return i;
        return 0;
    }

    private bool Delegate(SWActiveDilemma dilemma, int option, string ministryId)
    {
        if (!_ministry.IsClaimed(ministryId))
            return false;

        dilemma.ChosenOption = option;
        dilemma.Status = SWDilemmaStatus.Delegated;

        _requests.Add(new SWMinistryRequest
        {
            Id = _nextRequestId++,
            SourceDilemmaId = dilemma.Id,
            DilemmaId = dilemma.DilemmaId,
            MinistryId = ministryId,
            Scope = dilemma.Scope,
            Owner = dilemma.Owner,
            SectorId = dilemma.SectorId,
            OptionIndex = option,
            DeadlineHour = SWSenateTimeSystem.EndHour,
            Status = SWMinistryRequestStatus.Pending,
            Issue = RollIssue(),
        });

        // Citizens are left waiting on the ministry — a small hit while the request is unresolved.
        if (dilemma.SectorId != null)
            _senate.AddSectorBias(dilemma.SectorId, "SWGlobalSociety", WaitingDebuff);

        return true;
    }

    private SWAuditIssue RollIssue()
    {
        var roll = _random.NextFloat();
        return roll switch
        {
            < 0.40f => SWAuditIssue.Reliable,
            < 0.65f => SWAuditIssue.InflatedEstimate,
            < 0.85f => SWAuditIssue.CorruptContractor,
            _ => SWAuditIssue.UnderestimatedCrisis,
        };
    }

    private void ExpireRequests()
    {
        foreach (var request in _requests)
        {
            if (request.Status != SWMinistryRequestStatus.Pending)
                continue;

            if (_time.DayActive && _time.CurrentHour < request.DeadlineHour)
                continue;

            request.Status = SWMinistryRequestStatus.Expired;
            FailSource(request);
        }
    }

    private void FailSource(SWMinistryRequest request)
    {
        var dilemma = _dilemmas.FirstOrDefault(d => d.Id == request.SourceDilemmaId);
        if (dilemma == null || dilemma.Status != SWDilemmaStatus.Delegated)
            return;

        var mult = request.Issue == SWAuditIssue.UnderestimatedCrisis ? UnderestimatedFailFactor : 1f;
        if (_proto.TryIndex<SWDilemmaPrototype>(dilemma.DilemmaId, out var proto))
            ApplyScaled(dilemma.Scope, dilemma.SectorId, proto.FailPolicyEffects, proto.FailSectorEffects, mult);

        dilemma.Status = SWDilemmaStatus.Failed;
    }

    public bool MinistryHasMinister(string ministryId) => _ministry.IsClaimed(ministryId);

    public bool AuditRequest(string ministryId, int requestId)
    {
        var request = _requests.FirstOrDefault(r => r.Id == requestId);
        if (request == null || request.MinistryId != ministryId || request.Status != SWMinistryRequestStatus.Pending)
            return false;
        if (request.AuditDone || request.AuditPending)
            return false;

        EnsureMinistry(ministryId);
        if (_ministryAdmin[ministryId] < AuditAdminCost)
            return false;

        _ministryAdmin[ministryId] -= AuditAdminCost;
        request.AuditPending = true;
        request.AuditCompleteHour = _time.CurrentHour + 1;
        return true;
    }

    public bool ResolveRequest(string ministryId, int requestId, int optionIndex)
    {
        var request = _requests.FirstOrDefault(r => r.Id == requestId);
        if (request == null || request.MinistryId != ministryId || request.Status != SWMinistryRequestStatus.Pending)
            return false;

        if (!_proto.TryIndex<SWDilemmaPrototype>(request.DilemmaId, out var proto)
            || optionIndex < 0
            || optionIndex >= proto.MinistryOptions.Count)
            return false;

        var option = proto.MinistryOptions[optionIndex];
        var (costMod, effectMod) = DefenseModifier(ministryId, request.SectorId);
        var cost = (int) (RealCost(option.Cost, request.Issue) * costMod * _corruption.CostMultiplier(request.SectorId));

        EnsureMinistry(ministryId);
        if (_ministryBudget[ministryId] < cost)
            return false;

        _ministryBudget[ministryId] -= cost;

        var mult = (request.Issue == SWAuditIssue.Reliable ? ReliableBonus : 1f) * effectMod;
        ApplyScaled(request.Scope, request.SectorId, option.PolicyEffects, option.SectorEffects, mult);

        if (request.Issue == SWAuditIssue.CorruptContractor && request.SectorId != null)
        {
            _senate.AddSectorBias(request.SectorId, "SWGlobalSociety", -3f);
            _senate.AddSectorBias(request.SectorId, "SWGlobalEconomy", -2f);
        }

        if (ministryId == SWMinistryDefenseSystem.DefenseMinistryId)
            _defense.OnSectorEngagement(request.SectorId);
        else if (ministryId == SWMinistryInternalSecuritySystem.SecurityMinistryId)
            _security.OnSectorEngagement(request.SectorId);
        else if (ministryId == SWMinistryHealthSystem.HealthMinistryId)
            _health.OnSectorEngagement(request.SectorId);
        else if (ministryId == SWMinistryEconomySystem.EconomyMinistryId)
            _economy.OnSectorEngagement(request.SectorId);
        else if (ministryId == SWMinistryFinanceSystem.FinanceMinistryId)
            _finance.OnSectorEngagement(request.SectorId);
        else if (ministryId == SWMinistryJusticeSystem.JusticeMinistryId)
            _justice.OnSectorEngagement(request.SectorId);

        request.Status = SWMinistryRequestStatus.Resolved;
        request.MinisterOption = optionIndex;

        var dilemma = _dilemmas.FirstOrDefault(d => d.Id == request.SourceDilemmaId);
        if (dilemma != null)
            dilemma.Status = SWDilemmaStatus.Applied;

        AddNotice(request.Owner, $"{MinistryName(request.MinistryId)} удовлетворило обращение «{proto.Name}».");
        return true;
    }

    public bool RefuseRequest(string ministryId, int requestId, string reason)
    {
        var request = _requests.FirstOrDefault(r => r.Id == requestId);
        if (request == null || request.MinistryId != ministryId || request.Status != SWMinistryRequestStatus.Pending)
            return false;

        request.Status = SWMinistryRequestStatus.Refused;
        request.RefuseReason = reason.Trim();

        // Refusal does not fail the dilemma — it returns to the sector, which can still resolve it itself
        // (with a fresh deadline until the end of the day).
        var dilemma = _dilemmas.FirstOrDefault(d => d.Id == request.SourceDilemmaId);
        if (dilemma is { Status: SWDilemmaStatus.Delegated })
        {
            dilemma.Status = SWDilemmaStatus.Pending;
            dilemma.ChosenOption = -1;
            dilemma.DeadlineHour = SWSenateTimeSystem.EndHour;
            dilemma.ApplyHour = SWSenateTimeSystem.EndHour;
        }

        var name = _proto.TryIndex<SWDilemmaPrototype>(request.DilemmaId, out var proto) ? proto.Name : request.DilemmaId;
        var tail = string.IsNullOrWhiteSpace(request.RefuseReason) ? "." : $": {request.RefuseReason}";
        AddNotice(request.Owner, $"{MinistryName(request.MinistryId)} отказало в обращении «{name}»{tail} Решите вопрос самостоятельно.");
        return true;
    }

    private string MinistryName(string ministryId) =>
        _proto.TryIndex<SWMinistryPrototype>(ministryId, out var mproto) ? mproto.Name : "Министерство";

    private void AddNotice(string owner, string text)
    {
        if (!_notices.TryGetValue(owner, out var list))
        {
            list = new List<string>();
            _notices[owner] = list;
        }

        list.Insert(0, text);
        if (list.Count > 5)
            list.RemoveRange(5, list.Count - 5);
    }

    public List<string> GetNotices(string? owner) =>
        owner != null && _notices.TryGetValue(owner, out var list) ? new List<string>(list) : new List<string>();

    private (float Cost, float Effect) DefenseModifier(string ministryId, string? sectorId)
    {
        if (ministryId == SWMinistryDefenseSystem.DefenseMinistryId)
            return _defense.GetRequestModifier(sectorId);
        if (ministryId == SWMinistryInternalSecuritySystem.SecurityMinistryId)
            return _security.GetRequestModifier(sectorId);
        if (ministryId == SWMinistryHealthSystem.HealthMinistryId)
            return _health.GetRequestModifier(sectorId);
        if (ministryId == SWMinistryEconomySystem.EconomyMinistryId)
            return _economy.GetRequestModifier(sectorId);
        if (ministryId == SWMinistryFinanceSystem.FinanceMinistryId)
            return _finance.GetRequestModifier(sectorId);
        if (ministryId == SWMinistryJusticeSystem.JusticeMinistryId)
            return _justice.GetRequestModifier(sectorId);
        return (1f, 1f);
    }

    private static int RealCost(int cost, SWAuditIssue issue) =>
        issue == SWAuditIssue.InflatedEstimate ? (int) (cost * InflatedFactor) : cost;

    private void ApplyScaled(
        SWDilemmaScope scope,
        string? sectorId,
        Dictionary<ProtoId<SWStatPrototype>, float> policy,
        Dictionary<ProtoId<SWGlobalStatPrototype>, float> sector,
        float mult)
    {
        if (scope == SWDilemmaScope.Republic)
        {
            foreach (var (sub, delta) in policy)
                _senate.AddPolicy(sub, delta * mult);
        }
        else if (sectorId != null)
        {
            foreach (var (global, delta) in sector)
                _senate.AddSectorBias(sectorId, global, delta * mult);
        }
    }

    public List<SWDilemmaEntry> BuildOwnerDilemmas(string? owner) => BuildOwnerDilemmas(owner, false);

    public List<SWDilemmaEntry> BuildOwnerBudgetDilemmas(string? owner) => BuildOwnerDilemmas(owner, true);

    private List<SWDilemmaEntry> BuildOwnerDilemmas(string? owner, bool budget)
    {
        var list = new List<SWDilemmaEntry>();
        if (owner == null)
            return list;

        foreach (var dilemma in _dilemmas)
        {
            if (dilemma.Owner != owner || dilemma.IsBudget != budget)
                continue;
            if (dilemma.Status is not (SWDilemmaStatus.Pending or SWDilemmaStatus.Chosen or SWDilemmaStatus.Delegated))
                continue;

            list.Add(BuildEntry(dilemma, owner));
        }

        return list;
    }

    public List<SWDilemmaEntry> BuildAllDilemmas()
    {
        var list = new List<SWDilemmaEntry>();
        foreach (var dilemma in _dilemmas)
            list.Add(BuildEntry(dilemma, null));

        list.Sort((a, b) =>
        {
            if (a.IsRepublic != b.IsRepublic)
                return a.IsRepublic ? -1 : 1;
            var owner = string.Compare(a.OwnerLabel, b.OwnerLabel, StringComparison.Ordinal);
            return owner != 0 ? owner : a.Id.CompareTo(b.Id);
        });

        return list;
    }

    private SWDilemmaEntry BuildEntry(SWActiveDilemma dilemma, string? viewer)
    {
        _proto.TryIndex<SWDilemmaPrototype>(dilemma.DilemmaId, out var proto);

        var entry = new SWDilemmaEntry
        {
            Id = dilemma.Id,
            Name = proto?.Name ?? dilemma.DilemmaId,
            Description = proto?.Description ?? string.Empty,
            IsRepublic = dilemma.Scope == SWDilemmaScope.Republic,
            IsBudget = dilemma.IsBudget,
            OwnerLabel = dilemma.Scope == SWDilemmaScope.Republic ? "Канцлер" : SectorName(dilemma.SectorId),
            FailText = proto?.FailText ?? string.Empty,
            Status = StatusLabel(dilemma.Status),
            ChosenOption = dilemma.ChosenOption,
            CanResolve = viewer != null
                && dilemma.Owner == viewer
                && dilemma.Status == SWDilemmaStatus.Pending
                && _time.DayActive
                && _time.CurrentHour < dilemma.DeadlineHour,
            TimeLabel = dilemma.Status is SWDilemmaStatus.Pending or SWDilemmaStatus.Chosen
                ? $"Решить до {dilemma.DeadlineHour:00}:00"
                : StatusLabel(dilemma.Status),
        };

        if (proto != null)
        {
            var available = dilemma.IsBudget ? _budget.Available(dilemma.Owner) : 0;
            var costMult = _corruption.CostMultiplier(dilemma.SectorId);
            foreach (var option in proto.Options)
            {
                // A corrupt sector inflates every credit a budget option costs.
                var cost = dilemma.IsBudget ? (int) (option.Cost * costMult) : option.Cost;
                var optionEntry = new SWDilemmaOptionEntry
                {
                    Label = option.Label,
                    Description = option.Description,
                    Cost = cost,
                    Effects = dilemma.Scope == SWDilemmaScope.Republic
                        ? PolicyEffectEntries(option.PolicyEffects, SenatorScale)
                        : SectorEffectEntries(option.SectorEffects, SenatorScale),
                };

                if (option.Ministry is { } ministryId)
                {
                    optionEntry.MinistryName = _proto.TryIndex<SWMinistryPrototype>(ministryId, out var mproto)
                        ? mproto.Name
                        : ministryId;
                    optionEntry.MinistryAvailable = _ministry.IsClaimed(ministryId);
                    optionEntry.Affordable = optionEntry.MinistryAvailable;
                }
                else
                {
                    optionEntry.Affordable = !dilemma.IsBudget || available >= cost;
                }

                entry.Options.Add(optionEntry);
            }

            // Synthetic corrupt option (index == proto.Options.Count): pocket a kickback instead of solving.
            if (entry.CanResolve)
            {
                entry.Options.Add(new SWDilemmaOptionEntry
                {
                    Label = $"Прикарманить (личный счёт +{KickbackFor(dilemma)} кр)",
                    Description = "Замять проблему ради взятки. Деньги уйдут на ваш личный счёт, но коррупция и проблема вырастут.",
                    Affordable = true,
                    Corrupt = true,
                });
            }

            entry.FailEffects = dilemma.Scope == SWDilemmaScope.Republic
                ? PolicyEffectEntries(proto.FailPolicyEffects)
                : SectorEffectEntries(proto.FailSectorEffects);
        }

        return entry;
    }

    private static int KickbackFor(SWActiveDilemma dilemma) => dilemma.IsBudget ? 1500 : 1000;

    private string SectorName(string? sectorId)
    {
        if (sectorId != null && _proto.TryIndex<SWSectorPrototype>(sectorId, out var proto))
            return proto.Name;
        return sectorId ?? "—";
    }

    private static string StatusLabel(SWDilemmaStatus status) => status switch
    {
        SWDilemmaStatus.Pending => "Ожидает решения",
        SWDilemmaStatus.Chosen => "Решение принято",
        SWDilemmaStatus.Applied => "Применено",
        SWDilemmaStatus.Failed => "Провалено",
        SWDilemmaStatus.Delegated => "Передан в профильное министерство",
        _ => "—",
    };

    private List<SWBillEffectEntry> SectorEffectEntries(Dictionary<ProtoId<SWGlobalStatPrototype>, float> effects, float mult = 1f)
    {
        var list = new List<SWBillEffectEntry>();
        foreach (var global in _proto.EnumeratePrototypes<SWGlobalStatPrototype>().OrderBy(g => g.Order))
        {
            if (effects.TryGetValue(global.ID, out var delta))
                list.Add(new SWBillEffectEntry { StatName = global.ShortName, Color = global.Color, Delta = delta * mult });
        }

        return list;
    }

    private List<SWBillEffectEntry> PolicyEffectEntries(Dictionary<ProtoId<SWStatPrototype>, float> effects, float mult = 1f)
    {
        var list = new List<SWBillEffectEntry>();
        foreach (var sub in _proto.EnumeratePrototypes<SWStatPrototype>().OrderBy(s => s.Order))
        {
            if (!effects.TryGetValue(sub.ID, out var delta))
                continue;

            var color = _proto.TryIndex(sub.Global, out var global) ? global.Color : Color.White;
            list.Add(new SWBillEffectEntry { StatName = sub.ShortName, Color = color, Delta = delta * mult });
        }

        return list;
    }

    public List<SWMinistryRequestEntry> BuildMinistryRequests(string ministryId)
    {
        EnsureMinistry(ministryId);
        var budget = _ministryBudget[ministryId];
        var admin = _ministryAdmin[ministryId];

        var list = new List<SWMinistryRequestEntry>();
        foreach (var request in _requests)
        {
            if (request.MinistryId != ministryId || request.Status != SWMinistryRequestStatus.Pending)
                continue;

            _proto.TryIndex<SWDilemmaPrototype>(request.DilemmaId, out var proto);

            var entry = new SWMinistryRequestEntry
            {
                Id = request.Id,
                ProblemName = proto?.Name ?? request.DilemmaId,
                ProblemDescription = proto?.Description ?? string.Empty,
                OwnerLabel = request.Scope == SWDilemmaScope.Republic ? "Канцлер" : SectorName(request.SectorId),
                DeadlineLabel = $"Решить до {request.DeadlineHour:00}:00",
                Audited = request.AuditDone,
                AuditInProgress = request.AuditPending,
                AuditLabel = AuditLabel(request),
                IssueDescription = request.AuditDone ? IssueDescription(request.Issue) : string.Empty,
                CanAudit = !request.AuditDone && !request.AuditPending && admin >= AuditAdminCost,
                FailText = proto?.FailText ?? string.Empty,
            };

            if (proto != null)
            {
                var (costMod, effectMod) = DefenseModifier(ministryId, request.SectorId);
                var mult = (request.AuditDone && request.Issue == SWAuditIssue.Reliable ? ReliableBonus : 1f) * effectMod;

                foreach (var option in proto.MinistryOptions)
                {
                    var shownCost = (int) ((request.AuditDone ? RealCost(option.Cost, request.Issue) : option.Cost) * costMod * _corruption.CostMultiplier(request.SectorId));
                    entry.Options.Add(new SWMinistryRequestOptionEntry
                    {
                        Label = option.Label,
                        Description = option.Description,
                        Cost = shownCost,
                        Affordable = budget >= shownCost,
                        Effects = request.Scope == SWDilemmaScope.Republic
                            ? PolicyEffectEntries(option.PolicyEffects, mult)
                            : SectorEffectEntries(option.SectorEffects, mult),
                    });
                }

                if (request.AuditDone && request.Issue == SWAuditIssue.CorruptContractor && request.Scope == SWDilemmaScope.Sectoral)
                {
                    entry.SideEffects = SectorEffectEntries(new Dictionary<ProtoId<SWGlobalStatPrototype>, float>
                    {
                        { "SWGlobalSociety", -3f },
                        { "SWGlobalEconomy", -2f },
                    });
                }
            }

            list.Add(entry);
        }

        return list;
    }

    private static string AuditLabel(SWMinistryRequest request)
    {
        if (request.AuditDone)
            return IssueLabel(request.Issue);
        return request.AuditPending ? "Проверка идёт..." : "Не проверено";
    }

    private static string IssueLabel(SWAuditIssue issue) => issue switch
    {
        SWAuditIssue.InflatedEstimate => "Завышенная смета",
        SWAuditIssue.CorruptContractor => "Коррупционный подрядчик",
        SWAuditIssue.UnderestimatedCrisis => "Недооценённый кризис",
        _ => "Надёжная заявка",
    };

    private static string IssueDescription(SWAuditIssue issue) => issue switch
    {
        SWAuditIssue.InflatedEstimate => "Реальная стоимость ниже заявленной.",
        SWAuditIssue.CorruptContractor => "При одобрении будут побочные потери сектора.",
        SWAuditIssue.UnderestimatedCrisis => "Если не решить — провал будет сильнее.",
        _ => "Риск побочек снят, эффективность выше.",
    };
}

public sealed class SWActiveDilemma
{
    public int Id;
    public string DilemmaId = string.Empty;
    public SWDilemmaScope Scope;
    public bool IsBudget;
    public string Owner = string.Empty;
    public string? SectorId;
    public SWDilemmaWindow Window;
    public int DeadlineHour;
    public int ApplyHour;
    public SWDilemmaStatus Status;
    public int ChosenOption = -1;
}

public sealed class SWMinistryRequest
{
    public int Id;
    public int SourceDilemmaId;
    public string DilemmaId = string.Empty;
    public string MinistryId = string.Empty;
    public SWDilemmaScope Scope;
    public string Owner = string.Empty;
    public string? SectorId;
    public int OptionIndex;
    public int DeadlineHour;
    public SWMinistryRequestStatus Status;
    public SWAuditIssue Issue;
    public bool AuditPending;
    public bool AuditDone;
    public int AuditCompleteHour;
    public int MinisterOption = -1;
    public string RefuseReason = string.Empty;
}
