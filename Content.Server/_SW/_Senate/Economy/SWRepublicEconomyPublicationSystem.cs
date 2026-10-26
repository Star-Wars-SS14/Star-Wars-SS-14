using System.Linq;
using Content.Server._SW._Senate.Core;
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
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly SWGovernorSystem _governors = default!;
    private readonly Dictionary<ICommonSession, TimeSpan> _requests = new();
    private readonly Dictionary<ICommonSession, int> _lastSentRevision = new();
    private readonly Queue<ICommonSession> _recipients = new();
    private readonly HashSet<ICommonSession> _pendingRecipients = new();
    private EconomySnapshot? _published;
    private SWEconomyView _view = new() { Paused = true };
    private int _revision;
    private int _governorRevision = -1;

    public SWEconomyView View => _view;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(SWRepublicEconomySystem));
        UpdatesAfter.Add(typeof(SWGovernorSystem));
        UpdatesAfter.Add(typeof(SWSenateTimeSystem));
        SubscribeNetworkEvent<SWRepublicEconomyRequestEvent>(OnRequest);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => ClearRound());
        _players.PlayerStatusChanged += OnStatusChanged;
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnStatusChanged;
        _requests.Clear();
        _lastSentRevision.Clear();
        _recipients.Clear();
        _pendingRecipients.Clear();
        base.Shutdown();
    }

    private void ClearRound()
    {
        _published = null;
        _view = BuildView(null, ++_revision, true);
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
                _view = BuildView(snapshot, ++_revision, paused);
            }
            else
            {
                // Pause changes carry the same detached data; no model or per-world recomputation.
                _view = new SWEconomyView
                {
                    Ready = _view.Ready, Paused = paused, Revision = ++_revision, Hour = _view.Hour,
                    Worlds = _view.Worlds, Flows = _view.Flows, Links = _view.Links,
                };
            }
            _view.Governors = _time.ModeActive ? _governors.BuildViews() : new();
            _governorRevision = _governors.Revision;
            QueueAll();
        }
        SendNext();
    }

    private void OnRequest(SWRepublicEconomyRequestEvent message, EntitySessionEventArgs args)
    {
        var now = _timing.RealTime;
        if (_lastSentRevision.TryGetValue(args.SenderSession, out var delivered) && delivered == _view.Revision)
            return;
        if (_requests.TryGetValue(args.SenderSession, out var next) && now < next)
            return;
        _requests[args.SenderSession] = now + TimeSpan.FromSeconds(2);
        Queue(args.SenderSession);
    }

    private void OnStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
        {
            _requests.Remove(args.Session);
            _lastSentRevision.Remove(args.Session);
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
            if (_lastSentRevision.TryGetValue(session, out var delivered) && delivered == _view.Revision)
                continue;
            // Queue entries never capture a view: a new round/pause supersedes every unsent update.
            RaiseNetworkEvent(new SWRepublicEconomyUpdateEvent(_view), session);
            _lastSentRevision[session] = _view.Revision;
            break;
        }
    }

    public static SWEconomyView BuildView(EconomySnapshot? snapshot, int revision, bool paused)
    {
        var view = new SWEconomyView { Revision = revision, Paused = paused, Ready = snapshot != null };
        if (snapshot == null)
            return view;
        view.Hour = snapshot.Hour;
        var activity = snapshot.Goods.ToDictionary(g => (g.World, g.Good));
        var activityByWorld = snapshot.Goods.ToLookup(g => g.World);
        var inventory = snapshot.Inventory.ToLookup(i => i.World);
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
