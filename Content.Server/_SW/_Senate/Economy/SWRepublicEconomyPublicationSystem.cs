using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Consoles;
using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Economy;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._SW._Senate.Economy;

/// <summary>One conversion and broadcast per completed step, shared by all consoles and players.</summary>
public sealed class SWRepublicEconomyPublicationSystem : EntitySystem
{
    [Dependency] private readonly SWRepublicEconomySystem _economy = default!;
    [Dependency] private readonly SWEmpireEconomySystem _empire = default!;
    [Dependency] private readonly SWStrategicVisibilitySystem _visibility = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly SWGovernorSystem _governors = default!;
    private readonly Dictionary<ICommonSession, int> _lastSentRevision = new();
    private readonly Dictionary<ICommonSession, TimeSpan> _requests = new();
    private readonly Dictionary<ICommonSession, EntityUid?> _lastBody = new();
    private readonly Queue<ICommonSession> _recipients = new();
    private readonly HashSet<ICommonSession> _pendingRecipients = new();
    private EconomySnapshot? _published;
    private EconomySnapshot? _publishedEmpire;
    private SWEconomyView _empireView = new() { ViewerFaction = "empire", Paused = true };
    private readonly Dictionary<ICommonSession, string> _lastFaction = new();
    private SWEconomyView _view = new() { ViewerFaction = "republic", Paused = true };
    private int _revision;
    private int _governorRevision = -1;

