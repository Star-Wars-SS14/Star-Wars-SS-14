using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar.Mapping;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.Destructible;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Combat;
using Content.Shared._SW.GlobalWar.Mapping;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.ActionBlocker;
using Content.Shared.Interaction;
using Content.Shared.Power;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._SW.GlobalWar.Combat;

/// <summary>Finite funded hull orders. A reservation becomes an expense only with an actual docked ship.</summary>
public sealed class SWGlobalWarFleetSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarCampaignSystem _campaign = default!;
    [Dependency] private readonly SWGlobalWarCampaignStepSystem _steps = default!;
    [Dependency] private readonly SWGlobalWarAssignmentsSystem _assignments = default!;
    [Dependency] private readonly SWGlobalWarLedgerSystem _ledger = default!;
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarMapAnchorSystem _anchors = default!;
    [Dependency] private readonly SWGlobalWarShipOwnershipSystem _ownership = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly DockingSystem _docking = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SWGlobalWarGunnerySystem _gunnery = default!;
    [Dependency] private readonly SWGlobalWarHullPreparationSystem _preparation = default!;

    private readonly Dictionary<Guid, Order> _orders = new();
    private readonly Dictionary<(NetUserId, Guid), (EntityUid, SWGlobalWarShipClass, Guid)> _receipts = new();
    private readonly Dictionary<(EntityUid Console, EntityUid Actor), Guid> _subscriptions = new();
    private Guid _generation;
    private EntityUid? _staging;
    private TimeSpan _nextUpdate;
    private int _preparedIndex;
    private readonly HashSet<SWGlobalWarFaction> _startingGranted = new();
    private readonly HashSet<EntityUid> _forceWorldsGranted = new();
    private readonly HashSet<EntityUid> _jointsPublishedNow = new();

    private static readonly (SWGlobalWarShipClass Class, string Dock, string Clearance)[] StartingFleet =
    [
        (SWGlobalWarShipClass.Transport, "dockTransport", "clearTransport"),
        (SWGlobalWarShipClass.Transport, "dockAssault", "clearAssault"),
        (SWGlobalWarShipClass.Fighter, "dockShipyard", "clearShipyard"),
    ];

    // Existing physical hulls, with faction-specific layouts and weapons. Unavailable classes are never sold.
    private static readonly Dictionary<SWGlobalWarShipClass, Hull> Hulls = new()
    {
        [SWGlobalWarShipClass.Transport] = new(10000, 120, "Vessels/Republic/transship.yml", "Vessels/Empire/Imperialmilitarytransport.yml"),
        [SWGlobalWarShipClass.Fighter] = new(40000, 180, "Vessels/Republic/FlashfireRepublicFighter.yml", "Vessels/Empire/Mk6.yml"),
        [SWGlobalWarShipClass.Bomber] = new(70000, 240, "Vessels/Republic/specshipWarcarrier.yml", "Vessels/Empire/specshipGSS5CDustmaker.yml"),
        [SWGlobalWarShipClass.Frigate] = new(140000, 300, "Vessels/Republic/resistanceCruisersmall.yml", "Vessels/Empire/ISI.yml"),
        [SWGlobalWarShipClass.Cruiser] = new(260000, 480, "Mega/Ships/republic_cruiser.yml", "Mega/Ships/empire_cruiser.yml"),
        [SWGlobalWarShipClass.Battleship] = new(500000, 600, "Mega/Ships/republic_battleship.yml", "Mega/Ships/empire_battleship.yml"),
    };

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(SWGlobalWarCampaignStepSystem));
        SubscribeLocalEvent<SWGlobalWarCampaignStartedEvent>((ref SWGlobalWarCampaignStartedEvent ev) => Reset(ev.Generation));
        SubscribeLocalEvent<SWGlobalWarCampaignStoppedEvent>((ref SWGlobalWarCampaignStoppedEvent _) => Reset(Guid.Empty));
        SubscribeLocalEvent<SWGlobalWarShipyardComponent, BoundUIClosedEvent>((Entity<SWGlobalWarShipyardComponent> ent,
            ref BoundUIClosedEvent ev) => _subscriptions.Remove((ent.Owner, ev.Actor)));
        Subs.BuiEvents<SWGlobalWarShipyardComponent>(SWGlobalWarFleetUiKey.Key, subs =>
        {
            subs.Event<SWGlobalWarFleetOpen>(OnOpen);
            subs.Event<SWGlobalWarFleetRequest>(OnRequest);
        });
        SubscribeLocalEvent<SWGlobalWarTravelConsoleComponent, GetVerbsEvent<ActivationVerb>>(OnNavigationVerb);
    }

    private void OnNavigationVerb(EntityUid uid, SWGlobalWarTravelConsoleComponent component,
        GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || !_campaign.TryGetCampaign(out _)) return;
        var actor = args.User;
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("sw-global-war-fleet-navigation"),
            Act = () =>
            {
                if (!TerminatingOrDeleted(uid) && _interaction.InRangeUnobstructed(actor, uid) &&
                    _blocker.CanInteract(actor, uid) && this.IsPowered(uid, EntityManager))
                    _ui.TryOpenUi(uid, SWGlobalWarTravelConsoleUiKey.Key, actor);
            },
        });
    }

    private bool PrepareNavigation(EntityUid ship)
    {
        if (!_preparation.Prepare(ship)) return false;
        _gunnery.PrepareShip(ship);
        var found = false;
        var query = EntityQueryEnumerator<ShuttleConsoleComponent>();
        while (query.MoveNext(out var console, out _))
        {
            if (Transform(console).GridUid != ship) continue;
            EnsureComp<SWGlobalWarTravelConsoleComponent>(console);
            _ui.SetUi(console, SWGlobalWarTravelConsoleUiKey.Key,
                new InterfaceData("SWGlobalWarTravelConsoleBoundUserInterface"));
            found = true;
        }
        return found;
    }

    private void Reset(Guid generation)
    {
        foreach (var order in _orders.Values)
            if (order.Prepared is { } ship && !TerminatingOrDeleted(ship))
                QueueDel(ship);
        _orders.Clear();
        _receipts.Clear();
        _subscriptions.Clear();
        if (_staging is { } map && !TerminatingOrDeleted(map))
            QueueDel(map);
        _staging = null;
        _generation = generation;
        _nextUpdate = TimeSpan.Zero;
        _preparedIndex = 0;
        _startingGranted.Clear();
        _forceWorldsGranted.Clear();
    }

    private bool Operational(EntityUid console, SWGlobalWarFaction faction)
    {
        return !TerminatingOrDeleted(console) && HasComp<SWGlobalWarShipyardComponent>(console) &&
               Transform(console).Anchored && this.IsPowered(console, EntityManager) &&
               !(TryComp<DestructibleComponent>(console, out var damage) && damage.IsBroken) &&
               _planets.TryGetPhysicalLocation(console, out var planet, out var location) &&
               location == SWGlobalWarPlanetLocation.Orbit && Comp<SWGlobalWarPlanetComponent>(planet).OrbitOwner == faction;
    }

    private bool Authorized(SWGlobalWarAssignment actor, bool headDecision)
    {
        return (_assignments.GetAuthority(actor) & SWGlobalWarAuthority.WarBudget) != 0 ||
               actor.Role == SWGlobalWarRole.Head && headDecision;
    }

    private bool ClassDisabled(SWGlobalWarShipClass shipClass)
    {
        return _campaign.IsShipClassDisabled(shipClass);
    }

    private static bool TierAllows(SWGlobalWarShipyardTier tier, SWGlobalWarShipClass shipClass)
    {
        return shipClass == SWGlobalWarShipClass.Transport || tier != SWGlobalWarShipyardTier.Transport &&
               (shipClass < SWGlobalWarShipClass.Cruiser || tier == SWGlobalWarShipyardTier.Heavy);
    }

    private bool HasSlot(SWGlobalWarFaction faction, SWGlobalWarShipClass shipClass, Guid exclude = default)
    {
        var transports = 0;
        var armed = 0;
        var heavy = 0;
        void Count(SWGlobalWarShipClass value)
        {
            if (value == SWGlobalWarShipClass.Transport) transports++;
            else armed++;
            if (value >= SWGlobalWarShipClass.Cruiser) heavy++;
        }
        var query = EntityQueryEnumerator<SWGlobalWarFleetShipComponent>();
        while (query.MoveNext(out var ship, out var fleet))
            if (_ownership.TryGetOwner(ship, _generation, out var owner, out _) && owner == faction)
                Count(fleet.Class);
        foreach (var order in _orders.Values)
            if (order.Id != exclude && order.Faction == faction)
                Count(order.Class);
        return shipClass == SWGlobalWarShipClass.Transport ? transports < 8 : armed < 8 &&
            (shipClass < SWGlobalWarShipClass.Cruiser || heavy < 2);
    }

    /// <summary>Queues a free hull at a real console, such as a reward. It still waits for a free port and clearance.</summary>
    public bool TryGrantHull(EntityUid console, SWGlobalWarFaction faction, SWGlobalWarShipClass shipClass)
    {
        if (!Hulls.TryGetValue(shipClass, out var hull) || !hull.Enabled || !Operational(console, faction) ||
            !TierAllows(Comp<SWGlobalWarShipyardComponent>(console).Tier, shipClass) || !HasSlot(faction, shipClass))
            return false;
        var id = Guid.NewGuid();
        _orders.Add(id, new Order(id, console, faction, shipClass, Guid.Empty, 0, _timing.CurTime) { StartingGrant = true });
        return true;
    }

    /// <summary>Authoritative BUI endpoint; successful retries retain the original order without a second reserve.</summary>
    public bool TryExecute(ICommonSession session, EntityUid console, Guid generation, long revision,
        Guid request, SWGlobalWarShipClass shipClass, Guid cancel = default, bool headDecision = false)
    {
        if (request == Guid.Empty || generation != _generation || _steps.IsClosing ||
            !_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Phase != SWGlobalWarCampaignPhase.Running ||
            !_assignments.TryGetActor(session, generation, revision, out var actor) || !Authorized(actor, headDecision) ||
            !Operational(console, actor.Faction) || !_blocker.CanInteract(actor.Body, console) ||
            !_interaction.InRangeUnobstructed(actor.Body, console, range: 2f))
            return false;
        if (_receipts.TryGetValue((session.UserId, request), out var receipt))
            return receipt == (console, shipClass, cancel);
        if (cancel != Guid.Empty)
        {
            if (!_orders.TryGetValue(cancel, out var order) || order.StartingGrant ||
                order.Faction != actor.Faction || order.Console != console ||
                !Cancel(order))
                return false;
        }
        else
        {
            if (!Hulls.TryGetValue(shipClass, out var hull) || !hull.Enabled || ClassDisabled(shipClass) ||
                !TierAllows(Comp<SWGlobalWarShipyardComponent>(console).Tier, shipClass) ||
                _orders.Values.Count(o => o.Console == console) >= 3 || !HasSlot(actor.Faction, shipClass) ||
                !_ledger.TryGetLedger(out var ledger))
                return false;
            using var notifications = _ledger.DeferNotifications();
            if (!_ledger.TryExecuteSystem(new SWGlobalWarLedgerCommand(generation, Guid.NewGuid(), ledger.Comp.Revision,
                    SWGlobalWarLedgerAction.Reserve, actor.Faction, SWGlobalWarAccount.War, hull.Price,
                    Purpose: $"Fleet {shipClass}"), out var reservation, session.UserId))
                return false;
            var id = Guid.NewGuid();
            _orders.Add(id, new Order(id, console, actor.Faction, shipClass, reservation, hull.Price,
                _timing.CurTime + TimeSpan.FromSeconds(hull.Seconds)));
        }
        _receipts.Add((session.UserId, request), (console, shipClass, cancel));
        return true;
    }

    private bool Cancel(Order order)
    {
        using var notifications = _ledger.DeferNotifications();
        if (_ledger.TryGetReservation(order.Reservation, out _) &&
            (!_ledger.TryGetLedger(out var ledger) || !_ledger.TryExecuteSystem(new SWGlobalWarLedgerCommand(
                _generation, Guid.NewGuid(), ledger.Comp.Revision, SWGlobalWarLedgerAction.Release,
                order.Faction, SWGlobalWarAccount.War, Reservation: order.Reservation), out _)))
            return false;
        if (order.Prepared is { } ship && !TerminatingOrDeleted(ship)) QueueDel(ship);
        _orders.Remove(order.Id);
        return true;
    }

    public override void Update(float frameTime)
    {
        if (_generation == Guid.Empty || _timing.CurTime < _nextUpdate) return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        if (!_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Phase != SWGlobalWarCampaignPhase.Running) return;
        if (HasComp<SWGlobalWarStartingFleetComponent>(campaign.Owner)) GrantStartingFleet();
        _jointsPublishedNow.Clear();
        foreach (var order in _orders.Values.ToArray())
        {
            if (TerminatingOrDeleted(order.Console) ||
                !order.StartingGrant && !_ledger.TryGetReservation(order.Reservation, out _) ||
                !_planets.TryGetPhysicalLocation(order.Console, out var planet, out _) ||
                Comp<SWGlobalWarPlanetComponent>(planet).OrbitOwner != order.Faction)
            {
                Cancel(order);
                continue;
            }
            if (!Operational(order.Console, order.Faction)) { order.Status = "unpowered"; continue; }
            if (_timing.CurTime < order.ReadyAt) { order.Status = "preparing"; continue; }
            order.Status = "waiting";
            if (!HasSlot(order.Faction, order.Class, order.Id)) continue;
            TryDeliver(order);
        }
        foreach (var ((console, actor), _) in _subscriptions.ToArray()) SendView(console, actor);
    }

    private void GrantStartingFleet()
    {
        var terminals = EntityQueryEnumerator<SWGlobalWarShipyardComponent>();
        while (terminals.MoveNext(out var console, out var terminal))
        {
            if (!_planets.TryGetPhysicalLocation(console, out var planet, out var location) ||
                location != SWGlobalWarPlanetLocation.Orbit) continue;
            var world = Comp<SWGlobalWarPlanetComponent>(planet);
            var definition = _prototypes.Index(world.Prototype);
            var faction = definition.InitialOwner;
            if (world.OrbitOwner != faction || faction is not (SWGlobalWarFaction.Republic or SWGlobalWarFaction.Empire))
                continue;
            // A Force user starts away from the capitals and every other fleet, so this world keeps one way to the front.
            if (definition.Kind == SWGlobalWarSectorKind.Force)
            {
                if (terminal.Tier != SWGlobalWarShipyardTier.Transport || !_forceWorldsGranted.Add(planet)) continue;
                var transport = Guid.NewGuid();
                _orders.Add(transport, new Order(transport, console, faction, SWGlobalWarShipClass.Transport, Guid.Empty, 0,
                    _timing.CurTime + TimeSpan.FromSeconds(1))
                { StartingGrant = true, Dock = "dockTransport", Clearance = "clearTransport" });
                continue;
            }
            if (terminal.Tier != SWGlobalWarShipyardTier.SmallMedium || definition.Kind != SWGlobalWarSectorKind.Capital ||
                !_startingGranted.Add(faction)) continue;
            // Each granted hull has its own real port, so none of them waits for another pilot to leave.
            foreach (var (shipClass, dock, clearance) in StartingFleet)
            {
                var id = Guid.NewGuid();
                _orders.Add(id, new Order(id, console, faction, shipClass, Guid.Empty, 0, _timing.CurTime + TimeSpan.FromSeconds(1))
                { StartingGrant = true, Dock = dock, Clearance = clearance });
            }
        }
    }

    private bool TryResolveDelivery(EntityUid console, string? dockAnchor, string? clearanceAnchor, out EntityUid dock,
        out DockingComponent port, out EntityUid grid, out Box2 area)
    {
        dock = grid = default;
        port = default!;
        area = default;
        if (!_planets.TryGetPhysicalLocation(console, out var planet, out var location))
            return false;
        SWGlobalWarAnchorRef Local(string anchor) => new()
        {
            Sector = Comp<SWGlobalWarPlanetComponent>(planet).Prototype, Location = location, AnchorId = anchor,
        };
        SWGlobalWarAnchorRef dockRef;
        SWGlobalWarAnchorRef clearanceRef;
        if (dockAnchor != null && clearanceAnchor != null)
        {
            dockRef = Local(dockAnchor);
            clearanceRef = Local(clearanceAnchor);
        }
        else if (!_anchors.TryGetLink(console, "dock", out dockRef!) || !_anchors.TryGetLink(console, "clearance", out clearanceRef!))
            return false;
        if (!_anchors.TryResolveAnchor(dockRef, out var target) || !TryComp<DockingComponent>(target, out var targetPort) ||
            Transform(target.Value).GridUid is not { } targetGrid ||
            !_anchors.TryResolveAnchor(clearanceRef, out var clearance) ||
            !TryComp<SWGlobalWarMapAnchorComponent>(clearance, out var bounds) ||
            // A clearance marker off the station tiles loads parented to the map; its cells stay in the station's frame.
            Transform(clearance.Value).MapUid != Transform(target.Value).MapUid ||
            Transform(target.Value).MapUid != Transform(console).MapUid)
            return false;
        dock = target.Value;
        port = targetPort;
        grid = targetGrid;
        // A hull may wrap around the station's own edge: docking already rejected any overlap with its tiles.
        area = new Box2(bounds.Min.X, bounds.Min.Y, bounds.Max.X + 1, bounds.Max.Y + 1)
            .Union(Comp<MapGridComponent>(targetGrid).LocalAABB);
        return true;
    }

    /// <summary>Docking pose of this hull at the console's port, only if the docked hull stays inside the clearance.</summary>
    public DockingConfig? FindDelivery(EntityUid ship, EntityUid console, string? dockAnchor = null, string? clearanceAnchor = null)
    {
        if (!TryResolveDelivery(console, dockAnchor, clearanceAnchor, out var target, out var port, out var grid, out var area))
            return null;
        foreach (var shipPort in _docking.GetDocks(ship))
        {
            var config = _docking.GetDockingConfig(ship, grid, shipPort.Owner, shipPort.Comp, target, port);
            if (config != null && area.Contains(config.Area.BottomLeft) && area.Contains(config.Area.TopRight))
                return config;
        }
        return null;
    }

    /// <summary>Map file of the side's hull for this class; false for a class that is not sold.</summary>
    public bool TryGetHullMap(SWGlobalWarShipClass shipClass, SWGlobalWarFaction faction, out string? path)
    {
        path = null;
        if (!Hulls.TryGetValue(shipClass, out var hull) || !hull.Enabled)
            return false;
        path = $"/Maps/SW/{(faction == SWGlobalWarFaction.Republic ? hull.Republic : hull.Empire)}";
        return true;
    }

    private void TryDeliver(Order order)
    {
        if (!TryResolveDelivery(order.Console, order.Dock, order.Clearance, out _, out var port, out var grid, out _) ||
            port.Docked)
            return;
        // A hull entering the client's view initialises with its joint first. If the station's joint component arrives
        // in that same game state, the client adds the same joint twice and logs an error (ARCH-F-DOCK-002).
        // The engine removes an emptied component after any undock, so check on every delivery, one update ahead.
        if (!HasComp<JointComponent>(grid))
        {
            EnsureComp<JointComponent>(grid);
            _jointsPublishedNow.Add(grid);
        }
        if (_jointsPublishedNow.Contains(grid))
            return;
        if (order.Prepared == null)
        {
            _staging ??= _maps.CreateMap();
            if (!TryGetHullMap(order.Class, order.Faction, out var path) ||
                !_loader.TryLoadGrid(Transform(_staging.Value).MapID, new ResPath(path!),
                    out var loaded, offset: new Vector2(++_preparedIndex * 500, 0)) || loaded == null)
            { order.Status = "unavailable"; return; }
            order.Prepared = loaded;
            if (!HasComp<ShuttleComponent>(loaded) || !PrepareNavigation(loaded.Value)) { Cancel(order); return; }
        }
        var ship = order.Prepared.Value;
        if (FindDelivery(ship, order.Console, order.Dock, order.Clearance) is not { } config)
            return;
        using var notifications = _ledger.DeferNotifications();
        var arrival = _transform.ToMapCoordinates(config.Coordinates);
        var map = Transform(grid).MapUid!.Value;
        var angle = config.Angle + _transform.GetWorldRotation(grid);
        _transform.SetCoordinates(ship, Transform(ship), new EntityCoordinates(map, arrival.Position), rotation: angle);
        if (!_ownership.RegisterShip(ship, _generation, order.Faction) ||
            !order.StartingGrant && (!_ledger.TryGetLedger(out var ledger) ||
            !_ledger.TryExecuteSystem(new SWGlobalWarLedgerCommand(_generation, Guid.NewGuid(), ledger.Comp.Revision,
                SWGlobalWarLedgerAction.Commit, order.Faction, SWGlobalWarAccount.War, order.Price,
                Reservation: order.Reservation), out _)))
        {
            Cancel(order);
            return;
        }
        EnsureComp<SWGlobalWarFleetShipComponent>(ship).Class = order.Class;
        foreach (var pair in config.Docks)
            _docking.Dock((pair.DockAUid, pair.DockA), (pair.DockBUid, pair.DockB));
        _orders.Remove(order.Id);
    }

    private void OnOpen(Entity<SWGlobalWarShipyardComponent> ent, ref SWGlobalWarFleetOpen args)
    {
        if (args.Subscription == Guid.Empty || !_ui.IsUiOpen(ent.Owner, SWGlobalWarFleetUiKey.Key, args.Actor)) return;
        _subscriptions[(ent.Owner, args.Actor)] = args.Subscription;
        SendView(ent.Owner, args.Actor);
    }

    private void OnRequest(Entity<SWGlobalWarShipyardComponent> ent, ref SWGlobalWarFleetRequest args)
    {
        if (!_subscriptions.TryGetValue((ent.Owner, args.Actor), out var subscription) || subscription != args.Subscription ||
            !TryComp<ActorComponent>(args.Actor, out var actor)) return;
        TryExecute(actor.PlayerSession, ent.Owner, args.Generation, args.Revision, args.Request, args.Class,
            args.Cancel, args.HeadDecision);
        SendView(ent.Owner, args.Actor);
    }

    private void SendView(EntityUid console, EntityUid body)
    {
        if (!_subscriptions.TryGetValue((console, body), out var subscription) ||
            !_ui.IsUiOpen(console, SWGlobalWarFleetUiKey.Key, body) || !TryComp<ActorComponent>(body, out var actor)) return;
        _ui.ServerSendUiMessage(console, SWGlobalWarFleetUiKey.Key,
            new SWGlobalWarFleetSnapshot(GetView(actor.PlayerSession, console), subscription, GetNetEntity(body)), body);
    }

    /// <summary>Current authenticated terminal view, shared by the BUI and server integration checks.</summary>
    public SWGlobalWarFleetView GetView(ICommonSession session, EntityUid console)
    {
        if (TerminatingOrDeleted(console) || !HasComp<SWGlobalWarShipyardComponent>(console) ||
            !_assignments.TryGetAssignment(session.UserId, out var assignment) ||
            !_assignments.TryGetActor(session, _generation, assignment.Revision, out assignment))
            return new(_generation, 0, 0, false, false, "denied", [], []);
        var authorized = Authorized(assignment, true) && Operational(console, assignment.Faction);
        var offers = Hulls.Where(pair => !ClassDisabled(pair.Key)).Select(pair => new SWGlobalWarFleetOffer(pair.Key, pair.Value.Price, pair.Value.Seconds,
            authorized && pair.Value.Enabled && TierAllows(Comp<SWGlobalWarShipyardComponent>(console).Tier, pair.Key) &&
            HasSlot(assignment.Faction, pair.Key))).ToArray();
        var orders = _orders.Values.Where(o => o.Console == console && o.Faction == assignment.Faction)
            .Select(o => new SWGlobalWarFleetOrderView(o.Id, o.Class,
                Math.Max(0, (int) Math.Ceiling((o.ReadyAt - _timing.CurTime).TotalSeconds)), o.Status, !o.StartingGrant)).ToArray();
        var available = authorized ? _ledger.GetBalance(assignment.Faction, SWGlobalWarAccount.War).Available : 0;
        return new(_generation, assignment.Revision, available, authorized,
            assignment.Role == SWGlobalWarRole.Head, authorized ? "ready" : "denied", offers, orders);
    }

    private sealed record Hull(long Price, int Seconds, string Republic, string Empire, bool Enabled = true);
    private sealed class Order(Guid id, EntityUid console, SWGlobalWarFaction faction, SWGlobalWarShipClass shipClass,
        Guid reservation, long price, TimeSpan readyAt)
    {
        public readonly Guid Id = id;
        public readonly EntityUid Console = console;
        public readonly SWGlobalWarFaction Faction = faction;
        public readonly SWGlobalWarShipClass Class = shipClass;
        public readonly Guid Reservation = reservation;
        public readonly long Price = price;
        public readonly TimeSpan ReadyAt = readyAt;
        public EntityUid? Prepared;
        public bool StartingGrant;
        public string? Dock;
        public string? Clearance;
        public string Status = "preparing";
    }
}
