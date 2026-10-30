using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Content.Server._SW._Senate.Economy.Simulation;

namespace Content.Server._SW._Senate.Economy;

/// <summary>
/// One worker owns the mutable simulation. This scheduler is called only by the game thread.
/// Reset replaces the session, but drains the old task before admitting another: no overlap or blocking waits.
/// </summary>
public sealed class SWRepublicEconomyRunner
{
    public const double StepHours = .25;
    private EconomyDefinition _definition;
    private readonly EconomyDefinition _initialDefinition;
    private readonly Func<Func<WorkResult>, Task<WorkResult>> _dispatch;
    private Session _session;
    private Task<WorkResult>? _pending;
    private int _pendingGeneration;
    private int _pendingDay;
    public int SnapshotDay { get; private set; }
    private int _generation;
    private double _dueHours;
    private readonly LinkedList<(int Day, double Hours)> _dueDays = new();
    private bool _initialized;
    private readonly Queue<EconomyProjectRequest> _projectCommands = new();
    private readonly Queue<EconomyExternalTransfer> _externalCommands = new();
    private readonly HashSet<string> _externalIds = new();
    private readonly Dictionary<string, EconomyExternalTransferResult> _externalResults = new();
    private readonly Dictionary<string, CancellationTokenSource> _withdrawalCancellation = new();
    private readonly Dictionary<string, CancellationTokenSource> _depositCancellation = new();
    private readonly HashSet<string> _projectIds = new();
    private readonly Dictionary<(string World, string Recipe), double> _committedExpansion = new();
    private readonly Dictionary<(string World, string Recipe), string> _pendingExpansion = new();
    private readonly Dictionary<(string World, string Recipe), (string Id, EconomyProduction Line)> _pendingNewLines = new();
    private readonly HashSet<string> _committedTransit = new();
    private readonly Dictionary<string, Dictionary<string, double>> _transitSources = new();
    private readonly Dictionary<string, EconomyProductionModifier[]> _productionSources = new();
    private readonly Dictionary<string, double> _productionExpiry = new();
    private EconomyQuarantineRule[] _quarantines = Array.Empty<EconomyQuarantineRule>();

    public EconomySnapshot? Snapshot { get; private set; }
    public EconomyDefinition Definition => _definition;
    public Exception? Fault { get; private set; }
    public bool Busy => _pending != null;
    public double PendingHours => _dueHours;
    public double LastWorkerMilliseconds { get; private set; }
    public double PeakWorkerMilliseconds { get; private set; }
    public double LastMainThreadMilliseconds { get; private set; }
    public double PeakMainThreadMilliseconds { get; private set; }

    public SWRepublicEconomyRunner(EconomyDefinition definition,
        Func<Func<WorkResult>, Task<WorkResult>>? dispatch = null)
    {
        _definition = definition with
        {
            Goods = definition.Goods.ToArray(), Worlds = definition.Worlds.ToArray(),
            Inventory = definition.Inventory.ToArray(), Edges = definition.Edges.ToArray(),
            Shipping = definition.Shipping.ToArray(), Demands = definition.Demands.ToArray(),
            NavyDepots = definition.NavyDepots.Select(d => d with { Materials = d.Materials.ToArray() }).ToArray(),
            Production = definition.Production.Select(p => p with
                { Inputs = p.Inputs.ToArray(), Outputs = p.Outputs.ToArray() }).ToArray(),
        };
        _dispatch = dispatch ?? (work => Task.Run(work));
        _initialDefinition = _definition;
        _session = new Session(_definition);
    }

    public bool IsExpansionPending(string world, string recipe) => _pendingExpansion.ContainsKey((world, recipe));

    public bool TryQueueExternalTransfer(EconomyExternalTransfer command)
    {
        if (Fault != null || string.IsNullOrWhiteSpace(command.Id) || command.Id.Length > 128
            || !double.IsFinite(command.Amount) || command.Amount <= 0 || _externalIds.Contains(command.Id)
            || !_definition.Worlds.Any(w => w.Id == command.World && w.Active)
            || !_definition.Inventory.Any(i => i.World == command.World && i.Good == command.Good)) return false;
        _externalIds.Add(command.Id); _externalCommands.Enqueue(command);
        if (!command.Deposit) _withdrawalCancellation[command.Id] = new CancellationTokenSource();
        else _depositCancellation[command.Id] = new CancellationTokenSource();
        return true;
    }

