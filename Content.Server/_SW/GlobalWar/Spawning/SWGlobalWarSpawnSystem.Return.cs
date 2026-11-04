using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar.Mapping;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Mapping;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._SW.GlobalWar.Spawning;

public sealed partial class SWGlobalWarSpawnSystem
{
    [Dependency] private readonly SWGlobalWarReceptionSystem _reception = default!;
    private readonly Dictionary<Guid, SWGlobalWarReturnPlan> _returnReservations = new();

    /// <summary>
    /// Reserves a real prepared surface cell for an existing body, without spending a job slot or creating equipment.
    /// The caller must validate again immediately before detaching any carrier and release the reservation afterwards.
    /// </summary>
    public bool TryPrepareBodyReturn(EntityUid body, Guid generation, out SWGlobalWarReturnPlan plan,
        EntityUid? localPlanet = null, SWGlobalWarPlanetLocation localLocation = SWGlobalWarPlanetLocation.Surface)
    {
        plan = default!;
        if (!_rules.TryGetGlobalWarGamerule(out var rule) || rule.Comp.Generation != generation ||
            !_identity.TryGetOwnedBodyIdentity(body, out var mind, out var faction))
            return false;
        if (localPlanet != null && (!_planets.TryGetPhysicalLocation(body, out var currentPlanet, out var currentLocation) ||
                                    currentPlanet != localPlanet || currentLocation != localLocation))
            return false;
        foreach (var stale in _returnReservations.Where(p => p.Value.Generation != generation).Select(p => p.Key).ToArray())
            _returnReservations.Remove(stale);
        var bounds = BodyReturnBounds(body);
        var candidates = rule.Comp.PlanetEntities.Where(p => TryComp<SWGlobalWarPlanetComponent>(p.Value, out var planet) &&
                (localPlanet != null ? p.Value == localPlanet : planet.SurfaceOwner == faction && planet.OrbitOwner == faction))
            .OrderBy(p => _prototypes.Index(p.Key).Kind == SWGlobalWarSectorKind.Capital &&
                    _prototypes.Index(p.Key).InitialOwner == faction ? 0 :
                _prototypes.Index(p.Key).Kind == SWGlobalWarSectorKind.Industry ? 1 : 2)
            .ThenBy(p => p.Key.Id, StringComparer.Ordinal);
        foreach (var (id, planet) in candidates)
        {
            var location = localPlanet != null ? localLocation : SWGlobalWarPlanetLocation.Surface;
            if (!_planets.TryEnsureLocation(planet, location, out var map, out _) ||
                !_anchors.TryResolveAnchor(new SWGlobalWarAnchorRef
                {
                    Sector = id, Location = location,
                    AnchorId = localPlanet != null ? "withdrawalReception" : "arrivalCivilian",
                }, out var anchor) || Comp<SWGlobalWarMapAnchorComponent>(anchor.Value).Kind != SWGlobalWarAnchorKind.Spawn)
                continue;
            if (localPlanet != null && !_reception.TryRestoreAnchor(anchor.Value, generation))
                continue;
            var radius = Comp<SWGlobalWarMapAnchorComponent>(anchor.Value).SpawnRadius;
            if (radius is < 0 or > 8)
                continue;
            for (var x = -radius; x <= radius; x++)
            for (var y = -radius; y <= radius; y++)
            {
                var coordinates = Transform(anchor.Value).Coordinates.Offset(new Vector2(x, y));
                if (!PointInsideZone(anchor.Value, coordinates) || !PointIsFree(anchor.Value, coordinates) ||
                    IsReserved(coordinates) || !ReturnAreaFree(coordinates, bounds) ||
                    localPlanet != null && !_reception.IsSuitable(body, planet, anchor.Value, coordinates, bounds,
                        area => ReturnReservationIntersects(map.Value, area)))
                    continue;
                var operation = Guid.NewGuid();
                if (!_planets.TryAcquireLocationLease(planet, location, body, operation, "withdrawal-return", out var lease))
                    continue;
                plan = new SWGlobalWarReturnPlan(operation, generation, body, mind, faction, planet,
                    Comp<SWGlobalWarPlanetComponent>(planet).OwnershipRevision, map.Value, anchor.Value,
                    Transform(anchor.Value).Coordinates, coordinates, bounds,
                    -_transform.GetWorldRotation(coordinates.EntityId), localPlanet != null, lease);
                _returnReservations.Add(plan.Id, plan);
                return true;
            }
        }
        return false;
    }

