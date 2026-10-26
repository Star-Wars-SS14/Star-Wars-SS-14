using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Economy;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Economy;

/// <summary>Physical supply with existing Senate needs and reversible shortage consequences.</summary>
public sealed class SWRepublicEconomySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWMinistryHealthSystem _health = default!;
    [Dependency] private readonly SWMinistryDefenseSystem _defense = default!;
    private SWRepublicEconomyRunner? _runner;
    private EconomyDefinition? _definition;
    private EconomyDemandModifier[] _modifiers = Array.Empty<EconomyDemandModifier>();
    private EconomyDemandModifier[] _manualModifiers = Array.Empty<EconomyDemandModifier>();
    private readonly Dictionary<string, string> _worldSectors = new();
    private readonly Dictionary<string, string> _goodNames = new();
    private EconomySnapshot? _navySupplySnapshot;
    private SWNavySupplyState? _navySupplyState;
    private SWRepublicSupplyFeedback? _feedback;
    private EconomySnapshot? _feedbackSnapshot;
    private Exception? _reportedFault;
    public SWRepublicEconomyRunner? Runner => _runner;
    public EconomySnapshot? Snapshot => _runner?.Snapshot;
    internal EconomyDefinition? Definition => _definition;
    public IReadOnlyList<EconomySupplyPenalty> SupplyPenalties => _feedback?.Penalties ?? Array.Empty<EconomySupplyPenalty>();

    public bool QueueProject(EconomyProjectRequest project) =>
        _time.ModeActive && _time.DayActive && _runner != null && _runner.TryQueueProject(project);

    public bool QueueNavyProject(string id, string world, SWNavyProjectKind kind, double scale = 1,
        int? recipientFleetId = null)
    {
        if (!Enum.IsDefined(kind) || !double.IsFinite(scale) || scale <= 0 || scale > 100)
            return false;
        return QueueProject(SWRepublicNavyLogistics.CreateProject(id, world, kind, scale, recipientFleetId));
    }

    /// <summary>One detached publication; never reads mutable worker cargo or guesses from incoming totals.</summary>
    public SWNavySupplyState GetNavySupplyState()
    {
        var snapshot = Snapshot;
        if (_navySupplyState != null && ReferenceEquals(snapshot, _navySupplySnapshot)) return _navySupplyState;
        _navySupplySnapshot = snapshot;
        return _navySupplyState = SWRepublicNavySupplyPublication.Create(snapshot, _goodNames);
    }

    public IReadOnlyList<Content.Shared._SW._Senate.Ministries.SWDefenseSupplyFlowView> GetNavySupplyFlows() => GetNavySupplyState().Flows;

    /// <summary>Null may mean admitted but not yet published. Never re-admit a pending ID.</summary>
    public EconomyProjectState? GetProject(string id) => Snapshot?.Projects.FirstOrDefault(p => p.Id == id);

    /// <summary>Atomically replaces the Defense event restrictions; empty releases all lanes.</summary>
    public bool SetDefenseTransitRestrictions(IReadOnlyDictionary<string, double> multipliers) =>
        _runner?.SetTransitRestrictions(multipliers) ?? false;

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());
        // Compile immutable definitions once during system startup, never for a console opening.
        _definition = SWRepublicEconomyFactory.Create(_prototypes);
        foreach (var good in _definition.Goods)
            _goodNames[good.Id] = _prototypes.Index<SWCommodityPrototype>(good.Id).Name;
        var scenario = _prototypes.Index<SWRepublicEconomyScenarioPrototype>("SenateRepublicEconomy");
        foreach (var world in _prototypes.Index(scenario.Map).Worlds)
        {
            if (world.Sector is { } sector
                && _prototypes.TryIndex<SWPlanetEconomyPrototype>(world.Id, out var economy) && economy.ActiveEconomy)
                _worldSectors[world.Id] = sector.Id;
        }
        _feedback = new SWRepublicSupplyFeedback(_worldSectors, _definition.Demands,
            _worldSectors.Keys.ToDictionary(w => w, w => _prototypes.Index<SWPlanetEconomyPrototype>(w).PopulationUnits));
        _runner = new SWRepublicEconomyRunner(_definition);
    }

    private void Reset()
    {
        _runner?.Reset();
        _modifiers = Array.Empty<EconomyDemandModifier>();
        _manualModifiers = Array.Empty<EconomyDemandModifier>();
        _feedback?.Reset();
        _feedbackSnapshot = null;
        _navySupplySnapshot = null;
        _navySupplyState = null;
        _senate.ClearSupplyPenalties();
        _reportedFault = null;
    }

    public override void Update(float frameTime)
    {
        if (_runner == null)
            return;
        var running = _time.ModeActive && _time.DayActive;
        var activeHours = (double) _time.ActiveSeconds(frameTime) / SWSenateTimeSystem.RealSecondsPerGameHour;
        // Capture Senate conditions once before a scheduled step, not for every frame or console.
        if (running && !_runner.Busy
            && (_runner.Snapshot == null || _runner.PendingHours + activeHours + 1e-9 >= SWRepublicEconomyRunner.StepHours))
            RefreshDemandModifiers();
        _runner.Update(activeHours, running, _modifiers);
        // The runner may publish the last daytime step just after night or the round end begins.
        // Settle that accepted active time exactly once; never use wall time for supply feedback.
        if (_runner.Snapshot is { } snapshot && !ReferenceEquals(snapshot, _feedbackSnapshot))
        {
            _feedbackSnapshot = snapshot;
            if (_feedback?.Update(snapshot, true) == true)
            {
                foreach (var penalty in _feedback.Penalties)
                    _senate.SetSupplyPenalty(penalty.Sector, penalty.Global, penalty.Offset);
            }
        }
        if (_runner.Fault is { } fault && !ReferenceEquals(fault, _reportedFault))
        {
            _reportedFault = fault;
            Log.Error($"Republic economy stopped after a calculation failure: {fault}");
        }
    }

    /// <summary>Server-side event input, applied at the next whole economic step; commissioning quotas never scale.</summary>
    public bool SetDemandMultiplier(string world, string group, double multiplier)
    {
        if (_definition == null || !double.IsFinite(multiplier) || multiplier < 0 || multiplier > 5
            || group == "orders" || !_definition.Demands.Any(d => d.World == world && d.Group == group))
            return false;
        var next = _manualModifiers.Where(m => m.World != world || m.Group != group).ToList();
        if (Math.Abs(multiplier - 1) > 1e-9)
            next.Add(new EconomyDemandModifier(world, group, multiplier));
        _manualModifiers = next.ToArray();
        RefreshDemandModifiers();
        return true;
    }

    private void RefreshDemandModifiers()
    {
        if (_definition == null)
            return;
        var infection = _worldSectors.ToDictionary(w => w.Key, w => (double) _health.SectorInfection(w.Value));
        _modifiers = SWRepublicNeedsPolicy.BuildModifiers(_definition.Demands, infection, _defense.Stance, _manualModifiers);
    }
}