    public EconomyExternalTransferResult? GetExternalTransferResult(string id) => _externalResults.GetValueOrDefault(id);

    /// <summary>Game-thread view of the currently submitted civilian transit restrictions.</summary>
    public bool AllowsExternalTransit(string edge, string from, string to, string good)
    {
        if (_transitSources.Values.Any(source => source.TryGetValue(edge, out var factor) && factor <= 0))
            return false;
        foreach (var rule in _quarantines)
        {
            var insideFrom = rule.Worlds.Contains(from);
            var insideTo = rule.Worlds.Contains(to);
            if (insideFrom == insideTo) continue;
            if (!insideFrom && insideTo && rule.Humanitarian &&
                good is "water" or "fresh_food" or "synth_food" or "rations" or "drugs" or "medical_supplies") continue;
            return false;
        }
        return true;
    }

    /// <summary>Stops unexecuted hostile withdrawals without touching transfers already acknowledged.</summary>
    public void CancelPendingExternalWithdrawals()
    {
        foreach (var token in _withdrawalCancellation.Values) token.Cancel();
    }

    public void CancelExternalTransfer(string id)
    {
        if (_withdrawalCancellation.TryGetValue(id, out var withdrawal)) withdrawal.Cancel();
        if (_depositCancellation.TryGetValue(id, out var deposit)) deposit.Cancel();
    }

    /// <summary>Called only on the game thread; applied by the next active simulation step.</summary>
    public bool TryQueueProject(EconomyProjectRequest request)
    {
        if (Fault != null || !RepublicEconomyModel.ValidProject(_definition, request) || _projectIds.Contains(request.Id))
            return false;
        if (request.NewProduction is { } newLine && _pendingNewLines.ContainsKey((newLine.World, newLine.Recipe)))
            return false;
        if (request.TransitExpansion is { } transit && _committedTransit.Contains(transit.Edge))
            return false;
        if (request.Expansion is { } expansion)
        {
            var key = (request.World, expansion.Recipe);
            if (_pendingExpansion.ContainsKey(key)) return false;
            var committed = _committedExpansion.GetValueOrDefault(key) + expansion.AddedCyclesPerHour;
            var recipe = _definition.Production.Single(p => p.World == request.World && p.Recipe == expansion.Recipe);
            if (!double.IsFinite(committed) || committed > recipe.Rate || !double.IsFinite(recipe.Rate + committed))
                return false;
            _committedExpansion[key] = committed;
            _pendingExpansion[key] = request.Id;
        }
        _projectIds.Add(request.Id);
        if (request.NewProduction is { } acceptedLine)
            _pendingNewLines[(acceptedLine.World, acceptedLine.Recipe)] = (request.Id, acceptedLine);
        if (request.TransitExpansion is { } acceptedTransit)
            _committedTransit.Add(acceptedTransit.Edge);
        _projectCommands.Enqueue(request with { Materials = request.Materials.ToArray() });
        return true;
    }

    /// <summary>Game-thread input only. A detached replacement is captured by the next worker.</summary>
    public bool SetTransitRestrictions(IReadOnlyDictionary<string, double> multipliers) => SetTransitRestrictions("defense", multipliers);
    public bool SetTransitRestrictions(string source, IReadOnlyDictionary<string, double> multipliers)
    {
        if (string.IsNullOrWhiteSpace(source) || multipliers.Any(p => !double.IsFinite(p.Value) || p.Value < 0 || p.Value > 1
                || !_definition.Edges.Any(e => e.Id == p.Key)))
            return false;
        _transitSources[source] = multipliers.Where(p => p.Value < 1).ToDictionary(p => p.Key, p => p.Value);
        return true;
    }

    public bool SetProductionModifiers(string source, IReadOnlyList<EconomyProductionModifier> modifiers)
    {
        if (string.IsNullOrWhiteSpace(source) || modifiers.Any(m => !double.IsFinite(m.Multiplier) || m.Multiplier < 0 || m.Multiplier > 2
                || !_definition.Production.Any(p => p.World == m.World && p.Recipe == m.Recipe))
            || modifiers.Select(m => (m.World, m.Recipe)).Distinct().Count() != modifiers.Count) return false;
        _productionSources[source] = modifiers.ToArray();
        _productionExpiry.Remove(source);
        return true;
    }

