using System.Numerics;
using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Combat;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Access;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Interaction;
using Content.Shared.Mind.Components;
using Content.Shared.Ghost;
using Content.Shared.Power;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Shuttles.UI.MapObjects;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar;

// Handles interplanetary console requests, safe arrival points, and timed FTL travel.
public sealed partial class SWGlobalWarTravelSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarMarketSystem _markets = default!;
    [Dependency] private readonly SWGlobalWarGameruleSystem _globalWarGamerule = default!;
    [Dependency] private readonly ShuttleSystem _shuttles = default!;
    [Dependency] private readonly DockingSystem _docking = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SWGlobalWarAccessPolicySystem _policy = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SWGlobalWarShipOwnershipSystem _shipOwnership = default!;
    [Dependency] private readonly SWGlobalWarShipControlSystem _controls = default!;

    // Extra clearance in meters between the station's bounding circle and the ship's FTL safety circle.
    private TimeSpan _nextInterfaceUpdate;
    private long _snapshotSequence;
    private readonly Dictionary<(EntityUid Console, EntityUid Actor), Guid> _viewSubscriptions = new();

    private const float MinimumArrivalDistance = 8f;

    // Number of search rings, directions per ring, and radial spacing in meters.
    private const int ArrivalSearchRingCount = 4;
    private const int ArrivalSearchDirectionCount = 16;
    private const float ArrivalSearchRingSpacing = 64f;
    private static readonly TimeSpan MaximumDestinationWait = TimeSpan.FromSeconds(30);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarTravelConsoleComponent, BoundUIOpenedEvent>(OnOpen);
        SubscribeLocalEvent<SWGlobalWarTravelConsoleComponent, BoundUIClosedEvent>(OnClosed);
        Subs.BuiEvents<SWGlobalWarTravelConsoleComponent>(SWGlobalWarTravelConsoleUiKey.Key, subs =>
        {
            subs.Event<SWGlobalWarTravelRequest>(OnRequest);
            subs.Event<SWGlobalWarTravelOpen>(OnSubscribe);
        });
        SubscribeLocalEvent<SWGlobalWarTravelComponent, FTLBeforeArrivalEvent>(OnBeforeArrival);
        SubscribeLocalEvent<SWGlobalWarTravelComponent, FTLCompletedEvent>(OnCompleted);
        SubscribeLocalEvent<SWGlobalWarTravelComponent, ComponentShutdown>(OnTravelShutdown);
        SubscribeLocalEvent<FTLComponent, ComponentRemove>(OnFtlRemoved);
        SubscribeLocalEvent<SWGlobalWarFtlAttemptEvent>(OnFtlAttempt);
        SubscribeLocalEvent<SWGlobalWarAssignmentsChangedEvent>((ref SWGlobalWarAssignmentsChangedEvent _) => RefreshInterfaces());
        SubscribeLocalEvent<SWGlobalWarOwnershipChangedEvent>((ref SWGlobalWarOwnershipChangedEvent _) => RefreshInterfaces());
        SubscribeLocalEvent<SWGlobalWarCampaignStoppedEvent>((ref SWGlobalWarCampaignStoppedEvent _) => RefreshInterfaces());
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        // An ordinary FTL call can fail after its permission check. Do not retain its reservation.
        var pending = AllEntityQuery<SWGlobalWarTravelComponent>();
        while (pending.MoveNext(out var ship, out _))
        {
            if (!HasComp<FTLComponent>(ship))
                RemCompDeferred<SWGlobalWarTravelComponent>(ship);
        }
        if (_timing.CurTime < _nextInterfaceUpdate)
            return;
        _nextInterfaceUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        var query = EntityQueryEnumerator<SWGlobalWarTravelConsoleComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out _, out var ui))
        {
            if (_ui.IsUiOpen((uid, ui), SWGlobalWarTravelConsoleUiKey.Key))
                UpdateInterface(uid);
        }
    }

    private void OnOpen(Entity<SWGlobalWarTravelConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (Equals(args.UiKey, SWGlobalWarTravelConsoleUiKey.Key))
            _viewSubscriptions.Remove((ent.Owner, args.Actor));
    }

    private void OnClosed(Entity<SWGlobalWarTravelConsoleComponent> ent, ref BoundUIClosedEvent args)
    {
        if (Equals(args.UiKey, SWGlobalWarTravelConsoleUiKey.Key))
            _viewSubscriptions.Remove((ent.Owner, args.Actor));
    }

    private void OnSubscribe(Entity<SWGlobalWarTravelConsoleComponent> ent, ref SWGlobalWarTravelOpen args)
    {
        if (args.Subscription == Guid.Empty || !_ui.IsUiOpen(ent.Owner, SWGlobalWarTravelConsoleUiKey.Key, args.Actor))
            return;
        _viewSubscriptions[(ent.Owner, args.Actor)] = args.Subscription;
        UpdateInterface(ent.Owner);
    }

    private void OnRequest(Entity<SWGlobalWarTravelConsoleComponent> ent, ref SWGlobalWarTravelRequest args)
    {
        if (args.Actor is not { Valid: true } user ||
            !_ui.IsUiOpen(ent.Owner, SWGlobalWarTravelConsoleUiKey.Key, user))
            return;

        if (!TryStartTravel(ent.Owner, user, args.Destination, out var reason))
            _popup.PopupCursor(reason, user);

        UpdateInterface(ent.Owner);
    }

    /// <summary>
    /// Validates a travel request, reserves a safe arrival point, and starts FTL to the destination orbit.
    /// </summary>
    /// <param name="console">Navigation console used to request travel.</param>
    /// <param name="user">Entity interacting with the console.</param>
    /// <param name="destination">Prototype ID of the target planet.</param>
    /// <param name="reason">Localized rejection reason, or an empty string on success.</param>
    public bool TryStartTravel(EntityUid console, EntityUid user, ProtoId<SWGlobalWarPlanetPrototype> destination,
        out string reason)
    {
        reason = Loc.GetString("sw-global-war-travel-denied");
        if (!HasComp<SWGlobalWarTravelConsoleComponent>(console) || TerminatingOrDeleted(user) ||
            !this.IsPowered(console, EntityManager) || !Transform(console).Anchored ||
            !_blocker.CanInteract(user, console) || !_interaction.InRangeUnobstructed(user, console) ||
            (TryComp<AccessReaderComponent>(console, out var reader) && !_access.IsAllowed(user, console, reader)))
            return false;

        if (!_controls.CanOperate(console, user, out reason) ||
            !TryGetContext(console, out var ship, out var planet, out reason) ||
            !CheckDestination(console, ship, planet, destination, out var targetPlanet, out reason))
            return false;

        var source = Comp<SWGlobalWarPlanetComponent>(planet).Prototype;

        if (!_planets.TryPrepareArrival(destination, SWGlobalWarPlanetLocation.Orbit, ship, out var map))
        {
            reason = Loc.GetString("sw-global-war-travel-unavailable");
            return false;
        }

        // An unopened destination has no station/FTL data until its first load has completed.
        if (!_planets.TryGetOrbitStation(targetPlanet, out _) ||
            !_shuttles.CanFTLTo(ship, Transform(map.Value).MapID, console))
        {
            _planets.ReleaseArrival(map.Value, ship);
            reason = Loc.GetString("sw-global-war-travel-unavailable");
            return false;
        }

        var flight = AddComp<SWGlobalWarTravelComponent>(ship);
        InitializeFlight(ship, flight, planet, targetPlanet, map.Value);
        flight.UseStationArrival = true;
        try
        {
            if (!TryFindArrival(ship, flight, out var coordinates))
            {
                reason = Loc.GetString("sw-global-war-travel-blocked");
                return false;
            }

            var duration = _prototypes.Index(source).TravelTimes.TryGetValue(destination, out var edgeTime)
                ? (float) edgeTime.TotalSeconds
                : _prototypes.Index(destination).HyperspaceTime;
            _shuttles.FTLToCoordinates(ship, Comp<ShuttleComponent>(ship), coordinates, Angle.Zero,
                hyperspaceTime: duration);
            if (!HasComp<FTLComponent>(ship))
            {
                reason = Loc.GetString("sw-global-war-travel-unavailable");
                return false;
            }

            var started = new SWGlobalWarRouteStartedEvent(user);
            RaiseLocalEvent(ref started);

            reason = string.Empty;
            return true;
        }
        finally
        {
            if (!HasComp<FTLComponent>(ship))
                RemComp<SWGlobalWarTravelComponent>(ship);
        }
    }

    /// <summary>
    /// Resolves the source ship and planet and checks their readiness for interplanetary travel.
    /// </summary>
    /// <param name="console">Navigation console on the departing ship.</param>
    /// <param name="ship">Resolved ship grid.</param>
    /// <param name="planet">Resolved source planet controller.</param>
    /// <param name="reason">Localized reason if validation fails.</param>
    private bool TryGetContext(EntityUid console, out EntityUid ship, out EntityUid planet,
        out string reason)
    {
        ship = planet = default;
        reason = Loc.GetString("sw-global-war-travel-orbit-only");
        // Interplanetary travel must start from a ship grid on an orbit map.
        var xform = Transform(console);
        if (xform.GridUid is not { } grid ||
            !TryComp<SWGlobalWarPlanetMapComponent>(xform.MapUid, out var link) ||
            link.Location != SWGlobalWarPlanetLocation.Orbit ||
            !TryComp<SWGlobalWarPlanetComponent>(link.Planet, out var data))
            return false;

        ship = grid;
        planet = link.Planet;
        // Reject maps belonging to a previous GlobalWarGamerule instance.
        reason = Loc.GetString("sw-global-war-travel-unavailable");
        if (!_globalWarGamerule.TryGetGlobalWarGamerule(out var globalWarGamerule) ||
            data.GlobalWarGamerule != globalWarGamerule.Owner)
            return false;

        reason = Loc.GetString("sw-global-war-travel-denied");
        if (!this.IsPowered(console, EntityManager) || !xform.Anchored)
            return false;

        // Only mobile ships may travel; station grids and static bodies are excluded.
        reason = Loc.GetString("sw-global-war-travel-ship-only");
        if (!TryComp<ShuttleComponent>(ship, out var shuttle) || !shuttle.Enabled ||
            !TryComp<PhysicsComponent>(ship, out var body) || body.BodyType != BodyType.Dynamic ||
            _planets.IsPlanetStationGrid(planet, ship))
            return false;

        if (HasComp<SWGlobalWarTravelComponent>(ship))
        {
            reason = Loc.GetString("shuttle-console-in-ftl");
            return false;
        }

        if (!_shuttles.CanFTL(ship, out var ftlReason))
        {
            reason = ftlReason;
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Checks the directed route, destination station, and standard FTL destination restrictions.
    /// </summary>
    /// <param name="console">Console used to check destination access and coordinate disk requirements.</param>
    /// <param name="ship">Departing ship grid.</param>
    /// <param name="planet">Source planet controller.</param>
    /// <param name="destination">Prototype ID of the target planet.</param>
    /// <param name="targetPlanet">Resolved destination planet controller.</param>
    /// <param name="reason">Localized reason if validation fails.</param>
    private bool CheckDestination(EntityUid console, EntityUid ship, EntityUid planet,
        ProtoId<SWGlobalWarPlanetPrototype> destination, out EntityUid targetPlanet, out string reason)
    {
        targetPlanet = default;
        reason = Loc.GetString("sw-global-war-travel-no-route");
        var source = Comp<SWGlobalWarPlanetComponent>(planet).Prototype;
        if (source == destination)
        {
            reason = Loc.GetString("sw-global-war-travel-current");
            return false;
        }

        if (!_planets.CanTravel(source, destination) ||
            !_planets.TryGetPlanet(destination, out var target))
            return false;

        targetPlanet = target.Value;
        if (!TryEvaluateCrew(ship, targetPlanet, false, out reason))
            return false;
        reason = Loc.GetString("sw-global-war-travel-unavailable");
        if (!_planets.TryGetPlanetMap(targetPlanet, SWGlobalWarPlanetLocation.Orbit, out var map))
        {
            // Listing destinations must not eagerly load every map; readiness is checked again on departure.
            return _prototypes.Index(destination).Orbit != null;
        }
        if (!_planets.TryGetOrbitStation(targetPlanet, out _) ||
            !_shuttles.CanFTLTo(ship, Transform(map.Value).MapID, console))
            return false;

        reason = string.Empty;
        return true;
    }

    private void UpdateInterface(EntityUid console)
    {
        if (!TryComp<UserInterfaceComponent>(console, out var ui))
            return;
        foreach (var actor in _ui.GetActors((console, ui), SWGlobalWarTravelConsoleUiKey.Key).ToArray())
        {
            if (!_viewSubscriptions.TryGetValue((console, actor), out var subscription))
                continue;
            _ui.ServerSendUiMessage(console, SWGlobalWarTravelConsoleUiKey.Key,
                new SWGlobalWarTravelSnapshot(GetState(console, actor), GetNetEntity(actor), ++_snapshotSequence, subscription), actor);
        }
    }

    private void RefreshInterfaces()
    {
        if (_markets.IsClosingCampaignStep)
        {
            _nextInterfaceUpdate = TimeSpan.Zero;
            return;
        }
        var query = EntityQueryEnumerator<SWGlobalWarTravelConsoleComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out _, out _))
            UpdateInterface(uid);
    }

    /// <summary>
    /// Builds the current planet, flight status, and destination availability without searching for exit points.
    /// </summary>
    /// <param name="console">Navigation console whose state is being requested.</param>
    public SWGlobalWarTravelConsoleState GetState(EntityUid console, EntityUid? viewer = null)
    {
        var entries = new List<SWGlobalWarTravelDestination>();
        var valid = TryGetContext(console, out var ship, out var planet, out var status);
        if (!_controls.CanOperate(console, viewer, out var denied))
        {
            valid = false;
            status = denied;
        }
        var hasGlobalWarGamerule = _globalWarGamerule.TryGetGlobalWarGamerule(out var globalWarGamerule);
        var current = string.Empty;
        ProtoId<SWGlobalWarPlanetPrototype>? currentId = null;
        if (planet.Valid)
        {
            currentId = Comp<SWGlobalWarPlanetComponent>(planet).Prototype;
            current = _prototypes.Index(currentId.Value).Name;
        }

        if (Transform(console).GridUid is { } grid && TryComp<SWGlobalWarTravelComponent>(grid, out var flight))
        {
            hasGlobalWarGamerule &= flight.GlobalWarGamerule == globalWarGamerule.Owner;
            current = _prototypes.Index(flight.Source).Name;
            currentId = flight.Source;
            status = Loc.GetString("sw-global-war-travel-in-progress", ("planet", _prototypes.Index(flight.Destination).Name));
            valid = false;
        }

        if (hasGlobalWarGamerule)
        {
            foreach (var id in globalWarGamerule.Comp.Planets)
            {
                var reason = string.Empty;
                var available = valid && CheckDestination(console, ship, planet, id, out _, out reason);
                entries.Add(new SWGlobalWarTravelDestination(id, _prototypes.Index(id).Name, available, reason,
                    _markets.GetMarketView(id, viewer), _markets.GetMarketAvailability(id, viewer), _markets.GetPlanetOwner(id)));
            }
        }

        return new SWGlobalWarTravelConsoleState(current, status, entries, currentId);
    }

    /// <summary>
    /// Searches outward from the destination station and reserves the first exit accepted by FTLFree.
    /// </summary>
    /// <param name="ship">Arriving ship grid whose size determines the required clearance.</param>
    /// <param name="flight">Destination and arrival reservation updated when a safe point is found.</param>
    /// <param name="coordinates">Ship origin coordinates for FTLToCoordinates on success.</param>
    private bool TryFindArrival(EntityUid ship, SWGlobalWarTravelComponent flight, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (TerminatingOrDeleted(flight.DestinationMap) || !TryComp<PhysicsComponent>(ship, out var body))
            return false;

        // Collect active exclusion zones; FTLFree filters them by the destination map.
        var exclusions = new List<ShuttleExclusionObject>();
        var exclusionQuery = AllEntityQuery<FTLExclusionComponent, TransformComponent>();
        while (exclusionQuery.MoveNext(out var exclusion, out var xform))
        {
            if (exclusion.Enabled)
                exclusions.Add(new ShuttleExclusionObject(GetNetCoordinates(xform.Coordinates), exclusion.Range, string.Empty));
        }

        // Match the safety radius used by FTLFree around the ship's center of mass.
        var radius = _shuttles.GetFTLBufferRange(ship) + SharedShuttleSystem.FTLBufferRange;
        Vector2 center;
        var angle = Angle.Zero;
        var minimumDistance = 0f;
        if (flight.Returning)
        {
            angle = flight.SourceAngle;
            center = flight.SourceCoordinates.Position + angle.RotateVec(body.LocalCenter);
        }
        else if (flight.UseStationArrival)
        {
            if (!_planets.TryGetOrbitStation(flight.DestinationPlanet, out var station))
                return false;
            var stationGrid = Comp<MapGridComponent>(station.Value);
            center = Vector2.Transform(stationGrid.LocalAABB.Center, _transform.GetWorldMatrix(station.Value));
            minimumDistance = stationGrid.LocalAABB.Size.Length() / 2f + radius + MinimumArrivalDistance;
        }
        else
        {
            if (!TryComp<FTLComponent>(ship, out var ftl) || !Exists(ftl.TargetCoordinates.EntityId) ||
                Transform(ftl.TargetCoordinates.EntityId).MapUid != flight.DestinationMap)
                return false;
            angle = ftl.TargetAngle;
            if (HasComp<MapGridComponent>(ftl.TargetCoordinates.EntityId) &&
                !HasComp<MapComponent>(ftl.TargetCoordinates.EntityId))
                angle += _transform.GetWorldRotation(ftl.TargetCoordinates.EntityId);
            center = _transform.ToMapCoordinates(ftl.TargetCoordinates).Position + angle.RotateVec(body.LocalCenter);
        }

        // Search evenly spaced points on concentric rings, starting with the nearest ring.
        for (var ring = 0; ring < ArrivalSearchRingCount; ring++)
        {
            for (var direction = 0; direction < ArrivalSearchDirectionCount; direction++)
            {
                var directionAngle = direction * MathF.Tau / ArrivalSearchDirectionCount;
                var point = center + new Vector2(MathF.Cos(directionAngle), MathF.Sin(directionAngle)) *
                    (minimumDistance + ring * ArrivalSearchRingSpacing);
                var candidate = new EntityCoordinates(flight.DestinationMap, point);


                if (!_shuttles.FTLFree(ship, candidate, angle, exclusions, checkRange: false) ||
                    IsArrivalReserved(ship, flight.DestinationMap, point, radius))
                    continue;

                // Reserve the first valid area until this flight completes or is cancelled.
                flight.ArrivalCenter = point;
                flight.ArrivalRadius = radius;
                flight.ArrivalAngle = angle;

                coordinates = candidate.Offset(-angle.RotateVec(body.LocalCenter));
                return true;
            }
        }

        // Every candidate is obstructed, excluded, or reserved by another flight.
        return false;
    }

    /// <summary>
    /// Checks whether an arrival circle overlaps another incoming ship's reservation on the same map.
    /// </summary>
    /// <param name="ship">Ship whose own reservation should be ignored.</param>
    /// <param name="map">Destination map containing the proposed arrival area.</param>
    /// <param name="point">Arrival circle center in map coordinates.</param>
    /// <param name="radius">Arrival circle radius in meters, including FTL safety buffers.</param>
    private bool IsArrivalReserved(EntityUid ship, EntityUid map, Vector2 point, float radius)
    {
        var query = AllEntityQuery<SWGlobalWarTravelComponent>();
        while (query.MoveNext(out var uid, out var other))
        {
            if (uid != ship && other.DestinationMap == map && other.ArrivalRadius > 0 &&
                Vector2.Distance(point, other.ArrivalCenter) <= radius + other.ArrivalRadius)
                return true;
        }

        return false;
    }

    private void OnBeforeArrival(Entity<SWGlobalWarTravelComponent> ent, ref FTLBeforeArrivalEvent args)
    {
        if (ent.Comp.Withdrawal is { } withdrawal)
        {
            HandleWithdrawalArrival(ent, withdrawal, ref args);
            return;
        }
        var active = _globalWarGamerule.TryGetGlobalWarGamerule(out var rule);
        var current = active &&
                      rule.Owner == ent.Comp.GlobalWarGamerule && rule.Comp.Generation == ent.Comp.Generation;
        var allowed = current && TryEvaluateCrew(ent.Owner, ent.Comp.DestinationPlanet, true, out _);
        if (!ent.Comp.Returning && !allowed)
            BeginReturn(ent);
        // Returning from hyperspace is a new entry. It must not inherit local garrison movement rights.
        if (ent.Comp.Returning && active &&
            (!current || !TryEvaluateCrew(ent.Owner, ent.Comp.DestinationPlanet, true, out _)))
        {
            args.Cancelled = true;
            args.RetryDelay = TimeSpan.FromSeconds(10);
            return;
        }
        var ftl = Comp<FTLComponent>(ent.Owner);
        if (!ent.Comp.Returning && ent.Comp.DockTarget is { } dockTarget && !TerminatingOrDeleted(dockTarget) &&
            Transform(dockTarget).MapUid == ent.Comp.DestinationMap &&
            _docking.GetDockingConfig(ent.Owner, dockTarget, ftl.PriorityTag) is { } docking)
        {
            var center = _transform.ToMapCoordinates(docking.Coordinates).Position;
            var radius = _shuttles.GetFTLBufferRange(ent.Owner) + SharedShuttleSystem.FTLBufferRange;
            if (!IsArrivalReserved(ent.Owner, ent.Comp.DestinationMap, center, radius) &&
                !IsAreaExcluded(ent.Comp.DestinationMap, center, radius))
            {
                ent.Comp.ArrivalCenter = center;
                ent.Comp.ArrivalRadius = radius;
                // Keep coordinates relative to the destination grid: ShuttleSystem must execute FTLDock.
                ftl.TargetCoordinates = docking.Coordinates;
                ftl.TargetAngle = docking.Angle;
                return;
            }
        }
        if (!TryFindArrival(ent.Owner, ent.Comp, out var coordinates))
        {
            ent.Comp.BlockedSince ??= _timing.CurTime;
            if (!ent.Comp.Returning && _timing.CurTime - ent.Comp.BlockedSince >= MaximumDestinationWait)
                BeginReturn(ent);
            args.Cancelled = true;
            args.RetryDelay = TimeSpan.FromSeconds(ent.Comp.Returning ? 10 : 2);
            return;
        }

        ftl.TargetCoordinates = coordinates;
        ftl.TargetAngle = ent.Comp.ArrivalAngle;
    }

    private bool IsAreaExcluded(EntityUid map, Vector2 center, float radius)
    {
        var query = AllEntityQuery<FTLExclusionComponent, TransformComponent>();
        while (query.MoveNext(out var exclusion, out var transform))
        {
            if (exclusion.Enabled && transform.MapUid == map &&
                Vector2.Distance(center, _transform.GetWorldPosition(transform)) <= exclusion.Range + radius)
                return true;
        }
        return false;
    }

    private void BeginReturn(Entity<SWGlobalWarTravelComponent> ent)
    {
        _planets.ReleaseArrival(ent.Comp.DestinationMap, ent.Owner);
        ent.Comp.Returning = true;
        ent.Comp.Destination = ent.Comp.Source;
        ent.Comp.DestinationPlanet = ent.Comp.SourcePlanet;
        ent.Comp.DestinationMap = ent.Comp.SourceMap;
        ent.Comp.ArrivalRadius = 0;
        // A stopped rule leaves its maps unpaused. An active rule explicitly wakes the original location.
        if (_globalWarGamerule.TryGetGlobalWarGamerule(out var rule) &&
            rule.Owner == ent.Comp.GlobalWarGamerule && rule.Comp.Generation == ent.Comp.Generation)
            _planets.TryPrepareArrival(ent.Comp.Source, SWGlobalWarPlanetLocation.Orbit, ent.Owner, out _);
    }

    private void InitializeFlight(EntityUid ship, SWGlobalWarTravelComponent flight,
        EntityUid source, EntityUid destination, EntityUid map)
    {
        var ruleUid = Comp<SWGlobalWarPlanetComponent>(source).GlobalWarGamerule;
        flight.GlobalWarGamerule = ruleUid;
        flight.Generation = Comp<SWGlobalWarGameruleComponent>(ruleUid).Generation;
        flight.FlightId = Guid.NewGuid();
        flight.SourcePlanet = source;
        flight.SourceMap = Transform(ship).MapUid!.Value;
        flight.SourceCoordinates = new EntityCoordinates(flight.SourceMap, _transform.GetWorldPosition(ship));
        flight.SourceAngle = _transform.GetWorldRotation(ship);
        flight.Source = Comp<SWGlobalWarPlanetComponent>(source).Prototype;
        flight.Destination = Comp<SWGlobalWarPlanetComponent>(destination).Prototype;
        flight.DestinationPlanet = destination;
        flight.DestinationMap = map;
    }

    private bool TryEvaluateCrew(EntityUid ship, EntityUid destination, bool arriving, out string reason)
    {
        reason = Loc.GetString("sw-global-war-access-denied");
        if (!_globalWarGamerule.TryGetGlobalWarGamerule(out var rule))
            return false;
        var count = 0;
        var crew = AllEntityQuery<MindContainerComponent, TransformComponent>();
        while (crew.MoveNext(out var actor, out var mind, out var transform))
        {
            if (!mind.HasMind || transform.GridUid != ship || HasComp<GhostComponent>(actor))
                continue;
            count++;
            var decision = arriving ? _policy.EvaluateInFlight(actor, ship) :
                _policy.EvaluateTransition(actor, destination, SWGlobalWarPlanetLocation.Orbit,
                    SWGlobalWarTransitionKind.Ftl, rule.Comp.Generation);
            if (!decision.Allowed)
            {
                reason = Loc.GetString(decision.Reason);
                return false;
            }
        }
        if (count == 0)
            return false;
        reason = string.Empty;
        return true;
    }

    private void OnFtlAttempt(ref SWGlobalWarFtlAttemptEvent args)
    {
        if (args.Cancelled || !_globalWarGamerule.TryGetGlobalWarGamerule(out _))
            return;
        if (!TryComp<TransformComponent>(args.Shuttle, out var origin) ||
            !TryComp<TransformComponent>(args.Destination.EntityId, out var target))
            return;
        if (!HasComp<SWGlobalWarPlanetMapComponent>(origin.MapUid) &&
            !HasComp<SWGlobalWarPlanetMapComponent>(target.MapUid))
            return;
        // A campaign jump goes only along a hyperroute, from one orbit to the orbit of a connected world. The helm's
        // own FTL map would reach any point of any map: orbits off the route, the current orbit and planet surfaces.
        if (HasComp<FTLComponent>(args.Shuttle) ||
            !_planets.TryGetPhysicalLocation(args.Shuttle, out var source, out var from) ||
            !_planets.TryGetPhysicalLocation(args.Destination.EntityId, out var destination, out var to) ||
            from != SWGlobalWarPlanetLocation.Orbit || to != SWGlobalWarPlanetLocation.Orbit || source == destination ||
            _planets.IsPlanetStationGrid(source, args.Shuttle) ||
            !_planets.CanTravel(Comp<SWGlobalWarPlanetComponent>(source).Prototype,
                Comp<SWGlobalWarPlanetComponent>(destination).Prototype) ||
            !TryEvaluateCrew(args.Shuttle, destination, false, out _))
        {
            args.Cancelled = true;
            return;
        }
        if (TryComp<SWGlobalWarTravelComponent>(args.Shuttle, out var existing))
        {
            args.Cancelled = existing.SourcePlanet != source || existing.DestinationPlanet != destination ||
                             existing.DestinationMap != target.MapUid;
            return;
        }
        var destinationId = Comp<SWGlobalWarPlanetComponent>(destination).Prototype;
        if (!_planets.TryPrepareArrival(destinationId, to, args.Shuttle, out var map))
        {
            args.Cancelled = true;
            return;
        }
        var flight = AddComp<SWGlobalWarTravelComponent>(args.Shuttle);
        InitializeFlight(args.Shuttle, flight, source, destination, map.Value);
        flight.DockTarget = args.DockTarget;
    }

    private void OnCompleted(Entity<SWGlobalWarTravelComponent> ent, ref FTLCompletedEvent args)
    {
        // This acknowledges travel only. Cargo identity, unloading, acceptance and payment belong to the consumer.
        if (!ent.Comp.ArrivalPublished && ent.Comp.FlightId != Guid.Empty && !ent.Comp.Returning &&
            ent.Comp.Withdrawal == null && Transform(ent.Owner).MapUid == ent.Comp.DestinationMap &&
            _shipOwnership.TryGetOwner(ent.Owner, ent.Comp.Generation, out var owner, out var ownershipRevision) &&
            TryEvaluateCrew(ent.Owner, ent.Comp.DestinationPlanet, true, out _))
        {
            ent.Comp.ArrivalPublished = true;
            var arrived = new SWGlobalWarShipArrivalCommittedEvent(ent.Owner, ent.Comp.Generation,
                ent.Comp.FlightId, ent.Comp.SourcePlanet, ent.Comp.DestinationPlanet, owner, ownershipRevision);
            RaiseLocalEvent(ref arrived);
        }
        RemCompDeferred<SWGlobalWarTravelComponent>(ent.Owner);
    }

    private void OnFtlRemoved(Entity<FTLComponent> ent, ref ComponentRemove args)
    {
        if (HasComp<SWGlobalWarTravelComponent>(ent.Owner))
            RemCompDeferred<SWGlobalWarTravelComponent>(ent.Owner);
    }

    private void OnTravelShutdown(Entity<SWGlobalWarTravelComponent> ent, ref ComponentShutdown args)
    {
        _planets.ReleaseArrival(ent.Comp.DestinationMap, ent.Owner);
    }
}

/// <summary>Verified physical arrival, never evidence of goods delivery or authorization to pay.</summary>
[ByRefEvent]
public readonly record struct SWGlobalWarShipArrivalCommittedEvent(EntityUid Ship, Guid Generation, Guid FlightId,
    EntityUid SourcePlanet, EntityUid DestinationPlanet, SWGlobalWarFaction Owner, long OwnershipRevision);