    public SWEconomyView View => _view;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(SWRepublicEconomySystem));
        UpdatesAfter.Add(typeof(SWEmpireEconomySystem));
        UpdatesAfter.Add(typeof(SWGovernorSystem));
        UpdatesAfter.Add(typeof(SWSenateTimeSystem));
        SubscribeNetworkEvent<SWRepublicEconomyRequestEvent>(OnRequest);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => ClearRound());
        _players.PlayerStatusChanged += OnStatusChanged;
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnStatusChanged;
        _lastSentRevision.Clear();
        _requests.Clear();
        _lastBody.Clear();
        _lastFaction.Clear();
        _recipients.Clear();
        _pendingRecipients.Clear();
        base.Shutdown();
    }

    private void ClearRound()
    {
        _published = null;
        _publishedEmpire = null;
        _view = BuildFactionView(null, ++_revision, true, "republic", null);
        _empireView = BuildFactionView(null, ++_revision, true, "empire", null);
        QueueAll();
    }

    public override void Update(float frameTime)
    {
        var snapshot = _time.ModeActive ? _economy.Snapshot : null;
        var paused = !_time.ModeActive || !_time.DayActive || _economy.Runner?.Fault != null;
        if (!ReferenceEquals(snapshot, _published) || paused != _view.Paused || _governorRevision != _governors.Revision)
        {
            if (!ReferenceEquals(snapshot, _published))
            {
                _published = snapshot;
                _view = BuildFactionView(snapshot, ++_revision, paused, "republic", _economy.GoodNames);
            }
            else
            {
                // Pause changes carry the same detached data; no model or per-world recomputation.
                _view = new SWEconomyView
                {
                    ViewerFaction = "republic", Ready = _view.Ready, Paused = paused, Revision = ++_revision, Hour = _view.Hour,
                    Worlds = _view.Worlds, Flows = _view.Flows, Links = _view.Links,
                    MedicalSupplyFlows = _view.MedicalSupplyFlows, SupplyRealSecondsPerHour = _view.SupplyRealSecondsPerHour,
                };
            }
            _view.Governors = _time.ModeActive ? _governors.BuildViews() : new();
            _governorRevision = _governors.Revision;
            QueueAll();
        }
        var empireSnapshot = _time.ModeActive ? _empire.Snapshot : null;
        var empirePaused = !_time.ModeActive || !_time.DayActive || _empire.Runner?.Fault != null;
        if (!ReferenceEquals(empireSnapshot, _publishedEmpire) || empirePaused != _empireView.Paused)
        {
            _publishedEmpire = empireSnapshot;
            _empireView = BuildFactionView(empireSnapshot, ++_revision, empirePaused, "empire", _empire.GoodNames);
            QueueAll();
        }
        // Faction changes bypass the normal one-recipient queue and revision cache.
        foreach (var session in _players.Sessions)
        {
            if (session.Status is not (SessionStatus.Connected or SessionStatus.InGame)) continue;
            var faction = _visibility.GetFaction(session);
            if (_lastFaction.TryGetValue(session, out var previousFaction) && previousFaction == faction
                && _lastBody.TryGetValue(session, out var body) && body == session.AttachedEntity) continue;
            SendFaction(session, faction);
        }
        SendNext();
    }

    private void OnRequest(SWRepublicEconomyRequestEvent message, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        var faction = _visibility.GetFaction(session);
        if (!_lastFaction.TryGetValue(session, out var previous) || previous != faction
            || !_lastBody.TryGetValue(session, out var body) || body != session.AttachedEntity)
        {
            SendFaction(session, faction);
            return;
        }
        var now = _timing.RealTime;
        if (_requests.TryGetValue(session, out var next) && now < next) return;
        _requests[session] = now + TimeSpan.FromSeconds(2);
        // The first delivery can arrive before the client's faction component. A bounded
        // retry must deliver the same revision after that component catches up.
        _lastSentRevision.Remove(session);
        Queue(session);
    }

    private void OnStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
        {
            _lastSentRevision.Remove(args.Session);
            _requests.Remove(args.Session);
            _lastBody.Remove(args.Session);
            _lastFaction.Remove(args.Session);
            _pendingRecipients.Remove(args.Session);
        }
        else if (args.NewStatus is SessionStatus.Connected or SessionStatus.InGame)
            Queue(args.Session);
    }

    private void QueueAll()
    {
        foreach (var session in _players.Sessions)
            Queue(session);
    }

    private void Queue(ICommonSession session)
    {
        if (session.Status is SessionStatus.Connected or SessionStatus.InGame && _pendingRecipients.Add(session))
            _recipients.Enqueue(session);
    }

    private void SendNext()
    {
        // The engine serializes broadcast EntityEventArgs separately for every recipient.
        // Limit that work to one client per frame; requests use the same budget as broadcasts.
        while (_recipients.TryDequeue(out var session))
        {
            if (!_pendingRecipients.Remove(session) || session.Status is not (SessionStatus.Connected or SessionStatus.InGame))
                continue;
            var faction = _visibility.GetFaction(session);
            var view = ViewFor(faction);
            if (_lastFaction.TryGetValue(session, out var prior) && prior == faction
                && _lastBody.TryGetValue(session, out var body) && body == session.AttachedEntity
                && _lastSentRevision.TryGetValue(session, out var delivered) && delivered == view.Revision)
                continue;
            SendFaction(session, faction);
            break;
        }
    }

    private SWEconomyView ViewFor(string faction) => faction switch
    {
        "republic" => _view,
        "empire" => _empireView,
        _ => new SWEconomyView { ViewerFaction = faction, Paused = true, Revision = _revision },
    };

    private void SendFaction(ICommonSession session, string faction)
    {
        var view = ViewFor(faction);
        var body = session.AttachedEntity is { } entity ? GetNetEntity(entity) : (NetEntity?) null;
        RaiseNetworkEvent(new SWRepublicEconomyUpdateEvent(view, body), session);
        _lastFaction[session] = faction;
        _lastBody[session] = session.AttachedEntity;
        _lastSentRevision[session] = view.Revision;
    }

    private SWEconomyView BuildFactionView(EconomySnapshot? snapshot, int revision, bool paused, string faction,
        IReadOnlyDictionary<string, string>? goodNames)
    {
        var view = BuildView(snapshot, revision, paused, goodNames);
        view.ViewerFaction = faction;
        var map = _prototypes.Index<SWGalacticMapPrototype>("SenateRepublic");
        var allowed = map.Worlds.Where(w => map.GetWorldFaction(w) == faction || faction == "republic" && map.GetWorldFaction(w) == "neutral").Select(w => w.Id).ToHashSet();
        view.Worlds = view.Worlds.Where(w => allowed.Contains(w.World)).ToList();
        view.Flows = view.Flows.Where(f => allowed.Contains(f.From) && allowed.Contains(f.To)).ToList();
        var edges = map.Routes.Where(r => allowed.Contains(r.From) && allowed.Contains(r.To))
            .Select(r => SWTransitLinkPrototype.LinkId(r.From, r.To)).ToHashSet();
        view.Links = view.Links.Where(l => edges.Contains(l.Edge)).ToList();
        view.MedicalSupplyFlows = view.MedicalSupplyFlows.Where(f => allowed.Contains(f.From)
            && allowed.Contains(f.To) && allowed.Contains(f.TargetWorldId)).ToList();
        return view;
    }

    public static SWEconomyView BuildView(EconomySnapshot? snapshot, int revision, bool paused, IReadOnlyDictionary<string, string>? goodNames = null)
    {
        var view = new SWEconomyView { Revision = revision, Paused = paused, Ready = snapshot != null, SupplyRealSecondsPerHour = SWSenateTimeSystem.RealSecondsPerGameHour };
        if (snapshot == null)
            return view;
        view.Hour = snapshot.Hour;
        view.MedicalSupplyFlows = SWRepublicMedicalSupplyPublication.Create(snapshot, goodNames).ToList();
        var activity = snapshot.Goods.ToDictionary(g => (g.World, g.Good));
        var activityByWorld = snapshot.Goods.ToLookup(g => g.World);
        var inventory = snapshot.Inventory.ToLookup(i => i.World);
        var projectSupply = snapshot.ProjectSupply.ToDictionary(s => (s.World, s.Good), s => s.Available);
        var services = snapshot.Services.ToLookup(s => s.World);
        var assets = snapshot.Assets.ToLookup(a => a.World);
        var orders = snapshot.Orders.ToLookup(o => o.World);
        var production = snapshot.Production.ToLookup(p => p.World);
        var capacities = snapshot.Capacities.ToDictionary(c => c.World);
        var diets = snapshot.Diets.ToDictionary(d => d.World);
        const double hours = RepublicEconomyModel.StepHours;
        var worlds = snapshot.Inventory.Select(i => i.World).Concat(snapshot.Services.Select(s => s.World))
            .Concat(snapshot.Assets.Select(a => a.World)).Concat(snapshot.Orders.Select(o => o.World))
            .Concat(capacities.Keys).Concat(diets.Keys).Concat(activityByWorld.Select(g => g.Key))
            .Concat(production.Select(g => g.Key)).Distinct();
        foreach (var id in worlds)
        {
            var world = new SWEconomyWorldView { World = id };
            foreach (var line in production[id])
                world.Production.Add(new SWEconomyProductionView
                {
                    Recipe = line.Recipe, CompletedCyclesPerHour = line.CompletedCyclesLastStep / hours,
                    StartedCyclesPerHour = line.StartedCyclesLastStep / hours,
                    WorkInProgress = line.WorkInProgress, BaseCyclesPerHour = line.BaseCyclesPerHour,
                    EffectiveCyclesPerHour = line.EffectiveCyclesPerHour,
                });
            var stores = inventory[id].ToDictionary(i => i.Good);
            foreach (var good in stores.Keys.Union(activityByWorld[id].Select(g => g.Good)))
            {
                stores.TryGetValue(good, out var stock);
                activity.TryGetValue((id, good), out var value);
                world.Goods.Add(new SWEconomyGoodView
                {
                    Good = good, Stock = stock?.Stock ?? 0, Capacity = stock?.Capacity ?? 0,
                    AvailableForProjects = projectSupply.GetValueOrDefault((id, good)),
                    ProducedPerHour = (value?.Produced ?? 0) / hours,
                    ConsumedPerHour = (value?.Consumed ?? 0) / hours,
                    RequiredPerHour = (value?.Required ?? 0) / hours,
                    ImportedPerHour = (value?.Imported ?? 0) / hours,
                    ExportedPerHour = (value?.Exported ?? 0) / hours,
                    Incoming = value?.Incoming ?? 0,
                });
            }
            foreach (var service in services[id])
            {
                if (service.Group == "orders")
                    continue;
                world.Services.Add(new SWEconomyServiceView
                {
                    Group = service.Group, RequiredPerHour = service.LastRequired / hours,
                    ServedPerHour = service.LastServed / hours,
                });
            }
            foreach (var asset in assets[id])
                world.Assets.Add(new SWEconomyAssetView
                {
                    Good = asset.Good, Installed = asset.Installed, Operational = asset.Operational,
                    Target = asset.Target, Effect = asset.Effect, EffectiveBonus = asset.EffectiveBonus,
                });
            foreach (var order in orders[id])
                world.Orders.Add(new SWEconomyOrderView
                {
                    Good = order.Good, Ordered = order.Ordered, Delivered = order.Delivered,
                    CompletedHour = order.CompletedHour,
                });
            if (capacities.TryGetValue(id, out var capacity))
                world.Capacity = new SWEconomyCapacityView
                {
                    ProductionMultiplier = capacity.ProductionMultiplier, LaborCapacity = capacity.LaborCapacity,
                    StorageMultiplier = capacity.StorageMultiplier, DomesticPower = capacity.DomesticPower,
                    ReactorCapacityPower = capacity.ReactorCapacityPower,
                    FleetCapacity = capacity.FleetCapacity, FleetOccupied = capacity.FleetOccupied,
                };
            if (diets.TryGetValue(id, out var diet))
                world.Diet = new SWEconomyDietView { Consumed = diet.Consumed, Quality = diet.Quality };
            view.Worlds.Add(world);
        }
        view.Flows = snapshot.Flows.Select(f => new SWEconomyRouteFlow
        {
            Edge = f.Edge, From = f.From, To = f.To, Good = f.Good,
            UnitsPerHour = f.Dispatched / hours, InTransit = f.InTransit, TravelHours = f.TravelHours,
        }).ToList();
        view.Links = snapshot.Edges.Select(e => new SWEconomyLinkView
        {
            Edge = e.Id, LoadedSlotsPerHour = (e.UsedSlotsLastStep - e.EmptySlotsLastStep) / hours,
            EmptySlotsPerHour = e.EmptySlotsLastStep / hours, CapacitySlotsPerHour = e.CapacitySlotsPerHour,
        }).ToList();
        return view;
    }
}
