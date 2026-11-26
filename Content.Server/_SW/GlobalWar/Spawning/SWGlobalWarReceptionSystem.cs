using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar.Mapping;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Doors.Systems;
using Content.Server.NPC.Pathfinding;
using Content.Shared._SW.Droids;
using Content.Shared._SW.GlobalWar.Mapping;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Temperature.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server._SW.GlobalWar.Spawning;

/// <summary>Conservative, synchronous reception suitability; never heals, opens a door or consumes breathing gas.</summary>
public sealed class SWGlobalWarReceptionSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarGameruleSystem _rules = default!;
    [Dependency] private readonly SWGlobalWarMapAnchorSystem _anchors = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly BarotraumaSystem _pressure = default!;
    [Dependency] private readonly RespiratorSystem _respiration = default!;
    [Dependency] private readonly SharedDoorSystem _doors = default!;
    [Dependency] private readonly FirelockSystem _firelocks = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly PathfindingSystem _paths = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWGlobalWarReceptionComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWGlobalWarLocationReadyEvent>(OnLocationReady, after: [typeof(SWGlobalWarMapAnchorSystem)]);
    }

    private void OnMapInit(Entity<SWGlobalWarReceptionComponent> ent, ref MapInitEvent args)
    {
        BindReception(ent);
    }

    private void OnLocationReady(ref SWGlobalWarLocationReadyEvent args)
    {
        var query = AllEntityQuery<SWGlobalWarReceptionComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var reception, out var transform))
            if (transform.MapUid == args.Map)
                BindReception((uid, reception));
    }

    private void BindReception(Entity<SWGlobalWarReceptionComponent> ent)
    {
        var transform = Transform(ent);
        if (ent.Comp.Binding != null || !_rules.TryGetGlobalWarGamerule(out var rule) ||
            !_planets.TryGetPhysicalLocation(ent, out var planet, out _) ||
            transform.MapUid is not { } map || transform.GridUid is not { } grid || transform.ParentUid != grid ||
            !_planets.IsPlanetStationGrid(planet, grid) || !TryComp<PhysicsComponent>(grid, out var physics) ||
            physics.BodyType != BodyType.Static || !TryComp<MapGridComponent>(grid, out var gridData) ||
            _maps.GetTileRef(grid, gridData, transform.Coordinates).Tile.IsEmpty)
            return;
        ent.Comp.Binding = new SWGlobalWarReceptionBinding(rule.Comp.Generation, planet, map,
            transform.Coordinates, Transform(grid).Coordinates, Transform(grid).LocalRotation,
            _transform.GetWorldRotation(ent));
    }

    /// <summary>Restores only a floor-detached marker to its exact original stationary source after real floor repair.</summary>
    public bool TryRestoreAnchor(EntityUid anchor, Guid generation)
    {
        if (!TryComp<SWGlobalWarReceptionComponent>(anchor, out var reception) || reception.Binding is not { } binding ||
            binding.Generation != generation || !_rules.TryGetGlobalWarGamerule(out var rule) ||
            rule.Comp.Generation != generation || TerminatingOrDeleted(binding.Map) ||
            TerminatingOrDeleted(binding.Coordinates.EntityId) ||
            !_planets.IsPlanetStationGrid(binding.Planet, binding.Coordinates.EntityId))
            return false;
        var grid = binding.Coordinates.EntityId;
        var gridTransform = Transform(grid);
        var transform = Transform(anchor);
        if (gridTransform.MapUid != binding.Map || gridTransform.Coordinates != binding.GridCoordinates ||
            gridTransform.LocalRotation != binding.GridRotation || transform.MapUid != binding.Map ||
            !TryComp<PhysicsComponent>(grid, out var physics) || physics.BodyType != BodyType.Static ||
            !TryComp<MapGridComponent>(grid, out var gridData) ||
            _maps.GetTileRef(grid, gridData, binding.Coordinates).Tile.IsEmpty ||
            !_planets.TryGetPhysicalLocation(anchor, out var planet, out var location) || planet != binding.Planet ||
            !_anchors.TryResolveAnchor(new SWGlobalWarAnchorRef
            {
                Sector = Comp<SWGlobalWarPlanetComponent>(planet).Prototype,
                Location = location, AnchorId = "withdrawalReception",
            }, out var registered) || registered != anchor)
            return false;
        if (transform.ParentUid == grid)
            return transform.Coordinates == binding.Coordinates;
        if (transform.ParentUid != binding.Map ||
            _transform.ToMapCoordinates(transform.Coordinates) != _transform.ToMapCoordinates(binding.Coordinates) ||
            _transform.GetWorldRotation(anchor) != binding.MarkerWorldRotation)
            return false;
        _transform.SetCoordinates(anchor, binding.Coordinates);
        return true;
    }

    /// <summary>Checks a whole body footprint and a swept, bounded public route on the original stationary grid.</summary>
    public bool IsSuitable(EntityUid body, EntityUid planet, EntityUid anchor, EntityCoordinates destination,
        Box2 worldRelativeBounds, Func<Box2, bool> reserved)
    {
        var grid = destination.EntityId;
        if (!TryComp<SWGlobalWarReceptionComponent>(anchor, out var reception) ||
            !TryComp<SWGlobalWarMapAnchorComponent>(anchor, out var marker) ||
            marker.AnchorId != "withdrawalReception" || marker.Kind != SWGlobalWarAnchorKind.Spawn ||
            reception.Binding is not { } binding || binding.Coordinates.EntityId != grid ||
            binding.Coordinates != Transform(anchor).Coordinates ||
            binding.GridCoordinates != Transform(grid).Coordinates || binding.GridRotation != Transform(grid).LocalRotation ||
            marker.Links.Count != 0 || !_planets.IsPlanetStationGrid(planet, grid) ||
            Transform(anchor).ParentUid != grid || !TryComp<MapGridComponent>(grid, out var gridData) ||
            !TryComp<PhysicsComponent>(grid, out var physics) || physics.BodyType != BodyType.Static ||
            Transform(grid).MapUid is not { } map || _maps.IsPaused(map) ||
            !_planets.TryGetPhysicalLocation(anchor, out var currentPlanet, out var location) || currentPlanet != planet ||
            !_anchors.TryResolveAnchor(new SWGlobalWarAnchorRef
            {
                Sector = Comp<SWGlobalWarPlanetComponent>(planet).Prototype,
                Location = location, AnchorId = "withdrawalReception",
            }, out var registered) || registered != anchor ||
            reception.PublicExits.Count != 2 || reception.PublicExits[0] == reception.PublicExits[1] ||
            !FiniteArea(reception.PlacementArea) || !FiniteArea(reception.RouteArea) ||
            !reception.RouteArea.Contains(reception.PlacementArea))
            return false;

        var worldMatrix = _transform.GetWorldMatrix(grid);
        var inverse = _transform.GetInvWorldMatrix(grid);
        var worldStart = _transform.ToMapCoordinates(destination).Position;
        var localBounds = inverse.TransformBox(worldRelativeBounds.Translated(worldStart));
        if (!reception.PlacementArea.Contains(localBounds))
            return false;
        var localRelative = localBounds.Translated(-destination.Position);
        var doorResults = new Dictionary<EntityUid, bool>();
        var tileResults = new Dictionary<Vector2i, bool>();
        var nodeResults = new Dictionary<Vector2i, bool>();

        bool DoorAllowed(EntityUid uid, DoorComponent door)
        {
            if (doorResults.TryGetValue(uid, out var cached))
                return cached;
            var allowed = !Paused(uid) &&
                (!TryComp<AccessReaderComponent>(uid, out var reader) || _access.IsAllowed([], [], uid, reader));
            if (TryComp<FirelockComponent>(uid, out var firelock))
            {
                allowed &= !firelock.IsLocked;
            }
            var hazard = _firelocks.CheckDoorPressureAndFire(uid);
            allowed &= !hazard.Pressure && !hazard.Fire;
            // An open public passage is available now even if its motor has subsequently lost power.
            allowed = allowed && (door.State == DoorState.Open || _doors.CanOpen(uid, door, user: body, quiet: true));
            doorResults[uid] = allowed;
            return allowed;
        }

        bool TileSafe(Vector2i tile, bool closedDoor)
        {
            if (tileResults.TryGetValue(tile, out var cached))
                return cached;
            var safe = _atmos.TryGetKnownTilePressureState(grid, tile, out var pending, out var moving, out var difference, out _) &&
                !pending && (!moving || !_atmos.CanPressureMove(body, difference)) && !_atmos.IsHotspotActive(grid, tile);
            var gas = _atmos.GetTileMixture(grid, map, tile, excite: false);
            if (gas != null)
                safe &= EnvironmentFits(body, gas);
            else if (closedDoor)
            {
                // An airtight door has no tile gas. Both accessible sides must be known and suitable before opening.
                var sides = 0;
                foreach (var offset in new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) })
                {
                    var neighbor = tile + offset;
                    if (_atmos.IsTileAirBlocked(grid, neighbor))
                        continue;
                    sides++;
                    var air = _atmos.GetTileMixture(grid, map, neighbor, excite: false);
                    safe &= air != null && EnvironmentFits(body, air) &&
                        _atmos.TryGetKnownTilePressureState(grid, neighbor, out var wait, out var flow, out var delta, out _) &&
                        !wait && (!flow || !_atmos.CanPressureMove(body, delta)) && !_atmos.IsHotspotActive(grid, neighbor);
                }
                safe &= sides >= 2;
            }
            else
                safe = false;
            tileResults[tile] = safe;
            return safe;
        }

        bool AreaSafe(Box2 localArea, bool permitDoors)
        {
            if (!reception.RouteArea.Contains(localArea))
                return false;
            var worldArea = worldMatrix.TransformBox(localArea);
            if (reserved(worldArea))
                return false;
            var closedDoors = new HashSet<Vector2i>();
            foreach (var uid in _lookup.GetEntitiesIntersecting(Transform(map).MapID, worldArea,
                         LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Sundries))
            {
                if (uid == anchor || uid == grid)
                    continue;
                if (TryComp<FlammableComponent>(uid, out var flame) && flame.OnFire)
                    return false;
                if (TryComp<DoorComponent>(uid, out var door))
                {
                    if (!permitDoors || !DoorAllowed(uid, door))
                        return false;
                    if (door.State != DoorState.Open)
                        closedDoors.Add(_maps.CoordinatesToTile(grid, gridData, Transform(uid).Coordinates));
                }
                else if (TryComp<PhysicsComponent>(uid, out var obstacle) && obstacle.CanCollide &&
                         _lookup.GetAABBNoContainer(uid, _transform.GetWorldPosition(uid),
                             _transform.GetWorldRotation(uid)).Intersects(worldArea))
                    return false;
            }
            foreach (var tile in _maps.GetTilesIntersecting(grid, gridData, worldArea, ignoreEmpty: false))
                if (tile.Tile.IsEmpty || !TileSafe(tile.GridIndices, closedDoors.Contains(tile.GridIndices)))
                    return false;
            return true;
        }

        var start = _maps.CoordinatesToTile(grid, gridData, destination);
        Vector2 Center(Vector2i tile) => (new Vector2(tile.X, tile.Y) + new Vector2(.5f)) * gridData.TileSize;
        bool NodeSafe(Vector2i tile)
        {
            if (!nodeResults.TryGetValue(tile, out var result))
                nodeResults[tile] = result = AreaSafe(localRelative.Translated(Center(tile)), true);
            return result;
        }
        if (!AreaSafe(localBounds, false) || !NodeSafe(start) ||
            !AreaSafe(localBounds.Union(localRelative.Translated(Center(start))), true))
            return false;
        foreach (var exit in reception.PublicExits)
        {
            if (reception.PlacementArea.Contains(Center(exit)) || !NodeSafe(exit))
                continue;
            var path = _paths.GetPath(new PathfindingSystem.SimplePathArgs
            {
                Start = start, End = exit, Diagonals = false, Limit = 4096,
                TileCost = tile => NodeSafe(tile) ? 1 : 0,
            });
            if (path.Path == null)
                continue;
            // Cardinal movement at a fixed world rotation: union covers every intermediate footprint and turn.
            var passable = true;
            for (var i = 1; i < path.Path.Count; i++)
                passable &= AreaSafe(localRelative.Translated(Center(path.Path[i - 1]))
                    .Union(localRelative.Translated(Center(path.Path[i]))), true);
            if (passable)
                return true;
        }
        return false;
    }

    private bool EnvironmentFits(EntityUid body, GasMixture gas)
    {
        if (!float.IsFinite(gas.Temperature) || !float.IsFinite(gas.Pressure) || gas.Volume <= 0)
            return false;
        // SW droids inherit MobRobotic: no organic body/lungs, pressure damage or thermal physiology.
        // Missing organs in an organic body must not silently turn it into a supported non-breather.
        var droid = HasComp<SWDroidRepairableComponent>(body) && HasComp<BorgChassisComponent>(body) &&
                    !HasComp<BodyComponent>(body);
        if (TryComp<RespiratorComponent>(body, out var respirator))
        {
            var sample = new GasMixture(gas.Volume);
            sample.CopyFrom(gas);
            if (!_respiration.CanMetabolizeInhaledAir((body, respirator), sample.RemoveVolume(respirator.BreathVolume)))
                return false;
        }
        else if (!droid)
            return false;
        if (TryComp<BarotraumaComponent>(body, out var pressure) &&
            (_pressure.GetFeltLowPressure(body, pressure, gas.Pressure) <= Atmospherics.HazardLowPressure ||
             _pressure.GetFeltHighPressure(body, pressure, gas.Pressure) >= Atmospherics.HazardHighPressure))
            return false;
        if (TryComp<TemperatureComponent>(body, out var temperature) &&
            (gas.Temperature <= temperature.ColdDamageThreshold || gas.Temperature >= temperature.HeatDamageThreshold))
            return false;
        return droid || HasComp<BarotraumaComponent>(body) && HasComp<TemperatureComponent>(body);
    }

    private static bool FiniteArea(Box2 area)
    {
        return float.IsFinite(area.Left) && float.IsFinite(area.Right) && float.IsFinite(area.Bottom) &&
               float.IsFinite(area.Top) && area.Width > 0 && area.Height > 0 && area.Width * area.Height <= 4096;
    }
}
