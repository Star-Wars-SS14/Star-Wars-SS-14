using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Economy;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Server._SW._Senate.Economy;

/// <summary>An independent Imperial ledger and worker; no Republic ministry consumes this state.</summary>
public sealed class SWEmpireEconomySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    private readonly Dictionary<string, string> _goodNames = new();
    private Exception? _reportedFault;
    public SWRepublicEconomyRunner? Runner { get; private set; }
    public EconomyDefinition? Definition { get; private set; }
    public EconomySnapshot? Snapshot => Runner?.Snapshot;
    public IReadOnlyDictionary<string, string> GoodNames => _goodNames;

    public bool QueueNavyProject(string id, string world, SWNavyProjectKind kind, int? recipientFleetId = null)
    {
        if (!_time.ModeActive || !_time.DayActive || Runner == null || Definition == null || !Enum.IsDefined(kind)
            || !Definition.Worlds.Any(w => w.Id == world && w.Active))
            return false;
        return Runner.TryQueueProject(SWRepublicNavyLogistics.CreateProject(id, world, kind, recipientFleetId: recipientFleetId));
    }

    public EconomyProjectState? GetProject(string id) => Snapshot?.Projects.FirstOrDefault(p => p.Id == id);

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => { Runner?.Reset(); _reportedFault = null; });
        Definition = SWRepublicEconomyFactory.CreateEmpire(_prototypes);
        foreach (var good in Definition.Goods)
            _goodNames[good.Id] = _prototypes.Index<SWCommodityPrototype>(good.Id).Name;
        Runner = new SWRepublicEconomyRunner(Definition);
    }

    public override void Update(float frameTime)
    {
        if (Runner == null) return;
        var running = _time.ModeActive && _time.DayActive;
        var hours = (double) _time.ActiveSeconds(frameTime) / SWSenateTimeSystem.RealSecondsPerGameHour;
        Runner.Update(hours, running, Array.Empty<EconomyDemandModifier>(), _time.Day);
        if (Runner.Fault is { } fault && !ReferenceEquals(fault, _reportedFault))
        {
            _reportedFault = fault;
            Log.Error($"Imperial economy stopped after a calculation failure: {fault}");
        }
    }
}
