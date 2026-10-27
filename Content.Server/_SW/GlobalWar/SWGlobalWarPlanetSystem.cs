using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Shared._SW.GlobalWar;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Maps;
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

    private const float ActivityCheckInterval = 1f;
    private float _activityAccumulator;
    private readonly HashSet<EntityUid> _occupiedMaps = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ActorComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<ActorComponent, EntParentChangedMessage>(OnPlayerMoved);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    /// <summary>
    /// Creates planet controllers and loads both locations when the rule starts, before round-start spawning.
    /// Repeated calls do not duplicate the active GlobalWarGamerule's maps.
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

            _prototypes.Index(prototype.Surface);
            _prototypes.Index(prototype.Orbit);
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
                _metadata.SetEntityName(uid, prototype.Name);
                globalWarGamerule.Comp.PlanetEntities.Add(id, uid);

                LoadLocation((uid, planet), SWGlobalWarPlanetLocation.Surface, prototype.Surface);
                LoadLocation((uid, planet), SWGlobalWarPlanetLocation.Orbit, prototype.Orbit);
            }
        }
        catch
        {
            DeletePlanets(globalWarGamerule);
            throw;
        }
    }

    private void LoadLocation(Entity<SWGlobalWarPlanetComponent> planet, SWGlobalWarPlanetLocation location,
        ProtoId<GameMapPrototype> prototype)
    {
        // Use the game-map pipeline so BecomesStation and station configuration are processed normally.
        var grids = _ticker.LoadGameMap(_prototypes.Index(prototype), out var mapId,
            offset: Vector2.Zero, rot: Angle.Zero);
        var map = _maps.GetMap(mapId);
        planet.Comp.Maps.Add(location, map);

        foreach (var grid in grids)
        {
            if (_stations.GetOwningStation(grid) is { } station)
            {
                planet.Comp.Stations.Add(station);
                planet.Comp.StationGrids.Add(grid);
            }
        }

        var link = AddComp<SWGlobalWarPlanetMapComponent>(map);
        link.Planet = planet.Owner;
        link.Location = location;
        link.EmptySince = _timing.CurTime;

        // MapInit handlers can resolve the planet through its map component.
        if (!_maps.IsInitialized(mapId))
            _maps.InitializeMap(mapId);
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
            !TryGetPlanetMap(planet.Value, location, out map))
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
        _activityAccumulator += frameTime;
        if (_activityAccumulator < ActivityCheckInterval)
            return;

        _activityAccumulator = 0f;
        if (!_globalWarGamerule.TryGetGlobalWarGamerule(out var globalWarGamerule))
            return;

        _occupiedMaps.Clear();
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

            if (_occupiedMaps.Contains(uid) || link.IncomingTravellers.Count > 0)
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
    }

    private void WakeMap(EntityUid map, SWGlobalWarPlanetMapComponent link)
    {
        link.EmptySince = null;
        _maps.SetPaused(map, false);
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
                WakeMap(map, link);
            }
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
        _activityAccumulator = 0f;
    }
}
