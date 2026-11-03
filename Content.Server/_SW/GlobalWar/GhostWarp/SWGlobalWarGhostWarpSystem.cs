using Content.Server.Administration.Managers;
using Content.Server.Ghost;
using Content.Server.Popups;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Administration;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Warps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.GhostWarp;

/// <summary>
/// Lists every surface and orbit of the campaign in the ghost warp menu, loaded or not. A ghost that warps to one is
/// placed at its planetary teleporter and keeps the map awake while it stays there, so it can watch and move.
/// </summary>
public sealed class SWGlobalWarGhostWarpSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarGameruleSystem _rules = default!;
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IAdminManager _admins = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private const string LeaseReason = "ghost-warp";

    private readonly Dictionary<(EntityUid Planet, SWGlobalWarPlanetLocation Location), EntityUid> _beacons = new();
    private readonly Dictionary<EntityUid, SWGlobalWarLocationLease> _watchers = new();
    private readonly List<EntityUid> _left = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<GhostWarpsRequestEvent>(OnWarpsRequest, before: [typeof(GhostSystem)]);
        SubscribeNetworkEvent<GhostWarpToTargetRequestEvent>(OnWarpRequest, before: [typeof(GhostSystem)]);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _beacons.Clear();
            _watchers.Clear();
        });
    }

    private void OnWarpsRequest(GhostWarpsRequestEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { } ghost && HasComp<GhostComponent>(ghost))
            EnsureBeacons();
    }

    /// <summary>Puts the beacon on its map before the ghost system moves the ghost to it.</summary>
    private void OnWarpRequest(GhostWarpToTargetRequestEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } ghost || !HasComp<GhostComponent>(ghost) ||
            !TryGetEntity(msg.Target, out var beacon) || !TryComp<SWGlobalWarGhostWarpComponent>(beacon, out var warp))
            return;

        // A location nobody has visited yet costs a whole map load, so only an admin may start one.
        var loaded = _planets.TryGetPlanetMap(warp.Planet, warp.Location, out _);
        if (!loaded && !_admins.HasAdminFlag(args.SenderSession, AdminFlags.Admin) ||
            !TryWatch(ghost, warp.Planet, warp.Location, out var map))
        {
            // The ghost system still warps to the beacon, so leave it where the ghost already is.
            _transform.SetCoordinates(beacon.Value, Transform(ghost).Coordinates);
            _popup.PopupEntity(Loc.GetString(loaded ? "sw-global-war-ghost-warp-failed" : "sw-global-war-ghost-warp-unloaded"),
                ghost, ghost);
            return;
        }

        _transform.SetCoordinates(beacon.Value, ArrivalPoint(map));
    }

    private bool TryWatch(EntityUid ghost, EntityUid planet, SWGlobalWarPlanetLocation location, out EntityUid map)
    {
        map = default;
        Release(ghost);
        if (!_planets.TryAcquireLocationLease(planet, location, ghost, Guid.NewGuid(), LeaseReason, out var lease))
            return false;
        _watchers[ghost] = lease;
        map = lease.Map;
        return true;
    }

    private void Release(EntityUid ghost)
    {
        if (_watchers.Remove(ghost, out var lease))
            _planets.ReleaseLocationLease(lease);
    }

    /// <summary>The planetary teleporter of the location, else the middle of its largest grid.</summary>
    private EntityCoordinates ArrivalPoint(EntityUid map)
    {
        var teleporters = AllEntityQuery<SWGlobalWarPlanetTeleporterComponent, TransformComponent>();
        while (teleporters.MoveNext(out _, out var transform))
        {
            if (transform.MapUid == map)
                return transform.Coordinates;
        }

        EntityUid? best = null;
        var size = -1f;
        var grids = AllEntityQuery<MapGridComponent, TransformComponent>();
        while (grids.MoveNext(out var uid, out var grid, out var transform))
        {
            if (transform.MapUid != map || grid.LocalAABB.MaxDimension <= size)
                continue;
            size = grid.LocalAABB.MaxDimension;
            best = uid;
        }

        return best is { } found
            ? new EntityCoordinates(found, Comp<MapGridComponent>(found).LocalAABB.Center)
            : new EntityCoordinates(map, default);
    }

    /// <summary>One beacon per surface and orbit of the active rule; a destroyed map takes its beacon with it.</summary>
    private void EnsureBeacons()
    {
        if (!_rules.TryGetGlobalWarGamerule(out var rule))
            return;

        foreach (var (id, planet) in rule.Comp.PlanetEntities)
        {
            var prototype = _prototypes.Index(id);
            foreach (var location in new[] { SWGlobalWarPlanetLocation.Surface, SWGlobalWarPlanetLocation.Orbit })
            {
                if ((location == SWGlobalWarPlanetLocation.Surface ? prototype.Surface : prototype.Orbit) == null ||
                    _beacons.TryGetValue((planet, location), out var existing) && !TerminatingOrDeleted(existing))
                    continue;

                var beacon = Spawn(null, MapCoordinates.Nullspace);
                var warp = AddComp<SWGlobalWarGhostWarpComponent>(beacon);
                warp.Planet = planet;
                warp.Location = location;
                AddComp<WarpPointComponent>(beacon).Location = Loc.GetString(
                    location == SWGlobalWarPlanetLocation.Surface
                        ? "sw-global-war-ghost-warp-surface"
                        : "sw-global-war-ghost-warp-orbit",
                    ("planet", Name(planet)));
                _beacons[(planet, location)] = beacon;
            }
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_watchers.Count == 0)
            return;

        // A ghost holds its location only while it stays on that map.
        foreach (var (ghost, lease) in _watchers)
        {
            if (TerminatingOrDeleted(ghost) || !HasComp<GhostComponent>(ghost) || Transform(ghost).MapUid != lease.Map)
                _left.Add(ghost);
        }
        foreach (var ghost in _left)
            Release(ghost);
        _left.Clear();
    }
}
