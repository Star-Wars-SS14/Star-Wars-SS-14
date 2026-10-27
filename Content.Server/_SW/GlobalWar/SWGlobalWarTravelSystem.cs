using System.Numerics;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Interaction;
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

namespace Content.Server._SW.GlobalWar;

// Handles interplanetary console requests, safe arrival points, and timed FTL travel.
public sealed class SWGlobalWarTravelSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarGameruleSystem _globalWarGamerule = default!;
    [Dependency] private readonly ShuttleSystem _shuttles = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    // Extra clearance in meters between the station's bounding circle and the ship's FTL safety circle.
    private const float MinimumArrivalDistance = 8f;

    // Number of search rings, directions per ring, and radial spacing in meters.
    private const int ArrivalSearchRingCount = 4;
    private const int ArrivalSearchDirectionCount = 16;
    private const float ArrivalSearchRingSpacing = 64f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarTravelConsoleComponent, BoundUIOpenedEvent>(OnOpen);
        Subs.BuiEvents<SWGlobalWarTravelConsoleComponent>(SWGlobalWarTravelConsoleUiKey.Key, subs =>
        {
            subs.Event<SWGlobalWarTravelRequest>(OnRequest);
        });
        SubscribeLocalEvent<SWGlobalWarTravelComponent, FTLBeforeArrivalEvent>(OnBeforeArrival);
        SubscribeLocalEvent<SWGlobalWarTravelComponent, FTLCompletedEvent>(OnCompleted);
        SubscribeLocalEvent<SWGlobalWarTravelComponent, ComponentShutdown>(OnTravelShutdown);
        SubscribeLocalEvent<FTLComponent, ComponentRemove>(OnFtlRemoved);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
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

        if (!TryGetContext(console, out var ship, out var planet, out reason) ||
            !CheckDestination(console, ship, planet, destination, out var targetPlanet, out reason))
            return false;

        var source = Comp<SWGlobalWarPlanetComponent>(planet).Prototype;

        if (!_planets.TryPrepareArrival(destination, SWGlobalWarPlanetLocation.Orbit, ship, out var map))
        {
            reason = Loc.GetString("sw-global-war-travel-unavailable");
            return false;
        }

        var flight = AddComp<SWGlobalWarTravelComponent>(ship);
        flight.GlobalWarGamerule = Comp<SWGlobalWarPlanetComponent>(planet).GlobalWarGamerule;
        flight.Source = source;
        flight.Destination = destination;
        flight.DestinationPlanet = targetPlanet;
        flight.DestinationMap = map.Value;
        try
        {
            if (!TryFindArrival(ship, flight, out var coordinates))
            {
                reason = Loc.GetString("sw-global-war-travel-blocked");
                return false;
            }

            _shuttles.FTLToCoordinates(ship, Comp<ShuttleComponent>(ship), coordinates, Angle.Zero);
            if (!HasComp<FTLComponent>(ship))
            {
                reason = Loc.GetString("sw-global-war-travel-unavailable");
                return false;
            }

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
        reason = Loc.GetString("sw-global-war-travel-unavailable");
        if (!_planets.TryGetPlanetMap(targetPlanet, SWGlobalWarPlanetLocation.Orbit, out var map) ||
            !_planets.TryGetOrbitStation(targetPlanet, out _) ||
            !_shuttles.CanFTLTo(ship, Transform(map.Value).MapID, console))
            return false;

        reason = string.Empty;
        return true;
    }

    private void UpdateInterface(EntityUid console)
    {
        _ui.SetUiState(console, SWGlobalWarTravelConsoleUiKey.Key, GetState(console));
    }

    /// <summary>
    /// Builds the current planet, flight status, and destination availability without searching for exit points.
    /// </summary>
    /// <param name="console">Navigation console whose state is being requested.</param>
    public SWGlobalWarTravelConsoleState GetState(EntityUid console)
    {
        var entries = new List<SWGlobalWarTravelDestination>();
        var valid = TryGetContext(console, out var ship, out var planet, out var status);
        var hasGlobalWarGamerule = _globalWarGamerule.TryGetGlobalWarGamerule(out var globalWarGamerule);
        var current = string.Empty;
        if (planet.Valid)
            current = _prototypes.Index(Comp<SWGlobalWarPlanetComponent>(planet).Prototype).Name;

        if (Transform(console).GridUid is { } grid && TryComp<SWGlobalWarTravelComponent>(grid, out var flight))
        {
            hasGlobalWarGamerule &= flight.GlobalWarGamerule == globalWarGamerule.Owner;
            current = _prototypes.Index(flight.Source).Name;
            status = Loc.GetString("sw-global-war-travel-in-progress", ("planet", _prototypes.Index(flight.Destination).Name));
            valid = false;
        }

        if (hasGlobalWarGamerule)
        {
            foreach (var id in globalWarGamerule.Comp.Planets)
            {
                var reason = status;
                var available = valid && CheckDestination(console, ship, planet, id, out _, out reason);
                entries.Add(new SWGlobalWarTravelDestination(id, _prototypes.Index(id).Name, available, reason));
            }
        }

        return new SWGlobalWarTravelConsoleState(current, status, entries);
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
        if (TerminatingOrDeleted(flight.DestinationMap) ||
            !_planets.TryGetOrbitStation(flight.DestinationPlanet, out var station) ||
            !TryComp<PhysicsComponent>(ship, out var body))
            return false;

        // Collect active exclusion zones; FTLFree filters them by the destination map.
        var exclusions = new List<ShuttleExclusionObject>();
        var exclusionQuery = AllEntityQuery<FTLExclusionComponent, TransformComponent>();
        while (exclusionQuery.MoveNext(out var exclusion, out var xform))
        {
            if (exclusion.Enabled)
                exclusions.Add(new ShuttleExclusionObject(GetNetCoordinates(xform.Coordinates), exclusion.Range, string.Empty));
        }

        var stationGrid = Comp<MapGridComponent>(station.Value);
        var center = Vector2.Transform(stationGrid.LocalAABB.Center, _transform.GetWorldMatrix(station.Value));
        // Match the safety radius used by FTLFree around the ship's center of mass.
        var radius = _shuttles.GetFTLBufferRange(ship) + SharedShuttleSystem.FTLBufferRange;
        // Half the diagonal encloses the rotated station; add the ship's safety radius and extra clearance.
        var minimumDistance = stationGrid.LocalAABB.Size.Length() / 2f + radius + MinimumArrivalDistance;

        // Search evenly spaced points on concentric rings, starting with the nearest ring.
        for (var ring = 0; ring < ArrivalSearchRingCount; ring++)
        {
            for (var direction = 0; direction < ArrivalSearchDirectionCount; direction++)
            {
                var angle = direction * MathF.Tau / ArrivalSearchDirectionCount;
                var point = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) *
                    (minimumDistance + ring * ArrivalSearchRingSpacing);
                var candidate = new EntityCoordinates(flight.DestinationMap, point);


                if (!_shuttles.FTLFree(ship, candidate, Angle.Zero, exclusions, checkRange: false) ||
                    IsArrivalReserved(ship, flight.DestinationMap, point, radius))
                    continue;

                // Reserve the first valid area until this flight completes or is cancelled.
                flight.ArrivalCenter = point;
                flight.ArrivalRadius = radius;

                coordinates = candidate.Offset(-body.LocalCenter);
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
        if (!TryFindArrival(ent.Owner, ent.Comp, out var coordinates))
        {
            args.Cancelled = true;
            return;
        }

        var ftl = Comp<FTLComponent>(ent.Owner);
        ftl.TargetCoordinates = coordinates;
        ftl.TargetAngle = Angle.Zero;
    }

    private void OnCompleted(Entity<SWGlobalWarTravelComponent> ent, ref FTLCompletedEvent args)
    {
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