    public bool SetTimedProductionModifiers(string source, IReadOnlyList<EconomyProductionModifier> modifiers, double endHour)
    {
        if (Snapshot == null || !double.IsFinite(endHour) || endHour <= Snapshot.Hour || endHour > Snapshot.Hour + 8 ||
            !SetProductionModifiers(source, modifiers)) return false;
        _productionExpiry[source] = endHour;
        return true;
    }

    public bool SetQuarantineTransitRestrictions(IReadOnlyList<EconomyQuarantineRule> rules)
    {
        if (rules.Any(r => string.IsNullOrWhiteSpace(r.Id) || r.Worlds.Length == 0 || r.Worlds.Any(w => !_definition.Worlds.Any(v => v.Id == w && v.Active)))) return false;
        _quarantines = rules.Select(r => r with { Worlds = r.Worlds.Distinct().ToArray() }).ToArray();
        return true;
    }

    public void Reset()
    {
        _generation++;
        _definition = _initialDefinition;
        _session = new Session(_definition);
        _initialized = false;
        _dueHours = 0;
        _dueDays.Clear();
        _projectCommands.Clear();
        _externalCommands.Clear(); _externalIds.Clear(); _externalResults.Clear();
        foreach (var token in _withdrawalCancellation.Values) token.Cancel();
        _withdrawalCancellation.Clear();
        foreach (var token in _depositCancellation.Values) token.Cancel();
        _depositCancellation.Clear();
        _projectIds.Clear();
        _committedExpansion.Clear();
        _pendingExpansion.Clear();
        _pendingNewLines.Clear();
        _committedTransit.Clear();
        _transitSources.Clear();
        _productionSources.Clear();
        _productionExpiry.Clear();
        _quarantines = Array.Empty<EconomyQuarantineRule>();
        Snapshot = null;
        SnapshotDay = 0;
        Fault = null;
        LastWorkerMilliseconds = PeakWorkerMilliseconds = 0;
        LastMainThreadMilliseconds = PeakMainThreadMilliseconds = 0;
    }

