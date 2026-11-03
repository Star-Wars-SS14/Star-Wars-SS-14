using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server.GameTicking;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Server.Station.Systems;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Access;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Maps;
using Content.Shared.Mind.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.CombatMode;
using Content.Server._SW.Lathe.SWLathe;
using Content.Server.Atmos.Components;
using Content.Shared.Station.Components;
using Content.Shared.Trigger.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar;

/// <summary>
/// Manages planets belonging to Global War.
/// </summary>
public sealed class SWGlobalWarPlanetSystem : EntitySystem
{
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly SWGlobalWarGameruleSystem _globalWarGamerule = default!;
    [Dependency] private readonly MapSystem _maps = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly StationSystem _stations = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly TimeSpan ActivityCheckInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextActivityCheck;
    private readonly HashSet<EntityUid> _occupiedMaps = new();
    private HashSet<EntityUid>? _loadingStations;
    private Guid _ownershipBatchGeneration;
    private readonly List<SWGlobalWarOwnershipChangedEvent> _ownershipBatchEvents = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ActorComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<ActorComponent, EntParentChangedMessage>(OnPlayerMoved);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<StationDataComponent, ComponentInit>(OnStationInitializing);
    }

    private void OnStationInitializing(Entity<StationDataComponent> ent, ref ComponentInit args)
    {
        // LoadGameMap creates stations synchronously, before returning its grid list. Capture them before
        // any station startup handler can fail, within this load attempt (including nested load isolation).
        _loadingStations?.Add(ent.Owner);
    }

    /// <summary>
    /// Creates strategic controllers. Physical locations are loaded only by an explicit visit or spawn preparation.
    /// </summary>
    public void InitializePlanets()
    {
        if (!_globalWarGamerule.TryGetGlobalWarGamerule(out var globalWarGamerule))
            return;

        if (globalWarGamerule.Comp.PlanetEntities.Count != 0)
            return;

        if (globalWarGamerule.Comp.EmptyMapPauseDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(globalWarGamerule), "Empty map pause delay cannot be negative.");

        if (globalWarGamerule.Comp.Planets.Distinct().Count() != globalWarGamerule.Comp.Planets.Count)
            throw new ArgumentException("A GlobalWarGamerule cannot contain duplicate planets.", nameof(globalWarGamerule));

        // Validate references before creating any entities.
        foreach (var id in globalWarGamerule.Comp.Planets)
        {
            var prototype = _prototypes.Index(id);
            if (!float.IsFinite(prototype.MapPosition.X) || !float.IsFinite(prototype.MapPosition.Y))
                throw new ArgumentException($"Planet {id} mapPosition must be finite.", nameof(globalWarGamerule));

            if (prototype.HyperspaceTime is { } duration && (!float.IsFinite(duration) || duration < 0f))
                throw new ArgumentException($"Planet {id} hyperspaceTime must be finite and non-negative.", nameof(globalWarGamerule));

            if (prototype.Surface is { } surface)
                _prototypes.Index(surface);
            if (prototype.Orbit is { } orbit)
                _prototypes.Index(orbit);
            foreach (var (target, time) in prototype.TravelTimes)
            {
                if (!prototype.Connections.Contains(target) || time < TimeSpan.Zero)
                    throw new ArgumentException($"Planet {id} has an invalid directed travel duration.");
            }
        }

        try
        {
            foreach (var id in globalWarGamerule.Comp.Planets)
            {
                var prototype = _prototypes.Index(id);
                var uid = Spawn(null, MapCoordinates.Nullspace);
                var planet = AddComp<SWGlobalWarPlanetComponent>(uid);
                planet.Prototype = id;
                planet.GlobalWarGamerule = globalWarGamerule.Owner;
                planet.OrbitOwner = prototype.InitialOwner;
                planet.SurfaceOwner = prototype.InitialOwner;
                planet.Locations.Add(SWGlobalWarPlanetLocation.Surface, SWGlobalWarLocationState.Unloaded);
                planet.Locations.Add(SWGlobalWarPlanetLocation.Orbit, SWGlobalWarLocationState.Unloaded);
                _metadata.SetEntityName(uid, prototype.Name);
                globalWarGamerule.Comp.PlanetEntities.Add(id, uid);
            }
        }
        catch
        {
            DeletePlanets(globalWarGamerule);
            throw;
        }
    }

    /// <summary>Loads a location once. A destroyed visited map is never replenished from its original YAML.</summary>
    public bool TryEnsureLocation(EntityUid planet, SWGlobalWarPlanetLocation location,
        [NotNullWhen(true)] out EntityUid? map, out string failure)
    {
        map = null;
        failure = "sw-global-war-location-unavailable";
        if (!TryComp<SWGlobalWarPlanetComponent>(planet, out var data) ||
            !_globalWarGamerule.TryGetGlobalWarGamerule(out var rule) || data.GlobalWarGamerule != rule.Owner ||
            !data.Locations.TryGetValue(location, out var state) || state == SWGlobalWarLocationState.Loading)
            return false;

        if (data.Maps.ContainsKey(location))
        {
            if (!TryGetPlanetMap(planet, location, out map))
            {
                data.Locations[location] = SWGlobalWarLocationState.Destroyed;
                return false;
            }
            WakeMap(map.Value, Comp<SWGlobalWarPlanetMapComponent>(map.Value));
            failure = string.Empty;
            return true;
        }

        var definition = _prototypes.Index(data.Prototype);
        var prototype = location == SWGlobalWarPlanetLocation.Surface ? definition.Surface : definition.Orbit;
        if (prototype == null || state == SWGlobalWarLocationState.Destroyed)
            return false;

        data.Locations[location] = SWGlobalWarLocationState.Loading;
        var mapId = MapId.Nullspace;
        var attemptStations = new HashSet<EntityUid>();
        var attemptGrids = new HashSet<EntityUid>();
        var previousLoadingStations = _loadingStations;
        try
        {
            _loadingStations = attemptStations;
            var grids = _ticker.LoadGameMap(_prototypes.Index(prototype.Value), out mapId,
                offset: Vector2.Zero, rot: Angle.Zero);
            _loadingStations = previousLoadingStations;
            map = _maps.GetMap(mapId);
            data.Maps.Add(location, map.Value);
            foreach (var grid in grids)
            {
                if (_stations.GetOwningStation(grid) is not { } station)
                    continue;
                attemptStations.Add(station);
                attemptGrids.Add(grid);
                data.Stations.Add(station);
                data.StationGrids.Add(grid);
            }

            var link = AddComp<SWGlobalWarPlanetMapComponent>(map.Value);
            link.Planet = planet;
            link.Location = location;
            link.EmptySince = _timing.CurTime;
            AddComp<SWGlobalWarGuardedMapComponent>(map.Value);
            if (!_maps.IsInitialized(mapId))
                _maps.InitializeMap(mapId);

            data.Locations[location] = SWGlobalWarLocationState.Active;
            var ready = new SWGlobalWarLocationReadyEvent(planet, location, map.Value, rule.Comp.Generation);
            RaiseLocalEvent(ref ready);
            failure = string.Empty;
            return true;
        }
        catch
        {
            // Roll back only this attempt; already visited locations keep their entities and inventories.
            foreach (var station in attemptStations)
            {
                data.Stations.Remove(station);
                if (!TerminatingOrDeleted(station))
                    Del(station);
            }
            data.StationGrids.ExceptWith(attemptGrids);
            if (_maps.TryGetMap(mapId, out var failed) && !TerminatingOrDeleted(failed.Value))
                Del(failed.Value);
            data.Maps.Remove(location);
            data.Locations[location] = SWGlobalWarLocationState.Failed;
            map = null;
            return false;
        }
        finally
        {
            _loadingStations = previousLoadingStations;
        }
    }

    /// <summary>Atomically changes one owner. Caller validates political authority before invoking this server API.</summary>
    public bool TryChangeOwnership(EntityUid planet, SWGlobalWarPlanetLocation location, SWGlobalWarFaction owner,
        long expectedRevision, Guid generation, Guid operationId, out long revision)
    {
        revision = 0;
        if (!TryComp<SWGlobalWarPlanetComponent>(planet, out var data) || operationId == Guid.Empty ||
            !Enum.IsDefined(owner) || !Enum.IsDefined(location) ||
            !_globalWarGamerule.TryGetGlobalWarGamerule(out var rule) || rule.Owner != data.GlobalWarGamerule ||
            generation != rule.Comp.Generation)
            return false;
        revision = data.OwnershipRevision;
        if (data.OwnershipOperations.TryGetValue(operationId, out var previous))
        {
            revision = previous.Revision;
            return previous.Location == location && previous.Owner == owner;
        }
        // Campaign ownership is committed only by the ordered C/F coordinator. Completed receipts above remain readable.
        if (TryComp<SWGlobalWarCampaignComponent>(rule.Owner, out var campaign) &&
            (campaign.Phase is SWGlobalWarCampaignPhase.Ending or SWGlobalWarCampaignPhase.Stopped ||
             _ownershipBatchGeneration != generation))
            return false;
        if (expectedRevision != revision || revision == long.MaxValue)
            return false;
        var old = location == SWGlobalWarPlanetLocation.Orbit ? data.OrbitOwner : data.SurfaceOwner;
        if (old != owner)
        {
            if (location == SWGlobalWarPlanetLocation.Orbit)
                data.OrbitOwner = owner;
            else
                data.SurfaceOwner = owner;
            revision = ++data.OwnershipRevision;
        }
        data.OwnershipOperations.Add(operationId, (location, owner, revision));
        if (old != owner)
        {
            var changed = new SWGlobalWarOwnershipChangedEvent(planet, location, old, owner, revision, operationId);
            if (_ownershipBatchGeneration == generation)
                _ownershipBatchEvents.Add(changed);
            else
                RaiseLocalEvent(ref changed);
        }
        return true;
    }

    internal void BeginCampaignOwnershipBatch(Guid generation)
    {
        if (_ownershipBatchGeneration != Guid.Empty)
            throw new InvalidOperationException("A campaign ownership batch is already active.");
        _ownershipBatchGeneration = generation;
        _ownershipBatchEvents.Clear();
    }

    internal SWGlobalWarOwnershipChangedEvent[] EndCampaignOwnershipBatch()
    {
        var events = _ownershipBatchEvents.ToArray();
        _ownershipBatchEvents.Clear();
        _ownershipBatchGeneration = Guid.Empty;
        return events;
    }

    internal void PublishCampaignOwnership(Guid generation, SWGlobalWarOwnershipChangedEvent[] events)
    {
        foreach (var changed in events)
        {
            if (!_globalWarGamerule.TryGetGlobalWarGamerule(out var rule) || rule.Comp.Generation != generation)
                return;
            var notification = changed;
            RaiseLocalEvent(ref notification);
        }
    }

    /// <summary>Verifies a committed territorial capture used by D's batch provenance evidence.</summary>
    public bool HasOwnershipReceipt(EntityUid planet, Guid generation, Guid operation,
        SWGlobalWarPlanetLocation location, SWGlobalWarFaction owner)
    {
        return TryComp<SWGlobalWarPlanetComponent>(planet, out var data) &&
               _globalWarGamerule.TryGetGlobalWarGamerule(out var rule) && rule.Comp.Generation == generation &&
               data.GlobalWarGamerule == rule.Owner && data.OwnershipOperations.TryGetValue(operation, out var receipt) &&
               receipt.Location == location && receipt.Owner == owner;
    }

    /// <summary>Holds one physical location awake for an operation owned by a live entity.</summary>
    public bool TryAcquireLocationLease(EntityUid planet, SWGlobalWarPlanetLocation location, EntityUid owner,
        Guid operation, string reason, out SWGlobalWarLocationLease lease)
    {
        lease = default;
        if (operation == Guid.Empty || TerminatingOrDeleted(owner) || string.IsNullOrWhiteSpace(reason) ||
            !TryEnsureLocation(planet, location, out var map, out _) ||
            !_globalWarGamerule.TryGetGlobalWarGamerule(out var rule))
            return false;
        var link = Comp<SWGlobalWarPlanetMapComponent>(map.Value);
        if (link.Leases.TryGetValue(operation, out var existing) && existing != (owner, reason))
            return false;
        link.Leases[operation] = (owner, reason);
        lease = new SWGlobalWarLocationLease(map.Value, rule.Comp.Generation, operation);
        return true;
    }

    /// <summary>Idempotently releases a lease; stale round callbacks cannot affect a later round.</summary>
    public void ReleaseLocationLease(SWGlobalWarLocationLease lease)
    {
        if (_globalWarGamerule.TryGetGlobalWarGamerule(out var rule) && rule.Comp.Generation == lease.Generation &&
            TryComp<SWGlobalWarPlanetMapComponent>(lease.Map, out var link))
            link.Leases.Remove(lease.Operation);
    }

    public bool TryGetPlanet(ProtoId<SWGlobalWarPlanetPrototype> id,
        [NotNullWhen(true)] out EntityUid? planet)
    {
        planet = null;
        if (!_globalWarGamerule.TryGetGlobalWarGamerule(out var globalWarGamerule) ||
            !globalWarGamerule.Comp.PlanetEntities.TryGetValue(id, out var uid) || TerminatingOrDeleted(uid) ||
            !HasComp<SWGlobalWarPlanetComponent>(uid))
            return false;

        planet = uid;
        return true;
    }

    public bool TryGetPlanetMap(EntityUid planet, SWGlobalWarPlanetLocation location,
        [NotNullWhen(true)] out EntityUid? map)
    {
        map = null;
        if (!TryComp<SWGlobalWarPlanetComponent>(planet, out var data) ||
            !data.Maps.TryGetValue(location, out var uid) || TerminatingOrDeleted(uid) ||
            !HasComp<MapComponent>(uid) || !TryComp<SWGlobalWarPlanetMapComponent>(uid, out var link) ||
            link.Planet != planet || link.Location != location)
            return false;

        map = uid;
        return true;
    }

    /// <summary>
    /// Resolves the planet of a character, ship, grid, or other entity on one of its maps.
    /// </summary>
    public bool TryGetPlanetForEntity(EntityUid uid, [NotNullWhen(true)] out EntityUid? planet)
    {
        planet = null;
        if (!TryComp(uid, out TransformComponent? transform) ||
            !TryComp<SWGlobalWarPlanetMapComponent>(transform.MapUid, out var map) ||
            TerminatingOrDeleted(map.Planet) || !HasComp<SWGlobalWarPlanetComponent>(map.Planet))
            return false;

        planet = map.Planet;
        return true;
    }

    /// <summary>Returns the active rule's physical location; origin station membership grants no location.</summary>
    public bool TryGetPhysicalLocation(EntityUid uid, out EntityUid planet, out SWGlobalWarPlanetLocation location)
    {
        planet = default;
        location = default;
        if (!TryComp<TransformComponent>(uid, out var transform) ||
            !TryComp<SWGlobalWarPlanetMapComponent>(transform.MapUid, out var map) ||
            !TryComp<SWGlobalWarPlanetComponent>(map.Planet, out var data) ||
            !_globalWarGamerule.TryGetGlobalWarGamerule(out var rule) || data.GlobalWarGamerule != rule.Owner)
            return false;
        planet = map.Planet;
        location = map.Location;
        return true;
    }

    public bool IsPlanetStationGrid(EntityUid planet, EntityUid grid)
    {
        return TryComp<SWGlobalWarPlanetComponent>(planet, out var data) && data.StationGrids.Contains(grid);
    }

    /// <summary>
    /// Finds the largest surviving grid of a station loaded with this planet's orbit map.
    /// Player ships later assigned to a station are not used as arrival stations.
    /// </summary>
    public bool TryGetOrbitStation(EntityUid planet, [NotNullWhen(true)] out EntityUid? grid)
    {
        grid = null;
        if (!TryGetPlanetMap(planet, SWGlobalWarPlanetLocation.Orbit, out var orbit))
            return false;

        var size = -1f;
        foreach (var candidate in Comp<SWGlobalWarPlanetComponent>(planet).StationGrids)
        {
            if (TerminatingOrDeleted(candidate) || Transform(candidate).MapUid != orbit ||
                !TryComp<MapGridComponent>(candidate, out var mapGrid) || mapGrid.LocalAABB.MaxDimension <= size)
                continue;

            size = mapGrid.LocalAABB.MaxDimension;
            grid = candidate;
        }

        return grid != null;
    }

    /// <summary>
    /// Checks connectivity in the active GlobalWarGamerule. Local surface/orbit transitions are allowed.
    /// This is a future navigation API, not an interception of the existing FTL system.
    /// </summary>
    public bool CanTravel(ProtoId<SWGlobalWarPlanetPrototype> from,
        ProtoId<SWGlobalWarPlanetPrototype> to)
    {
        return TryGetPlanet(from, out _) && TryGetPlanet(to, out _) &&
               (from == to || _prototypes.Index(from).Connections.Contains(to));
    }

    /// <summary>
    /// Wakes and reserves a destination for future travel code without moving the traveller.
    /// The caller checks its route and releases the reservation if travel is cancelled.
    /// </summary>
    public bool TryPrepareArrival(ProtoId<SWGlobalWarPlanetPrototype> destination,
        SWGlobalWarPlanetLocation location, EntityUid traveller, [NotNullWhen(true)] out EntityUid? map)
    {
        map = null;
        if (TerminatingOrDeleted(traveller) ||
            !TryComp(traveller, out TransformComponent? _) || !TryGetPlanet(destination, out var planet) ||
            !TryEnsureLocation(planet.Value, location, out map, out _))
            return false;

        var link = Comp<SWGlobalWarPlanetMapComponent>(map.Value);
        link.IncomingTravellers.Add(traveller);
        WakeMap(map.Value, link);
        return true;
    }

    /// <summary>
    /// Releases a cancelled or completed arrival. Repeated releases are harmless.
    /// </summary>
    public void ReleaseArrival(EntityUid map, EntityUid traveller)
    {
        if (TryComp<SWGlobalWarPlanetMapComponent>(map, out var link))
            link.IncomingTravellers.Remove(traveller);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextActivityCheck)
            return;

        _nextActivityCheck = _timing.CurTime + ActivityCheckInterval;
        if (!_globalWarGamerule.TryGetGlobalWarGamerule(out var globalWarGamerule))
            return;

        _occupiedMaps.Clear();
        // Includes disconnected bodies and paused entities, unlike the session list.
        var minds = AllEntityQuery<MindContainerComponent, TransformComponent>();
        while (minds.MoveNext(out var uid, out var mind, out var bodyTransform))
        {
            if (mind.HasMind && !HasComp<GhostComponent>(uid) && bodyTransform.MapUid is { } occupied)
                _occupiedMaps.Add(occupied);
        }
        var combatants = AllEntityQuery<CombatModeComponent, TransformComponent>();
        while (combatants.MoveNext(out var combat, out var transform))
        {
            if (combat.IsInCombatMode && transform.MapUid is { } occupied)
                _occupiedMaps.Add(occupied);
        }
        var fires = AllEntityQuery<FlammableComponent, TransformComponent>();
        while (fires.MoveNext(out var fire, out var transform))
        {
            if (fire.OnFire && transform.MapUid is { } occupied)
                _occupiedMaps.Add(occupied);
        }
        var factories = AllEntityQuery<SWLatheComponent, TransformComponent>();
        while (factories.MoveNext(out var factory, out var transform))
        {
            if (factory.CurrentRecipeProducting != null && transform.MapUid is { } occupied)
                _occupiedMaps.Add(occupied);
        }
        var timers = AllEntityQuery<ActiveTimerTriggerComponent, TransformComponent>();
        while (timers.MoveNext(out _, out var transform))
        {
            if (transform.MapUid is { } occupied)
                _occupiedMaps.Add(occupied);
        }
        var atmospheres = AllEntityQuery<GridAtmosphereComponent, TransformComponent>();
        while (atmospheres.MoveNext(out var atmosphere, out var transform))
        {
            if (atmosphere.HotspotTilesCount > 0 && transform.MapUid is { } occupied)
                _occupiedMaps.Add(occupied);
        }
        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { } player ||
                HasComp<GhostComponent>(player) || !TryComp(player, out TransformComponent? transform) ||
                transform.MapUid is not { } map)
                continue;

            _occupiedMaps.Add(map);
        }

        // A crew can disconnect during startup. Do not pause its ship before it leaves the orbit.
        var flights = AllEntityQuery<SWGlobalWarTravelComponent, TransformComponent>();
        while (flights.MoveNext(out _, out var transform))
        {
            if (transform.MapUid is { } map)
                _occupiedMaps.Add(map);
        }

        // Normal entity queries skip paused maps, including those we need to wake.
        var query = AllEntityQuery<SWGlobalWarPlanetMapComponent>();
        while (query.MoveNext(out var uid, out var link))
        {
            if (TerminatingOrDeleted(uid) || !TryComp<SWGlobalWarPlanetComponent>(link.Planet, out var planet) ||
                planet.GlobalWarGamerule != globalWarGamerule.Owner)
                continue;

            link.IncomingTravellers.RemoveWhere(traveller => TerminatingOrDeleted(traveller) ||
                !TryComp(traveller, out TransformComponent? transform) || transform.MapUid == uid);

            foreach (var (operation, lease) in link.Leases.ToArray())
            {
                if (TerminatingOrDeleted(lease.Owner))
                    link.Leases.Remove(operation);
            }
            if (_occupiedMaps.Contains(uid) || link.IncomingTravellers.Count > 0 || link.Leases.Count > 0)
            {
                WakeMap(uid, link);
                continue;
            }

            link.EmptySince ??= _timing.CurTime;
            if (_timing.CurTime - link.EmptySince.Value >= globalWarGamerule.Comp.EmptyMapPauseDelay)
                SleepMap(uid);
        }
    }

    private void SleepMap(EntityUid map)
    {
        _maps.SetPaused(map, true);
        if (TryComp<SWGlobalWarPlanetMapComponent>(map, out var link) &&
            TryComp<SWGlobalWarPlanetComponent>(link.Planet, out var planet))
            planet.Locations[link.Location] = SWGlobalWarLocationState.Paused;
    }

    private void WakeMap(EntityUid map, SWGlobalWarPlanetMapComponent link)
    {
        link.EmptySince = null;
        _maps.SetPaused(map, false);
        if (TryComp<SWGlobalWarPlanetComponent>(link.Planet, out var planet))
            planet.Locations[link.Location] = SWGlobalWarLocationState.Active;
    }

    private void OnPlayerAttached(Entity<ActorComponent> ent, ref PlayerAttachedEvent args)
    {
        WakePlayerMap(ent.Owner);
    }

    private void OnPlayerMoved(Entity<ActorComponent> ent, ref EntParentChangedMessage args)
    {
        WakePlayerMap(ent.Owner);
    }

    private void WakePlayerMap(EntityUid player)
    {
        if (HasComp<GhostComponent>(player) || !TryComp(player, out TransformComponent? transform) ||
            transform.MapUid is not { } map || !TryComp<SWGlobalWarPlanetMapComponent>(map, out var link))
            return;

        WakeMap(map, link);
    }

    /// <summary>
    /// Stops automatic pausing and leaves the maps available for the post-round period.
    /// The normal round reset deletes maps, stations and planet controllers together.
    /// </summary>
    public void StopPlanets(Entity<SWGlobalWarGameruleComponent> globalWarGamerule)
    {
        foreach (var uid in globalWarGamerule.Comp.PlanetEntities.Values)
        {
            if (!TryComp<SWGlobalWarPlanetComponent>(uid, out var planet))
                continue;

            foreach (var map in planet.Maps.Values)
            {
                if (TerminatingOrDeleted(map) || !HasComp<MapComponent>(map) ||
                    !TryComp<SWGlobalWarPlanetMapComponent>(map, out var link))
                    continue;

                link.IncomingTravellers.Clear();
                link.Leases.Clear();
                RemComp<SWGlobalWarGuardedMapComponent>(map);
                WakeMap(map, link);
            }

            var stopped = new SWGlobalWarPlanetStoppedEvent(uid, globalWarGamerule.Owner);
            RaiseLocalEvent(ref stopped);
        }
    }

    private void DeletePlanets(Entity<SWGlobalWarGameruleComponent> globalWarGamerule)
    {
        foreach (var uid in globalWarGamerule.Comp.PlanetEntities.Values)
        {
            if (!TryComp<SWGlobalWarPlanetComponent>(uid, out var planet))
                continue;

            foreach (var station in planet.Stations)
            {
                if (!TerminatingOrDeleted(station))
                    Del(station);
            }

            foreach (var map in planet.Maps.Values)
            {
                if (!TerminatingOrDeleted(map))
                    Del(map);
            }

            Del(uid);
        }

        globalWarGamerule.Comp.PlanetEntities.Clear();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _occupiedMaps.Clear();
        _nextActivityCheck = TimeSpan.Zero;
    }
}

/// <summary>Round-scoped handle for a single location activity reservation.</summary>
public readonly record struct SWGlobalWarLocationLease(EntityUid Map, Guid Generation, Guid Operation);
