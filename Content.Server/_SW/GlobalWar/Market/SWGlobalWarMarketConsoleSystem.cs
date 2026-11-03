using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Market;

public sealed class SWGlobalWarMarketConsoleSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarGameruleSystem _globalWarGamerule = default!;
    [Dependency] private readonly SWGlobalWarMarketSystem _markets = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private TimeSpan _nextUpdate;
    private long _snapshotSequence;
    private readonly Dictionary<(EntityUid Console, EntityUid Actor), Guid> _subscriptions = new();
    private readonly Dictionary<(EntityUid Console, EntityUid Actor),
        (Guid Generation, Guid Assignment, long Revision, SWGlobalWarFreightQuery Query)> _queries = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarMarketConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SWGlobalWarMarketConsoleComponent, BoundUIClosedEvent>(OnClosed);
        Subs.BuiEvents<SWGlobalWarMarketConsoleComponent>(SWGlobalWarMarketConsoleUiKey.Key, subs =>
        {
            subs.Event<SWGlobalWarMarketPageRequest>(OnPageRequest);
            subs.Event<SWGlobalWarMarketOpen>(OnSubscribe);
            subs.Event<SWGlobalWarFlowRequest>(OnFlowRequest);
            subs.Event<SWGlobalWarFlowGrantRequest>(OnFlowGrant);
            subs.Event<SWGlobalWarFlowRevokeRequest>(OnFlowRevoke);
            subs.Event<SWGlobalWarGrowthRequest>(OnGrowth);
            subs.Event<SWGlobalWarReturnRequest>(OnReturn);
            subs.Event<SWGlobalWarLanthanumRequest>(OnLanthanum);
            subs.Event<SWGlobalWarSupplyRequest>(OnSupply);
            subs.Event<SWGlobalWarSupplyWorkRequest>(OnSupplyWork);
        });
        SubscribeLocalEvent<SWGlobalWarAssignmentsChangedEvent>((ref SWGlobalWarAssignmentsChangedEvent _) => Refresh());
        SubscribeLocalEvent<SWGlobalWarOwnershipChangedEvent>((ref SWGlobalWarOwnershipChangedEvent _) => Refresh());
        SubscribeLocalEvent<SWGlobalWarCampaignStartedEvent>((ref SWGlobalWarCampaignStartedEvent _) => Invalidate());
        SubscribeLocalEvent<SWGlobalWarCampaignStoppedEvent>((ref SWGlobalWarCampaignStoppedEvent _) => Invalidate());
    }

    private bool ValidSubscription(EntityUid console, EntityUid actor, Guid subscription)
    {
        return _ui.IsUiOpen(console, SWGlobalWarMarketConsoleUiKey.Key, actor) &&
            _subscriptions.TryGetValue((console, actor), out var current) && current == subscription;
    }

    private void OnLanthanum(Entity<SWGlobalWarMarketConsoleComponent> ent, ref SWGlobalWarLanthanumRequest args)
    {
        if (args.Command == null || !ValidSubscription(ent.Owner, args.Actor, args.Subscription) ||
            !TryComp<ActorComponent>(args.Actor, out var player))
            return;
        _markets.TryConfigureLanthanum(player.PlayerSession, args.Command, out var result);
        _ui.ServerSendUiMessage(ent.Owner, SWGlobalWarMarketConsoleUiKey.Key,
            new SWGlobalWarLanthanumReply(result, args.Subscription, GetNetEntity(args.Actor)), args.Actor);
        Refresh();
    }

    private void OnSupplyWork(Entity<SWGlobalWarMarketConsoleComponent> ent, ref SWGlobalWarSupplyWorkRequest args)
    {
        if (args.Command == null || !ValidSubscription(ent.Owner, args.Actor, args.Subscription) ||
            !TryComp<ActorComponent>(args.Actor, out var player))
            return;
        _markets.TryExecuteSupplyWork(player.PlayerSession, args.Command, out var result);
        _ui.ServerSendUiMessage(ent.Owner, SWGlobalWarMarketConsoleUiKey.Key,
            new SWGlobalWarSupplyWorkReply(result, args.Subscription, GetNetEntity(args.Actor)), args.Actor);
        Refresh();
    }

    private void OnSupply(Entity<SWGlobalWarMarketConsoleComponent> ent, ref SWGlobalWarSupplyRequest args)
    {
        if (args.Command == null || !ValidSubscription(ent.Owner, args.Actor, args.Subscription) ||
            !TryComp<ActorComponent>(args.Actor, out var player))
            return;
        _markets.TryConfigureSupply(player.PlayerSession, args.Command, out var result);
        _ui.ServerSendUiMessage(ent.Owner, SWGlobalWarMarketConsoleUiKey.Key,
            new SWGlobalWarSupplyReply(result, args.Subscription, GetNetEntity(args.Actor)), args.Actor);
        Refresh();
    }

    private void OnReturn(Entity<SWGlobalWarMarketConsoleComponent> ent, ref SWGlobalWarReturnRequest args)
    {
        if (args.Command == null || !ValidSubscription(ent.Owner, args.Actor, args.Subscription) ||
            !TryComp<ActorComponent>(args.Actor, out var player))
            return;
        _markets.TryConfigureReturn(player.PlayerSession, args.Command, out var result);
        _ui.ServerSendUiMessage(ent.Owner, SWGlobalWarMarketConsoleUiKey.Key,
            new SWGlobalWarReturnReply(result, args.Subscription, GetNetEntity(args.Actor)), args.Actor);
        Refresh();
    }

    private void OnGrowth(Entity<SWGlobalWarMarketConsoleComponent> ent, ref SWGlobalWarGrowthRequest args)
    {
        if (args.Command == null || !ValidSubscription(ent.Owner, args.Actor, args.Subscription) ||
            !TryComp<ActorComponent>(args.Actor, out var player))
            return;
        _markets.TryConfigureGrowth(player.PlayerSession, args.Command, out var result);
        _ui.ServerSendUiMessage(ent.Owner, SWGlobalWarMarketConsoleUiKey.Key,
            new SWGlobalWarGrowthReply(result, args.Subscription, GetNetEntity(args.Actor)), args.Actor);
        Refresh();
    }

    private void OnFlowRequest(Entity<SWGlobalWarMarketConsoleComponent> ent, ref SWGlobalWarFlowRequest args)
    {
        if (!ValidSubscription(ent.Owner, args.Actor, args.Subscription) || !TryComp<ActorComponent>(args.Actor, out var player))
            return;
        _markets.TryEditFlow(player.PlayerSession, args.Command, out var result);
        _ui.ServerSendUiMessage(ent.Owner, SWGlobalWarMarketConsoleUiKey.Key,
            new SWGlobalWarFlowReply(result, args.Subscription, GetNetEntity(args.Actor)), args.Actor);
        SendView(ent.Owner, args.Actor);
    }

    private void OnFlowGrant(Entity<SWGlobalWarMarketConsoleComponent> ent, ref SWGlobalWarFlowGrantRequest args)
    {
        if (args.Command == null || !ValidSubscription(ent.Owner, args.Actor, args.Subscription) ||
            !TryComp<ActorComponent>(args.Actor, out var player))
            return;
        var success = _markets.TryGrantFlowWork(player.PlayerSession, args.Command, out _, out var failure);
        _ui.ServerSendUiMessage(ent.Owner, SWGlobalWarMarketConsoleUiKey.Key,
            new SWGlobalWarFlowReply(new(args.Command.Request, success,
                success ? SWGlobalWarEconomyFailure.None : failure),
                args.Subscription, GetNetEntity(args.Actor)), args.Actor);
        Refresh();
    }

    private void OnFlowRevoke(Entity<SWGlobalWarMarketConsoleComponent> ent, ref SWGlobalWarFlowRevokeRequest args)
    {
        if (args.Command == null || !ValidSubscription(ent.Owner, args.Actor, args.Subscription) || !TryComp<ActorComponent>(args.Actor, out var player))
            return;
        var success = _markets.TryRevokeFlowWork(player.PlayerSession, args.Command, out var failure);
        _ui.ServerSendUiMessage(ent.Owner, SWGlobalWarMarketConsoleUiKey.Key,
            new SWGlobalWarFlowReply(new(args.Command.Request, success,
                success ? SWGlobalWarEconomyFailure.None : failure),
                args.Subscription, GetNetEntity(args.Actor)), args.Actor);
        Refresh();
    }

    private void OnClosed(Entity<SWGlobalWarMarketConsoleComponent> ent, ref BoundUIClosedEvent args)
    {
        if (Equals(args.UiKey, SWGlobalWarMarketConsoleUiKey.Key))
        {
            _queries.Remove((ent.Owner, args.Actor));
            _subscriptions.Remove((ent.Owner, args.Actor));
        }
    }

    private void OnSubscribe(Entity<SWGlobalWarMarketConsoleComponent> ent, ref SWGlobalWarMarketOpen args)
    {
        if (args.Subscription == Guid.Empty || !_ui.IsUiOpen(ent.Owner, SWGlobalWarMarketConsoleUiKey.Key, args.Actor))
            return;
        _subscriptions[(ent.Owner, args.Actor)] = args.Subscription;
        _queries.Remove((ent.Owner, args.Actor));
        SendView(ent.Owner, args.Actor);
    }

    private void OnPageRequest(Entity<SWGlobalWarMarketConsoleComponent> ent, ref SWGlobalWarMarketPageRequest args)
    {
        if (!_ui.IsUiOpen(ent.Owner, SWGlobalWarMarketConsoleUiKey.Key, args.Actor) ||
            !_subscriptions.TryGetValue((ent.Owner, args.Actor), out var subscription) || subscription != args.Subscription)
            return;
        var observer = _markets.TryGetMarketObserver(args.Actor, out var assignment);
        _queries[(ent.Owner, args.Actor)] = (_markets.Simulation?.Generation ?? Guid.Empty,
            observer ? assignment.Id : Guid.Empty, observer ? assignment.Revision : 0, args.Query);
        SendView(ent.Owner, args.Actor);
    }

    private void Invalidate()
    {
        _queries.Clear();
        Refresh();
    }

    private void OnOpened(Entity<SWGlobalWarMarketConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (Equals(args.UiKey, SWGlobalWarMarketConsoleUiKey.Key))
        {
            _subscriptions.Remove((ent.Owner, args.Actor));
            _queries.Remove((ent.Owner, args.Actor));
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        Refresh();
    }

    private void Refresh()
    {
        if (_markets.IsClosingCampaignStep)
        {
            _nextUpdate = TimeSpan.Zero;
            return;
        }
        var query = EntityQueryEnumerator<SWGlobalWarMarketConsoleComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out _, out var ui))
        {
            foreach (var actor in _ui.GetActors((uid, ui), SWGlobalWarMarketConsoleUiKey.Key).ToArray())
                SendView(uid, actor);
        }
    }

    private void SendView(EntityUid console, EntityUid actor)
    {
        if (_markets.IsClosingCampaignStep)
            return;
        if (!_subscriptions.TryGetValue((console, actor), out var subscription))
            return;
        SWGlobalWarFreightQuery? query = null;
        if (_queries.TryGetValue((console, actor), out var saved))
        {
            var observer = _markets.TryGetMarketObserver(actor, out var assignment);
            if (saved.Generation == (_markets.Simulation?.Generation ?? Guid.Empty) &&
                saved.Assignment == (observer ? assignment.Id : Guid.Empty) &&
                saved.Revision == (observer ? assignment.Revision : 0))
                query = saved.Query;
            else
                _queries.Remove((console, actor));
        }
        _ui.ServerSendUiMessage(console, SWGlobalWarMarketConsoleUiKey.Key,
            new SWGlobalWarMarketSnapshot(GetState(console, actor, query),
                GetNetEntity(actor), ++_snapshotSequence, subscription), actor);
    }

    /// <summary>Publishes inventories, current shipment legs and shared link occupancy from the active simulation.</summary>
    /// <param name="console">Console location is used only to mark its planet on the map.</param>
    public SWGlobalWarMarketConsoleState GetState(EntityUid console, EntityUid? viewer = null,
        SWGlobalWarFreightQuery? requestedQuery = null)
    {
        var planets = new List<SWGlobalWarPlanetMarketView>();
        var links = new List<SWGlobalWarMarketConnectionView>();
        var shipments = new List<SWGlobalWarShipmentView>();
        if (!_globalWarGamerule.TryGetGlobalWarGamerule(out var rule) || _markets.Simulation is not { } simulation)
            return new SWGlobalWarMarketConsoleState(false, TimeSpan.Zero, planets, links, shipments, null);

        var legacy = simulation.Generation == null;
        var hasObserver = _markets.TryGetMarketObserver(viewer, out var observer);
        foreach (var id in rule.Comp.Planets)
        {
            var availability = _markets.GetMarketAvailability(id, viewer);
            var visible = availability == SWGlobalWarMarketAvailability.Ready;
            var market = simulation.Markets[id.Id];
            planets.Add(new SWGlobalWarPlanetMarketView(id, _prototypes.Index(id).Name, _markets.GetMarketView(id, viewer),
                availability, visible ? (double) market.Population : null,
                visible ? market.HubCapacity : null, visible ? market.HubCapacity : null, market.Owner));
        }

        var occupied = new Dictionary<(string A, string B), double>();
        var query = requestedQuery ?? new();
        var filtered = new List<SWGlobalWarShipment>();
        var totals = new Dictionary<ProtoId<SWGlobalWarCommodityPrototype>, SWGlobalWarFreightTotal>();
        foreach (var shipment in simulation.Shipments)
        {
            if (!legacy && (!hasObserver || !SWGlobalWarMarketSystem.HasEconomicOverview(observer) ||
                            (shipment.SupplyManifest?.Owner ?? shipment.Batch?.Owner) != observer.Faction &&
                            shipment.DeliveryParticipant != observer.Faction))
                continue;
            var quantity = shipment.Quantity / (double) simulation.UnitsPerItem;
            if ((query.Commodity == null || query.Commodity.Value.Id == shipment.Commodity) &&
                (query.Planet == null || shipment.Route.Contains(query.Planet.Value.Id)) &&
                (query.From == null || query.To == null || Lane(query.From.Value.Id, query.To.Value.Id) ==
                    Lane(shipment.Route[shipment.Leg], shipment.Route[Math.Min(shipment.Leg + 1, shipment.Route.Length - 1)])))
            {
                filtered.Add(shipment);
                ProtoId<SWGlobalWarCommodityPrototype> commodity = shipment.Commodity;
                var total = totals.GetValueOrDefault(commodity) ?? new(commodity, 0, 0, 0);
                totals[commodity] = total with
                {
                    Moving = total.Moving + (shipment.ArrivesAt != null ? quantity : 0),
                    Waiting = total.Waiting + (shipment.ArrivesAt == null ? quantity : 0),
                    Count = total.Count + 1,
                };
            }
            if (shipment.ArrivesAt == null)
                continue;

            var lane = Lane(shipment.Route[shipment.Leg], shipment.Route[shipment.Leg + 1]);
            var volume = quantity * _prototypes.Index<SWGlobalWarCommodityPrototype>(shipment.Commodity).CargoVolume;
            occupied[lane] = occupied.GetValueOrDefault(lane) + volume;
        }

        query = query with { Page = Math.Clamp(query.Page, 0,
            Math.Max(0, (filtered.Count - 1) / SWGlobalWarMarketConsoleState.ShipmentPageSize)) };
        foreach (var shipment in filtered.OrderBy(s => s.ArrivesAt != null).ThenBy(s => s.DepartedAt).ThenBy(s => s.Id)
                     .Skip(query.Page * SWGlobalWarMarketConsoleState.ShipmentPageSize)
                     .Take(SWGlobalWarMarketConsoleState.ShipmentPageSize))
        {
            var manifest = shipment.SupplyManifest;
            var proof = manifest?.Capture ?? shipment.Batch?.Capture;
            shipments.Add(new SWGlobalWarShipmentView(shipment.Commodity, shipment.Quantity / (double) simulation.UnitsPerItem,
                shipment.Route.Select(id => new ProtoId<SWGlobalWarPlanetPrototype>(id)).ToList(),
                shipment.Leg, shipment.DepartedAt, shipment.ArrivesAt, shipment.Id, shipment.HoldReason,
                shipment.ReturnOperation != null, manifest?.Origin ?? shipment.Batch?.Origin,
                manifest?.Owner ?? shipment.Batch?.Owner, proof != null,
                manifest?.Id ?? shipment.Batch?.Id ?? Guid.Empty, shipment.DepartureReceipt, shipment.ReturnOperation,
                proof is { } capture ? new(capture.CaptureOperation, capture.TransferOperation,
                    capture.Node, capture.Batch, manifest != null ? capture.Quantity :
                        capture.Quantity / (double) simulation.UnitsPerItem,
                    capture.PreviousOwner, capture.NewOwner) : null, _markets.SupplyTransportView(shipment)));
        }

        foreach (var link in simulation.Connections.Values)
            links.Add(new SWGlobalWarMarketConnectionView(link.From, link.To, link.TravelTime, link.Capacity,
                occupied.GetValueOrDefault(Lane(link.From, link.To)), legacy));

        ProtoId<SWGlobalWarPlanetPrototype>? current = null;
        if (Transform(console).MapUid is { } map && TryComp<SWGlobalWarPlanetMapComponent>(map, out var mapLink)
            && TryComp<SWGlobalWarPlanetComponent>(mapLink.Planet, out var planet) && planet.GlobalWarGamerule == rule.Owner)
            current = planet.Prototype;
        return new SWGlobalWarMarketConsoleState(true, simulation.Time, planets, links, shipments, current,
            simulation.Generation ?? Guid.Empty, hasObserver ? observer.Id : Guid.Empty,
            hasObserver ? observer.Revision : 0, !legacy, query, filtered.Count,
            totals.Values.OrderBy(t => t.Commodity.Id, StringComparer.Ordinal).ToList(),
            _markets.GetFlowViews(viewer), hasObserver ? _markets.GetFlowResults(observer) : [],
            hasObserver && _markets.CanManageFlowWork(observer), hasObserver && observer.Role == SWGlobalWarRole.Head,
            hasObserver ? _markets.GetFlowGrants(observer) : [], hasObserver ? _markets.GetFlowWorkers(observer) : [],
            hasObserver ? _markets.GetNeutralOffers(observer) : [], hasObserver ? _markets.GetGrowthViews(observer) : [],
            hasObserver ? _markets.GetGrowthGrants(observer) : [], hasObserver ? _markets.GetGrowthResults(observer) : [],
            hasObserver && _markets.RequiresLogisticsHeadDecision(observer),
            hasObserver ? _markets.GetReturnCandidates(observer, shipments.Select(s => s.Id)) : [],
            hasObserver ? _markets.GetReturnGrants(observer) : [], hasObserver ? _markets.GetReturnResults(observer) : [],
            hasObserver ? _markets.GetLanthanumView(observer) : null, hasObserver ? _markets.GetLanthanumResults(observer) : [],
            hasObserver ? _markets.GetSupplyView(observer, query.SupplyPage, query.SupplyKind) : null,
            hasObserver ? _markets.GetSupplyResults(observer) : [],
            hasObserver ? _markets.GetSupplyWorkBoard(observer, query.SupplyWorkPage) : null,
            hasObserver ? _markets.GetSupplyWorkResults(observer) : []);
    }

    private static (string A, string B) Lane(string from, string to) =>
        string.CompareOrdinal(from, to) <= 0 ? (from, to) : (to, from);
}
