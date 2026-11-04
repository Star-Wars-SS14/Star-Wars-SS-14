using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar.Identity;
using Content.Server._SW.GlobalWar.Mapping;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Systems;
using Content.Server.Spawners.EntitySystems;
using Content.Server.Station.Systems;
using Content.Server.Station.Events;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Maps;
using Content.Shared._SW.GlobalWar.Mapping;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar.Spawning;

/// <summary>Prepares campaign destinations while retaining the normal GameTicker character creation pipeline.</summary>
public sealed partial class SWGlobalWarSpawnSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarGameruleSystem _rules = default!;
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarMapAnchorSystem _anchors = default!;
    [Dependency] private readonly SWGlobalWarIdentitySystem _identity = default!;
    [Dependency] private readonly StationSystem _stations = default!;
    [Dependency] private readonly StationJobsSystem _jobs = default!;
    [Dependency] private readonly StationSpawningSystem _spawning = default!;
    [Dependency] private readonly SharedMindSystem _minds = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<SWGlobalWarFaction, EntityUid> _roleStations = new();
    private readonly Dictionary<Guid, SWGlobalWarSpawnPlan> _reservations = new();
    private readonly Dictionary<EntityUid, SWGlobalWarSpawnPlan> _spawned = new();
    private readonly Dictionary<NetUserId, SWGlobalWarLife> _lives = new();
    private Guid _generation;
    private EntityUid? _rule;
    private SWGlobalWarSpawnReservation? _prepared;

    public override void Initialize()
    {
        SubscribeLocalEvent<RulePlayerSpawningEvent>(OnRoundSpawning);
        SubscribeLocalEvent<SWGlobalWarBeforeSpawnEvent>(OnBeforeSpawn);
        SubscribeLocalEvent<SWGlobalWarValidateSpawnEvent>(OnValidateSpawn);
        SubscribeLocalEvent<SWGlobalWarFilterJobStationsEvent>(OnFilterStations);
        SubscribeLocalEvent<SWGlobalWarJobAllocationEvent>(OnJobAllocation);
        SubscribeLocalEvent<StationJobsGetCandidatesEvent>(OnJobCandidates);
        InitializeAdmission();
        InitializeRespawn();
        SubscribeLocalEvent<PlayerSpawningEvent>(OnSpawning,
            before: [typeof(ContainerSpawnPointSystem), typeof(ArrivalsSystem), typeof(SpawnPointSystem)]);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<SWGlobalWarPlanetStoppedEvent>(OnStopped);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            ClearPreparation();
            _lives.Clear();
            _lastRespawnStates.Clear();
            _pendingJoins.Clear();
        });
    }

    public override void Update(float frameTime)
    {
        // A later pre-spawn subscriber can cancel the request. Successful creation consumes this in the same tick.
        ReleasePrepared();
        FlushPendingJoins();
        SyncRespawnStates();
    }

    private bool EnsureStations(out Entity<SWGlobalWarGameruleComponent> rule,
        out SWGlobalWarSpawnConfigurationComponent configuration)
    {
        configuration = default!;
        if (!_rules.TryGetGlobalWarGamerule(out rule) ||
            !TryComp(rule.Owner, out configuration!))
            return false;
        if (_generation == rule.Comp.Generation)
            return true;
        ClearPreparation();
        _generation = rule.Comp.Generation;
        _rule = rule.Owner;
        foreach (var (faction, station) in configuration.Stations)
            _roleStations.Add(faction, _stations.InitializeNewStation(station, null));
        var ready = new SWGlobalWarRoleStationsReadyEvent(rule.Owner, _generation);
        RaiseLocalEvent(ref ready);
        return true;
    }

    private void OnRoundSpawning(RulePlayerSpawningEvent args)
    {
        if (!EnsureStations(out var rule, out var configuration))
            return;
        // Only initial destinations are warmed; job assignment still receives every ready player and profile.
        var forceFactions = args.Profiles.Values.SelectMany(p => p.JobPriorities.Keys)
            .Where(j => configuration.Roles.TryGetValue(j, out var role) && role.Force)
            .Select(j => configuration.Roles[j].Faction).ToHashSet();
        foreach (var (id, planet) in rule.Comp.PlanetEntities)
        {
            var prototype = _prototypes.Index(id);
            // The base supply program runs on real lines with nobody visiting, so home farms and industry must exist
            // from the start. Only their surfaces: idle maps sleep and a production lease wakes them.
            if (prototype.Kind is SWGlobalWarSectorKind.Agriculture or SWGlobalWarSectorKind.Industry &&
                prototype.InitialOwner is SWGlobalWarFaction.Republic or SWGlobalWarFaction.Empire)
            {
                WarmLocation(planet, id, prototype.Surface, SWGlobalWarPlanetLocation.Surface);
                continue;
            }
            if (prototype.Kind != SWGlobalWarSectorKind.Capital &&
                !(prototype.Kind == SWGlobalWarSectorKind.Force && forceFactions.Contains(prototype.InitialOwner)))
                continue;
            WarmLocation(planet, id, prototype.Surface, SWGlobalWarPlanetLocation.Surface);
            WarmLocation(planet, id, prototype.Orbit, SWGlobalWarPlanetLocation.Orbit);
        }
    }

    private void WarmLocation(EntityUid planet, ProtoId<SWGlobalWarPlanetPrototype> id, ProtoId<GameMapPrototype>? map,
        SWGlobalWarPlanetLocation location)
    {
        if (map != null && !_planets.TryEnsureLocation(planet, location, out _, out var failure))
            Log.Error($"Could not load {location} of {id} at round start: {failure}");
    }

    private void OnFilterStations(ref SWGlobalWarFilterJobStationsEvent args)
    {
        if (!_rules.TryGetGlobalWarGamerule(out _))
            return;
        args.Stations.Clear();
        if (EnsureStations(out _, out _))
            args.Stations.AddRange(_roleStations.Values.Where(s => !TerminatingOrDeleted(s)));
    }

    private void OnJobCandidates(ref StationJobsGetCandidatesEvent args)
    {
        if (!_rules.TryGetGlobalWarGamerule(out _))
            return;
        if (!EnsureStations(out _, out var configuration))
        {
            args.Jobs.Clear();
            return;
        }
        var user = args.Player;
        args.Jobs.RemoveAll(job => !configuration.Roles.TryGetValue(job, out var role) ||
            !_identity.CanJoin(user, role.Faction));
    }

    private void OnJobAllocation(ref SWGlobalWarJobAllocationEvent args)
    {
        if (EnsureStations(out _, out _) && args.Stations.Count > 0 &&
            args.Stations.All(s => _roleStations.ContainsValue(s)))
            args.BalanceStations = false;
    }

    /// <summary>Returns a trusted role station without deriving it from a player's physical location.</summary>
    public bool TryGetRoleStation(SWGlobalWarFaction faction, out EntityUid station)
    {
        station = default;
        return EnsureStations(out _, out _) && _roleStations.TryGetValue(faction, out station);
    }

    /// <summary>Selects a prepared surface point in capital, industry, then another fully owned sector.</summary>
    public bool TryResolveSpawn(ICommonSession session, ProtoId<JobPrototype> requestedRole,
        out SWGlobalWarSpawnPlan? plan, out string reason)
    {
        plan = null;
        reason = Loc.GetString("sw-global-war-spawn-unavailable");
        if (!EnsureStations(out var rule, out var configuration) ||
            !configuration.Roles.TryGetValue(requestedRole, out var role))
            return false;
        if (!_identity.CanJoin(session.UserId, role.Faction))
        {
            reason = Loc.GetString("sw-global-war-spawn-faction-locked");
            return false;
        }
        if (!_roleStations.TryGetValue(role.Faction, out var station) ||
            !_jobs.TryGetJobSlot(station, requestedRole, out var slots) || slots == 0)
        {
            reason = Loc.GetString("sw-global-war-spawn-no-slots");
            return false;
        }
        // Only the respawn transaction may replace a recorded life; lobby entry never does, except for an admin.
        if ((_respawn is not { } ticket || ticket.User != session.UserId || ticket.Job != requestedRole) &&
            !MayJoinOverLife(session))
        {
            if (_lives.TryGetValue(session.UserId, out var life))
            {
                reason = LifeRefusal(life, configuration);
                return false;
            }
            if (_minds.TryGetMind(session.UserId, out var mind) && mind.Value.Comp.OwnedEntity is { } body &&
                !TerminatingOrDeleted(body) && !HasComp<GhostComponent>(body))
            {
                reason = Loc.GetString("sw-global-war-spawn-life-active");
                return false;
            }
        }
        if (!TryPickPoint(rule, role, out var point, out reason))
            return false;
        plan = new SWGlobalWarSpawnPlan(session.UserId, rule.Comp.Generation, requestedRole, role.Faction,
            station, point.Planet, Comp<SWGlobalWarPlanetComponent>(point.Planet).OwnershipRevision, point.Map,
            point.Anchor, Transform(point.Anchor).Coordinates, point.Coordinates);
        return true;
    }

    /// <summary>Finds a free cell on a fully owned world without reserving it; never falls back to an enemy map.</summary>
    private bool TryPickPoint(Entity<SWGlobalWarGameruleComponent> rule, SWGlobalWarSpawnRole role,
        out (EntityUid Planet, EntityUid Map, EntityUid Anchor, EntityCoordinates Coordinates) point, out string reason)
    {
        point = default;
        var candidates = rule.Comp.PlanetEntities
            .Where(p => FullyOwned(p.Value, role.Faction))
            .Where(p => !role.Force || _prototypes.Index(p.Key).Kind == SWGlobalWarSectorKind.Force &&
                _prototypes.Index(p.Key).InitialOwner == role.Faction)
            .OrderBy(p => _prototypes.Index(p.Key).Kind == SWGlobalWarSectorKind.Capital &&
                    _prototypes.Index(p.Key).InitialOwner == role.Faction ? 0 :
                _prototypes.Index(p.Key).Kind == SWGlobalWarSectorKind.Industry ? 1 : 2)
            .ThenBy(p => p.Key.Id, StringComparer.Ordinal)
            .ToArray();
        reason = Loc.GetString(role.Force && candidates.Length == 0
            ? "sw-global-war-spawn-force-world-lost" : "sw-global-war-spawn-no-point");
        foreach (var (id, planet) in candidates)
        {
            if (!_planets.TryEnsureLocation(planet, SWGlobalWarPlanetLocation.Surface, out var map, out _))
                continue;
            var anchorRef = new SWGlobalWarAnchorRef
            {
                Sector = id,
                Location = SWGlobalWarPlanetLocation.Surface,
                AnchorId = role.Force ? "arrivalForce" : "arrivalCivilian",
            };
            if (!_anchors.TryResolveAnchor(anchorRef, out var anchor) ||
                Comp<SWGlobalWarMapAnchorComponent>(anchor.Value).Kind != SWGlobalWarAnchorKind.Spawn ||
                !TryFindPoint(anchor.Value, out var coordinates))
                continue;
            point = (planet, map!.Value, anchor.Value, coordinates);
            reason = string.Empty;
            return true;
        }
        return false;
    }

    /// <summary>Rechecks the selected point and reserves it until synchronous character creation finishes.</summary>
    private bool TryPrepareSpawn(SWGlobalWarSpawnPlan plan, out SWGlobalWarSpawnReservation? reservation, out string reason)
    {
        reservation = null;
        reason = Loc.GetString("sw-global-war-spawn-unavailable");
        if (!_rules.TryGetGlobalWarGamerule(out var rule) || rule.Comp.Generation != plan.Generation ||
            !TryComp<SWGlobalWarSpawnConfigurationComponent>(rule.Owner, out var configuration) ||
            !configuration.Roles.TryGetValue(plan.Job, out var role) || role.Faction != plan.Faction ||
            !_identity.CanJoin(plan.User, plan.Faction) || !FullyOwned(plan.Planet, plan.Faction) ||
            Comp<SWGlobalWarPlanetComponent>(plan.Planet).GlobalWarGamerule != rule.Owner ||
            Comp<SWGlobalWarPlanetComponent>(plan.Planet).OwnershipRevision != plan.OwnershipRevision ||
            !_planets.TryGetPlanetMap(plan.Planet, SWGlobalWarPlanetLocation.Surface, out var map) || map != plan.Map ||
            !_roleStations.TryGetValue(plan.Faction, out var station) || station != plan.Station ||
            !_jobs.TryGetJobSlot(station, plan.Job, out var slots) || slots == 0 ||
            TerminatingOrDeleted(plan.Anchor) || Transform(plan.Anchor).MapUid != plan.Map ||
            Transform(plan.Anchor).Coordinates != plan.AnchorCoordinates ||
            !PointInsideZone(plan.Anchor, plan.Coordinates) || !PointIsFree(plan.Anchor, plan.Coordinates) ||
            IsReserved(plan.Coordinates))
            return false;
        var prototype = _prototypes.Index(Comp<SWGlobalWarPlanetComponent>(plan.Planet).Prototype);
        if (role.Force && (prototype.Kind != SWGlobalWarSectorKind.Force || prototype.InitialOwner != role.Faction))
            return false;
        var reference = new SWGlobalWarAnchorRef
        {
            Sector = prototype.ID,
            Location = SWGlobalWarPlanetLocation.Surface,
            AnchorId = role.Force ? "arrivalForce" : "arrivalCivilian",
        };
        if (!_anchors.TryResolveAnchor(reference, out var anchor) || anchor != plan.Anchor ||
            Comp<SWGlobalWarMapAnchorComponent>(plan.Anchor).Kind != SWGlobalWarAnchorKind.Spawn)
            return false;
        var token = Guid.NewGuid();
        _reservations.Add(token, plan);
        reservation = new SWGlobalWarSpawnReservation(token, plan);
        reason = string.Empty;
        return true;
    }

    private bool FullyOwned(EntityUid planet, SWGlobalWarFaction faction)
    {
        return TryComp<SWGlobalWarPlanetComponent>(planet, out var data) && data.GlobalWarGamerule == _rule &&
               data.SurfaceOwner == faction && data.OrbitOwner == faction;
    }

    private bool TryFindPoint(EntityUid anchor, out EntityCoordinates coordinates)
    {
        coordinates = default;
        var radius = Comp<SWGlobalWarMapAnchorComponent>(anchor).SpawnRadius;
        if (radius is < 0 or > 8)
            return false;
        var offsets = new List<Vector2i>();
        for (var x = -radius; x <= radius; x++)
        for (var y = -radius; y <= radius; y++)
            offsets.Add(new Vector2i(x, y));
        foreach (var offset in offsets.OrderBy(v => v.X * v.X + v.Y * v.Y).ThenBy(v => v.X).ThenBy(v => v.Y))
        {
            var candidate = Transform(anchor).Coordinates.Offset(new Vector2(offset.X, offset.Y));
            if (!PointInsideZone(anchor, candidate) || !PointIsFree(anchor, candidate) || IsReserved(candidate))
                continue;
            coordinates = candidate;
            return true;
        }
        return false;
    }

    private bool PointInsideZone(EntityUid anchor, EntityCoordinates coordinates)
    {
        var transform = Transform(anchor);
        var radius = Comp<SWGlobalWarMapAnchorComponent>(anchor).SpawnRadius;
        var offset = coordinates.Position - transform.Coordinates.Position;
        return radius is >= 0 and <= 8 && transform.GridUid == coordinates.EntityId &&
               transform.Coordinates.EntityId == coordinates.EntityId &&
               Math.Abs(offset.X) <= radius && Math.Abs(offset.Y) <= radius;
    }

    private bool IsReserved(EntityCoordinates coordinates)
    {
        return IsReturnReserved(coordinates) || _reservations.Values.Any(p => p.Coordinates.EntityId == coordinates.EntityId &&
            Vector2.DistanceSquared(p.Coordinates.Position, coordinates.Position) < 1f);
    }

    private bool PointIsFree(EntityUid anchor, EntityCoordinates coordinates)
    {
        var transform = Transform(anchor);
        if (!TryComp<MapGridComponent>(transform.GridUid, out var grid) ||
            _maps.GetTileRef(transform.GridUid!.Value, grid, coordinates).Tile.IsEmpty)
            return false;
        var position = _transform.ToMapCoordinates(coordinates);
        return !_lookup.AnyEntitiesIntersecting(position.MapId,
            Box2.CenteredAround(position.Position, Vector2.One), LookupFlags.Static | LookupFlags.Dynamic);
    }

    private void OnBeforeSpawn(ref SWGlobalWarBeforeSpawnEvent args)
    {
        if (!_rules.TryGetGlobalWarGamerule(out _) || args.Cancelled)
            return;
        ReleasePrepared();
        if (!TryResolveSpawn(args.Player, args.Job, out var plan, out var reason) ||
            plan!.Station != args.Station || !TryPrepareSpawn(plan, out _prepared, out reason))
        {
            args.Cancelled = true;
            args.Reason = reason;
        }
    }

    private void OnSpawning(PlayerSpawningEvent args)
    {
        if (!_rules.TryGetGlobalWarGamerule(out var rule))
            return;
        // There is no asynchronous work between pre-DoSpawn preparation and this handler.
        // Never fall through to SpawnPointSystem's arbitrary-map fallback in this mode.
        if (_prepared is not { } prepared || prepared.Plan.Generation != rule.Comp.Generation ||
            prepared.Plan.Station != args.Station || prepared.Plan.Job != args.Job ||
            !_reservations.ContainsKey(prepared.Token))
            throw new InvalidOperationException("GlobalWar character creation requires a prepared GameTicker spawn.");
        try
        {
            var body = _spawning.SpawnPlayerMob(prepared.Plan.Coordinates, args.Job,
                args.HumanoidCharacterProfile, args.Station);
            args.SpawnResult = body;
            _spawned.Add(body, prepared.Plan);
        }
        finally
        {
            ReleasePrepared();
        }
    }

    private void OnValidateSpawn(ref SWGlobalWarValidateSpawnEvent args)
    {
        if (!_rules.TryGetGlobalWarGamerule(out _))
            return;
        var prepared = _prepared;
        ReleasePrepared();
        if (args.Cancelled)
            return;
        var reason = Loc.GetString("sw-global-war-spawn-no-point");
        if (prepared == null || prepared.Plan.User != args.Player.UserId || prepared.Plan.Job != args.Job ||
            prepared.Plan.Station != args.Station || !TryPrepareSpawn(prepared.Plan, out _prepared, out reason))
        {
            args.Cancelled = true;
            args.Reason = reason;
        }
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (!_spawned.Remove(args.Mob, out var plan) || plan.User != args.Player.UserId ||
            !_minds.TryGetMind(plan.User, out var mind) || mind.Value.Comp.OwnedEntity != args.Mob)
            return;
        if (!_identity.TryEnroll(mind.Value.Owner, plan.Faction, plan.Generation))
            throw new InvalidOperationException("Prepared GlobalWar identity became invalid during synchronous spawn.");
        _lives[plan.User] = new SWGlobalWarLife(Guid.NewGuid(), args.Mob, mind.Value.Owner, plan.Faction);
        EnsureComp<SWGlobalWarLifeComponent>(args.Mob).User = plan.User;
        ConfirmEntry(args.Player, plan);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        foreach (var (user, life) in _lives)
        {
            if (life.Body != args.Target)
                continue;
            life.Death = args.NewMobState == MobState.Dead ? life.Death ?? _timing.CurTime : null;
            SendRespawnState(user, life);
            break;
        }
    }

    private void OnStopped(ref SWGlobalWarPlanetStoppedEvent args)
    {
        if (args.Rule == _rule)
            ClearPreparation();
    }

    private void ReleasePrepared()
    {
        if (_prepared is { } prepared)
            _reservations.Remove(prepared.Token);
        _prepared = null;
    }

    private void ClearPreparation()
    {
        ReleasePrepared();
        _reservations.Clear();
        foreach (var plan in _returnReservations.Values)
            _planets.ReleaseLocationLease(plan.Lease);
        _returnReservations.Clear();
        _spawned.Clear();
        foreach (var station in _roleStations.Values)
        {
            if (!TerminatingOrDeleted(station))
                _stations.DeleteStation(station);
        }
        _roleStations.Clear();
        _generation = Guid.Empty;
        _rule = null;
    }
}

public sealed record SWGlobalWarSpawnPlan(NetUserId User, Guid Generation, ProtoId<JobPrototype> Job,
    SWGlobalWarFaction Faction, EntityUid Station, EntityUid Planet, long OwnershipRevision, EntityUid Map,
    EntityUid Anchor, EntityCoordinates AnchorCoordinates, EntityCoordinates Coordinates);

public sealed record SWGlobalWarSpawnReservation(Guid Token, SWGlobalWarSpawnPlan Plan);

/// <summary>One player's current body. Replaced only by a committed respawn, and never reused for a new body.</summary>
public sealed class SWGlobalWarLife(Guid id, EntityUid body, EntityUid mind, SWGlobalWarFaction faction)
{
    public readonly Guid Id = id;
    public readonly EntityUid Body = body;
    public readonly EntityUid Mind = mind;
    public readonly SWGlobalWarFaction Faction = faction;

    /// <summary>Start of the current uninterrupted death; revival clears it, so a new death starts a new term.</summary>
    public TimeSpan? Death;
}