    /// <summary>Rechecks ownership, exact body identity, anchor and full footprint before a prepared return commits.</summary>
    public bool ValidateBodyReturn(SWGlobalWarReturnPlan plan)
    {
        return _returnReservations.TryGetValue(plan.Id, out var current) && current == plan &&
               _rules.TryGetGlobalWarGamerule(out var rule) && rule.Comp.Generation == plan.Generation &&
               _identity.TryGetOwnedBodyIdentity(plan.Body, out var mind, out var faction) &&
               mind == plan.Mind && faction == plan.Faction &&
               TryComp<SWGlobalWarPlanetComponent>(plan.Planet, out var planet) && planet.GlobalWarGamerule == rule.Owner &&
               (plan.LocalDisembark ? Transform(plan.Body).MapUid == plan.Map :
                   planet.SurfaceOwner == faction && planet.OrbitOwner == faction) &&
               planet.OwnershipRevision == plan.OwnershipRevision &&
               !TerminatingOrDeleted(plan.Anchor) && Transform(plan.Anchor).Coordinates == plan.AnchorCoordinates &&
               Transform(plan.Anchor).MapUid == plan.Map && PointInsideZone(plan.Anchor, plan.Coordinates) &&
               PointIsFree(plan.Anchor, plan.Coordinates) && BodyReturnBounds(plan.Body) == plan.Bounds &&
               -_transform.GetWorldRotation(plan.Coordinates.EntityId) == plan.Rotation &&
               ReturnAreaFree(plan.Coordinates, plan.Bounds, plan.Id) &&
               (!plan.LocalDisembark || _reception.IsSuitable(plan.Body, plan.Planet, plan.Anchor,
                   plan.Coordinates, plan.Bounds, area => ReturnReservationIntersects(plan.Map, area, plan.Id)));
    }

    /// <summary>Releases only this exact generation's spatial reservation.</summary>
    public void ReleaseBodyReturn(SWGlobalWarReturnPlan plan)
    {
        if (_returnReservations.TryGetValue(plan.Id, out var current) && current == plan)
        {
            _returnReservations.Remove(plan.Id);
            _planets.ReleaseLocationLease(plan.Lease);
        }
    }

    private bool ReturnReservationIntersects(EntityUid map, Box2 bounds, Guid except = default)
    {
        return _returnReservations.Values.Any(p => p.Id != except && p.Map == map &&
            p.Bounds.Translated(_transform.ToMapCoordinates(p.Coordinates).Position).Intersects(bounds));
    }

    private Box2 BodyReturnBounds(EntityUid body)
    {
        // Commit fixes world rotation to zero. Preserve fixture offsets, not just the width of a centered box.
        var actual = _lookup.GetAABBNoContainer(body, Vector2.Zero, Angle.Zero);
        // Use the actual fixture rather than imposing a one-tile-wide body that cannot pass ordinary doors.
        return actual.Width > 0 && actual.Height > 0 ? actual.Enlarged(.05f) :
            Box2.CenteredAround(Vector2.Zero, Vector2.One).Enlarged(.05f);
    }

    private bool ReturnAreaFree(EntityCoordinates coordinates, Box2 relativeBounds, Guid except = default)
    {
        var map = _transform.ToMapCoordinates(coordinates);
        var bounds = relativeBounds.Translated(map.Position);
        var grid = coordinates.EntityId;
        if (!TryComp<MapGridComponent>(grid, out var gridData))
            return false;
        foreach (var tile in _maps.GetTilesIntersecting(grid, gridData, bounds, ignoreEmpty: false))
            if (tile.Tile.IsEmpty)
                return false;
        return !_lookup.AnyEntitiesIntersecting(map.MapId, bounds, LookupFlags.Static | LookupFlags.Dynamic) &&
               !_returnReservations.Values.Any(p => p.Id != except && Transform(p.Map).MapID == map.MapId &&
                   p.Bounds.Translated(_transform.ToMapCoordinates(p.Coordinates).Position).Intersects(bounds));
    }

    private bool IsReturnReserved(EntityCoordinates coordinates)
    {
        var map = _transform.ToMapCoordinates(coordinates);
        return _returnReservations.Values.Any(p => p.Generation == _generation &&
            !TerminatingOrDeleted(p.Map) && Transform(p.Map).MapID == map.MapId &&
            p.Bounds.Translated(_transform.ToMapCoordinates(p.Coordinates).Position)
                .Intersects(Box2.CenteredAround(map.Position, Vector2.One)));
    }
}

/// <summary>Server-only reservation of a complete footprint, not a replacement life or a client teleport destination.</summary>
public sealed record SWGlobalWarReturnPlan(Guid Id, Guid Generation, EntityUid Body, EntityUid Mind,
    SWGlobalWarFaction Faction, EntityUid Planet, long OwnershipRevision, EntityUid Map, EntityUid Anchor,
    EntityCoordinates AnchorCoordinates, EntityCoordinates Coordinates, Box2 Bounds, Angle Rotation, bool LocalDisembark,
    SWGlobalWarLocationLease Lease);