    /// <param name="activeHours">Only elapsed daytime accepted by SWSenateTimeSystem.</param>
    /// <param name="running">False at night or outside the Senate; completed daytime work can still publish.</param>
    public void Update(double activeHours, bool running, EconomyDemandModifier[] modifiers, int day = 0)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            if (!double.IsFinite(activeHours) || activeHours < 0)
                throw new ArgumentOutOfRangeException(nameof(activeHours));
            if (running && activeHours > 0)
            {
                _dueHours += activeHours;
                if (_dueDays.Last is { } last && last.Value.Day == day)
                    last.Value = (day, last.Value.Hours + activeHours);
                else
                    _dueDays.AddLast((day, activeHours));
            }
            if (_pending is { IsCompleted: true })
            {
                var pending = _pending;
                _pending = null;
                try
                {
                    // IsCompleted is checked above; this never waits for worker execution.
                    var result = pending.GetAwaiter().GetResult();
                    if (_pendingGeneration == _generation)
                    {
                        Snapshot = result.Snapshot;
                        foreach (var receipt in result.ExternalResults ?? Array.Empty<EconomyExternalTransferResult>())
                        {
                            _externalResults[receipt.Id] = receipt;
                            _withdrawalCancellation.Remove(receipt.Id);
                            _depositCancellation.Remove(receipt.Id);
                        }
                        SnapshotDay = _pendingDay;
                        var completed = Snapshot.Projects.Where(p => p.CompletedHour != null).Select(p => p.Id).ToHashSet();
                        foreach (var key in _pendingNewLines.Where(p => completed.Contains(p.Value.Id)).Select(p => p.Key).ToArray())
                        {
                            _definition = _definition with { Production = _definition.Production.Append(_pendingNewLines[key].Line).ToArray() };
                            _pendingNewLines.Remove(key);
                        }
                        foreach (var key in _pendingExpansion.Where(p => completed.Contains(p.Value)).Select(p => p.Key).ToArray())
                            _pendingExpansion.Remove(key);
                        LastWorkerMilliseconds = result.Milliseconds;
                        PeakWorkerMilliseconds = Math.Max(PeakWorkerMilliseconds, result.Milliseconds);
                    }
                }
                catch (Exception exception)
                {
                    if (_pendingGeneration == _generation)
                        Fault = exception;
                }
            }
            if (_pending != null || Fault != null || !running)
                return;
            var initialize = !_initialized;
            if (!initialize && _dueHours + 1e-9 < StepHours)
                return;
            var stepDay = day;
            if (!initialize)
            {
                _dueHours = Math.Max(0, _dueHours - StepHours);
                var needed = StepHours;
                if (_dueDays.First is { } first) stepDay = first.Value.Day;
                while (needed > 1e-9 && _dueDays.First is { } node)
                {
                    var segment = node.Value;
                    var take = Math.Min(needed, segment.Hours);
                    needed -= take;
                    if (segment.Hours - take > 1e-9)
                        node.Value = (segment.Day, segment.Hours - take);
                    else
                        _dueDays.RemoveFirst();
                }
            }
            _initialized = true;
            var session = _session;
            var capturedModifiers = (EconomyDemandModifier[]) modifiers.Clone();
            var capturedRestrictions = _transitSources.ToDictionary(p => p.Key, p => p.Value.ToDictionary(v => v.Key, v => v.Value));
            var capturedQuarantines = _quarantines;
            foreach (var key in _productionExpiry.Where(p => Snapshot != null && Snapshot.Hour >= p.Value).Select(p => p.Key).ToArray())
            {
                _productionExpiry.Remove(key);
                _productionSources.Remove(key);
            }
            var capturedProduction = _productionSources.Values.SelectMany(v => v)
                .GroupBy(m => (m.World, m.Recipe)).Select(g => new EconomyProductionModifier(g.Key.World, g.Key.Recipe, g.Min(m => m.Multiplier))).ToArray();
            var projects = initialize ? Array.Empty<EconomyProjectRequest>() : _projectCommands.ToArray();
            var transfers = initialize ? Array.Empty<EconomyExternalTransfer>() : _externalCommands.ToArray();
            var cancellation = transfers.ToDictionary(t => t.Id, t => (t.Deposit ? _depositCancellation[t.Id] : _withdrawalCancellation[t.Id]).Token);
            if (!initialize)
            {
                _projectCommands.Clear();
                _externalCommands.Clear();
            }
            _pendingGeneration = _generation;
            _pendingDay = stepDay;
            _pending = _dispatch(() =>
            {
                var started = Stopwatch.GetTimestamp();
                session.Model ??= new RepublicEconomyModel(session.Definition);
                foreach (var source in capturedRestrictions)
                    session.Model.SetTransitRestrictions(source.Key, source.Value);
                session.Model.SetQuarantineTransitRestrictions(capturedQuarantines);
                session.Model.SetProductionModifiers(capturedProduction);
                foreach (var project in projects)
                    session.Model.TryAddProject(project);
                var receipts = transfers.Select(t => cancellation[t.Id].IsCancellationRequested
                    ? new EconomyExternalTransferResult(t.Id, 0) : session.Model.ApplyExternalTransfer(t)).ToArray();
                var snapshot = initialize ? session.Model.Snapshot() : session.Model.Advance(modifiers: capturedModifiers);
                return new WorkResult(snapshot, Stopwatch.GetElapsedTime(started).TotalMilliseconds, receipts);
            });
        }
        finally
        {
            LastMainThreadMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            PeakMainThreadMilliseconds = Math.Max(PeakMainThreadMilliseconds, LastMainThreadMilliseconds);
        }
    }

    public sealed record WorkResult(EconomySnapshot Snapshot, double Milliseconds, EconomyExternalTransferResult[]? ExternalResults = null);

    private sealed class Session(EconomyDefinition definition)
    {
        public readonly EconomyDefinition Definition = definition;
        // No game-thread reads; owned exclusively by the currently scheduled worker.
        public RepublicEconomyModel? Model;
    }
}
