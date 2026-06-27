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

    public const string RepublicOwner = "republic";

    // Mini-debuff to a sector whose citizens are left waiting on a delegated ministry request.
    private const float WaitingDebuff = -2f;

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
            _generated.Clear();
            _nextId = 1;
            _nextRequestId = 1;
        }

        ApplyDue();
        ExpireRequests();
        EnsureGeneration();
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
            Apply(dilemma, option.PolicyEffects, option.SectorEffects);
            dilemma.Status = SWDilemmaStatus.Applied;
        }
        else
        {
            Apply(dilemma, proto.FailPolicyEffects, proto.FailSectorEffects);
            dilemma.Status = SWDilemmaStatus.Failed;
        }
    }

    private void Apply(
        SWActiveDilemma dilemma,
        Dictionary<ProtoId<SWStatPrototype>, float> policy,
        Dictionary<ProtoId<SWGlobalStatPrototype>, float> sector)
    {
        if (dilemma.Scope == SWDilemmaScope.Republic)
        {
            foreach (var (sub, delta) in policy)
                _senate.AddPolicy(sub, delta);
        }
        else if (dilemma.SectorId != null)
        {
            foreach (var (global, delta) in sector)
                _senate.AddSectorBias(dilemma.SectorId, global, delta);
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

        if (!_proto.TryIndex<SWDilemmaPrototype>(dilemma.DilemmaId, out var proto)
            || option < 0
            || option >= proto.Options.Count)
            return false;

        var chosen = proto.Options[option];

        // Appeal option: delegate the dilemma to the named ministry instead of resolving it directly.
        if (chosen.Ministry is { } ministryId)
            return Delegate(dilemma, option, ministryId);

        if (dilemma.IsBudget && !_budget.Spend(dilemma.Owner, chosen.Cost))
            return false;

        dilemma.ChosenOption = option;
        dilemma.Status = SWDilemmaStatus.Chosen;
        return true;
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

        if (_proto.TryIndex<SWDilemmaPrototype>(dilemma.DilemmaId, out var proto))
            Apply(dilemma, proto.FailPolicyEffects, proto.FailSectorEffects);

        dilemma.Status = SWDilemmaStatus.Failed;
    }

    public bool MinistryHasMinister(string ministryId) => _ministry.IsClaimed(ministryId);

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
            foreach (var option in proto.Options)
            {
                var optionEntry = new SWDilemmaOptionEntry
                {
                    Label = option.Label,
                    Description = option.Description,
                    Cost = option.Cost,
                    Effects = dilemma.Scope == SWDilemmaScope.Republic
                        ? PolicyEffectEntries(option.PolicyEffects)
                        : SectorEffectEntries(option.SectorEffects),
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
                    optionEntry.Affordable = !dilemma.IsBudget || available >= option.Cost;
                }

                entry.Options.Add(optionEntry);
            }

            entry.FailEffects = dilemma.Scope == SWDilemmaScope.Republic
                ? PolicyEffectEntries(proto.FailPolicyEffects)
                : SectorEffectEntries(proto.FailSectorEffects);
        }

        return entry;
    }

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

    private List<SWBillEffectEntry> SectorEffectEntries(Dictionary<ProtoId<SWGlobalStatPrototype>, float> effects)
    {
        var list = new List<SWBillEffectEntry>();
        foreach (var global in _proto.EnumeratePrototypes<SWGlobalStatPrototype>().OrderBy(g => g.Order))
        {
            if (effects.TryGetValue(global.ID, out var delta))
                list.Add(new SWBillEffectEntry { StatName = global.ShortName, Color = global.Color, Delta = delta });
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
    public int MinisterOption = -1;
    public string RefuseReason = string.Empty;
}
