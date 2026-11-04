using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar;

public sealed partial class SWGlobalWarTravelSystem
{
    [Dependency] private readonly SWGlobalWarPoliticsSystem _politics = default!;
    [Dependency] private readonly SharedPhysicsSystem _returnPhysics = default!;

    /// <summary>
    /// Prepares the ship's complete FTL clearance before undocking an idle ship or retargeting an existing flight.
    /// The permission is an exact campaign withdrawal order, never a navigation/UI request or generic bypass.
    /// </summary>
    public SWGlobalWarWithdrawalStatus TryReturnWithdrawnShip(SWGlobalWarShipWithdrawalOrder order,
        out ProtoId<SWGlobalWarPlanetPrototype>? destination)
    {
        destination = null;
        var ship = order.Ship;
        if (!_globalWarGamerule.TryGetGlobalWarGamerule(out var rule) || rule.Comp.Generation != order.Generation ||
            TerminatingOrDeleted(ship) || !TryComp<PhysicsComponent>(ship, out var physics))
            return SWGlobalWarWithdrawalStatus.WaitingForSeparation;
        if (HasComp<FTLComponent>(ship) && TryComp<SWGlobalWarTravelComponent>(ship, out var committedFlight) &&
            committedFlight.Withdrawal?.Id == order.Id &&
            _politics.ValidateShipWithdrawal(order, committedFlight.DestinationPlanet))
        {
            // An already committed group waits together for arrival clearance. A temporary obstruction must not
            // downgrade it to an unprepared group and independently teleport its passengers out of hyperspace.
            return SWGlobalWarWithdrawalStatus.Returning;
        }
        var coordinatesBefore = Transform(ship).Coordinates;
        var docks = _docking.GetDocks(ship).Select(d => d.Comp.DockJointId).Where(id => id != null).ToHashSet();
        if (TryComp<JointComponent>(ship, out var joints) && joints.GetJoints.Keys.Any(id => !docks.Contains(id)))
            return SWGlobalWarWithdrawalStatus.WaitingForSeparation;
        var candidates = rule.Comp.PlanetEntities.Where(p =>
                TryComp<SWGlobalWarPlanetComponent>(p.Value, out var target) &&
                target.OrbitOwner == order.Faction && target.SurfaceOwner == order.Faction)
            .OrderBy(p => _prototypes.Index(p.Key).Kind == SWGlobalWarSectorKind.Capital &&
                          _prototypes.Index(p.Key).InitialOwner == order.Faction ? 0 : 1)
            .ThenBy(p => p.Key.Id, StringComparer.Ordinal);
        var hasEligibleCrew = false;
        foreach (var (id, planet) in candidates)
        {
            if (!_politics.ValidateShipWithdrawal(order, planet))
                continue;
            hasEligibleCrew = true;
            if (!_planets.TryEnsureLocation(planet, SWGlobalWarPlanetLocation.Orbit, out var map, out _) ||
                !_planets.TryGetOrbitStation(planet, out _))
                continue;
            var prepared = new SWGlobalWarTravelComponent
            {
                GlobalWarGamerule = rule.Owner, Generation = order.Generation, Destination = id,
                DestinationPlanet = planet, DestinationMap = map.Value, UseStationArrival = true,
            };
            if (!TryFindArrival(ship, prepared, out var coordinates) ||
                !_politics.ValidateShipWithdrawal(order, planet) || Transform(ship).Coordinates != coordinatesBefore)
                continue;
            if (TryComp<FTLComponent>(ship, out var ftl))
            {
                // The old flight owns the grid. Never SetCoordinates a grid that still has an FTL arrival pending.
                if (!TryComp<SWGlobalWarTravelComponent>(ship, out var flight) || flight.Generation != order.Generation ||
                    !_planets.TryPrepareArrival(id, SWGlobalWarPlanetLocation.Orbit, ship, out _))
                    return SWGlobalWarWithdrawalStatus.WaitingForSeparation;
                if (flight.DestinationMap != map.Value)
                    _planets.ReleaseArrival(flight.DestinationMap, ship);
                flight.Destination = id;
                flight.DestinationPlanet = planet;
                flight.DestinationMap = map.Value;
                flight.UseStationArrival = true;
                flight.Returning = false;
                flight.DockTarget = null;
                flight.ArrivalCenter = prepared.ArrivalCenter;
                flight.ArrivalRadius = prepared.ArrivalRadius;
                flight.ArrivalAngle = prepared.ArrivalAngle;
                flight.Withdrawal = order;
                ftl.TargetCoordinates = coordinates;
                ftl.TargetAngle = prepared.ArrivalAngle;
                return SWGlobalWarWithdrawalStatus.Returning;
            }
            _docking.UndockDocks(ship);
            _transform.SetCoordinates(ship, Transform(ship), coordinates, rotation: prepared.ArrivalAngle);
            _returnPhysics.SetLinearVelocity(ship, System.Numerics.Vector2.Zero, body: physics);
            _returnPhysics.SetAngularVelocity(ship, 0, body: physics);
            destination = id;
            return SWGlobalWarWithdrawalStatus.Returned;
        }
        return hasEligibleCrew ? SWGlobalWarWithdrawalStatus.WaitingForPoint : SWGlobalWarWithdrawalStatus.WaitingForSeparation;
    }

    private void HandleWithdrawalArrival(Entity<SWGlobalWarTravelComponent> ent, SWGlobalWarShipWithdrawalOrder order,
        ref FTLBeforeArrivalEvent args)
    {
        if (!_politics.TryGetPolitics(out var politics) || politics.Comp.Generation != order.Generation ||
            politics.Comp.Relations == SWGlobalWarRelations.War)
        {
            // Withdrawal was cancelled. Resume ordinary flight validation against the already selected destination.
            ent.Comp.Withdrawal = null;
            OnBeforeArrival(ent, ref args);
            return;
        }
        if (!_politics.ValidateShipWithdrawal(order, ent.Comp.DestinationPlanet) ||
            !TryFindArrival(ent.Owner, ent.Comp, out var coordinates))
        {
            args.Cancelled = true;
            args.RetryDelay = TimeSpan.FromSeconds(10);
            return;
        }
        var ftl = Comp<FTLComponent>(ent.Owner);
        ftl.TargetCoordinates = coordinates;
        ftl.TargetAngle = ent.Comp.ArrivalAngle;
    }
}
